using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.BeamNG;

/// <summary>
/// Merges BeamNG's standard OutGauge telemetry with the companion Rally Mode bridge.
/// The two UDP streams are deliberately separate: OutGauge remains a supported game
/// protocol while the bridge can evolve with Rally Mode independently.
/// </summary>
public sealed class BeamNGGameDataReader : UdpGameDataReader
{
    public const int DefaultOutGaugePort = 4444;
    public const int LegacyIncorrectOutGaugePort = 44444;
    private const uint HandbrakeLight = 1u << 2;
    private const float FallbackMaximumRpm = 10000f;
    private const float DamageActivityThreshold = 0.001f;
    private const float BodyDamageEventThreshold = 0.01f;
    private static readonly TimeSpan BridgeConnectionTimeout = TimeSpan.FromSeconds(5);

    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    private readonly object _sync = new();

    private UdpReceiver? _bridgeReceiver;
    private SocketError? _bridgeListenerSocketError;
    private GameState _gameState;
    private GameData _lastGameData;
    private GameData _currentGameData;
    private BeamNGOutGaugePacket? _lastOutGauge;
    private BeamNGRallySnapshot _rallySnapshot = new();
    private DateTime _firstOutGaugeUtc;
    private DateTime? _lastBridgePacketUtc;
    private bool _reportedUnsupportedProtocol;
    private string _lastStagePacenoteFingerprint = string.Empty;
    private readonly Dictionary<string, DamageValue> _damageValues = new(StringComparer.OrdinalIgnoreCase);

    private event Action<GameData, GameData>? _onNewGameData;
    private event Action<GameStateChangeEvent>? _onGameStateChanged;
    private event Action<CarDamageEvent>? _onCarDamaged;

    public override event Action<GameData, GameData> onNewGameData
    {
        add => _onNewGameData += value;
        remove => _onNewGameData -= value;
    }

    public override event Action<GameStateChangeEvent> onGameStateChanged
    {
        add => _onGameStateChanged += value;
        remove => _onGameStateChanged -= value;
    }

    public override event Action<CarDamageEvent> onCarDamaged
    {
        add => _onCarDamaged += value;
        remove => _onCarDamaged -= value;
    }

    public override GameState GameState
    {
        get
        {
            lock (_sync)
            {
                return _gameState;
            }
        }
        set => PublishGameStateChange(value);
    }

    public bool IsBridgeListening
    {
        get
        {
            lock (_sync)
            {
                return _bridgeReceiver is not null;
            }
        }
    }

    public SocketError? BridgeListenerSocketError
    {
        get
        {
            lock (_sync)
            {
                return _bridgeListenerSocketError;
            }
        }
    }

    public DateTime? LastBridgePacketUtc
    {
        get
        {
            lock (_sync)
            {
                return _lastBridgePacketUtc;
            }
        }
    }

    public bool IsBridgeConnected
    {
        get
        {
            lock (_sync)
            {
                return _lastBridgePacketUtc is { } lastBridgePacketUtc &&
                       DateTime.UtcNow - lastBridgePacketUtc <= BridgeConnectionTimeout;
            }
        }
    }

    public override GameData LastGameData
    {
        get
        {
            lock (_sync)
            {
                return _lastGameData;
            }
        }
        set
        {
            lock (_sync)
            {
                _lastGameData = value;
            }
        }
    }

    public override GameData CurrentGameData
    {
        get
        {
            lock (_sync)
            {
                return _currentGameData;
            }
        }
        set
        {
            lock (_sync)
            {
                _currentGameData = value;
            }
        }
    }

    public override string TrackName
    {
        get
        {
            lock (_sync)
            {
                if (!string.IsNullOrWhiteSpace(_rallySnapshot.StageName))
                {
                    return _rallySnapshot.StageName;
                }

                return string.IsNullOrWhiteSpace(_rallySnapshot.StageId)
                    ? "BeamNG.drive Rally"
                    : _rallySnapshot.StageId;
            }
        }
    }

    public override string CarName
    {
        get
        {
            lock (_sync)
            {
                return string.IsNullOrWhiteSpace(_rallySnapshot.VehicleName)
                    ? "BeamNG.drive"
                    : _rallySnapshot.VehicleName;
            }
        }
    }

    public override string CarClass
    {
        get
        {
            lock (_sync)
            {
                return _rallySnapshot.VehicleClass;
            }
        }
    }

    public override bool Initialize(IGame game)
    {
        if (!base.Initialize(game))
        {
            return false;
        }

        UdpReceiver? bridgeReceiver = null;
        SocketError? bridgeListenerSocketError = null;
        try
        {
            bridgeReceiver = new UdpReceiver();
            bridgeReceiver.onNewMessage += OnBridgeUdpMessage;
            bridgeReceiver.StartListening(IPAddress.Loopback, BeamNGRallyBridgeConfig.DefaultPort);
        }
        catch (Exception exception)
        {
            if (bridgeReceiver is not null)
            {
                bridgeReceiver.onNewMessage -= OnBridgeUdpMessage;
                bridgeReceiver.Dispose();
            }

            bridgeReceiver = null;
            bridgeListenerSocketError = exception switch
            {
                PortAlreadyInUseException => SocketError.AddressAlreadyInUse,
                PortAccessDeniedException => SocketError.AccessDenied,
                SocketException socketException => socketException.SocketErrorCode,
                _ => null
            };
            Logger.Warn(exception,
                "BeamNG Rally bridge listener could not start on UDP port {0}; BeamNG initialization is aborted.",
                BeamNGRallyBridgeConfig.DefaultPort);
            base.Uninitialize(game);
            throw;
        }

        lock (_sync)
        {
            _bridgeReceiver = bridgeReceiver;
            _bridgeListenerSocketError = bridgeListenerSocketError;
            _gameState = GameState.Unknown;
            _lastGameData = default;
            _currentGameData = default;
            _lastOutGauge = null;
            _rallySnapshot = new BeamNGRallySnapshot();
            _firstOutGaugeUtc = default;
            _lastBridgePacketUtc = null;
            _reportedUnsupportedProtocol = false;
            _lastStagePacenoteFingerprint = string.Empty;
            _damageValues.Clear();
        }

        Logger.Info("BeamNG OutGauge and Rally bridge listeners initialized on {0} and {1}.",
            GetOutGaugePort(game), BeamNGRallyBridgeConfig.DefaultPort);
        return true;
    }

    public override void Uninitialize(IGame game)
    {
        UdpReceiver? bridgeReceiver;
        lock (_sync)
        {
            bridgeReceiver = _bridgeReceiver;
            _bridgeReceiver = null;
            _bridgeListenerSocketError = null;
            _lastBridgePacketUtc = null;
            _damageValues.Clear();
        }

        if (bridgeReceiver is not null)
        {
            bridgeReceiver.onNewMessage -= OnBridgeUdpMessage;
            bridgeReceiver.Dispose();
        }

        base.Uninitialize(game);
    }

    public override void onNewUdpMessage(byte[] oldMsg, byte[] newMsg)
    {
        base.onNewUdpMessage(oldMsg, newMsg);
        if (!BeamNGOutGaugePacket.TryParse(newMsg, out var outGauge) || outGauge is null)
        {
            return;
        }

        GameData oldData;
        GameData newData;
        lock (_sync)
        {
            _lastOutGauge = outGauge;
            _firstOutGaugeUtc = _firstOutGaugeUtc == default ? DateTime.UtcNow : _firstOutGaugeUtc;
            UpdateTelemetrySnapshotLocked(outGauge);
            oldData = _currentGameData;
            newData = CreateGameDataLocked();
            _lastGameData = oldData;
            _currentGameData = newData;
        }

        _onNewGameData?.Invoke(oldData, newData);
    }

    private void OnBridgeUdpMessage(byte[] _, byte[] message)
    {
        if (!BeamNGRallyBridgePacket.TryParse(message, out var packet) || packet is null)
        {
            return;
        }

        if (packet.ProtocolVersion != BeamNGRallyBridgePacket.SupportedProtocolVersion)
        {
            if (!_reportedUnsupportedProtocol)
            {
                _reportedUnsupportedProtocol = true;
                Logger.Warn("Ignoring BeamNG Rally bridge protocol version {0}; supported version is {1}.",
                    packet.ProtocolVersion, BeamNGRallyBridgePacket.SupportedProtocolVersion);
            }
            return;
        }

        GameStateChangeEvent? stateChange;
        GameData oldData;
        GameData newData;
        IReadOnlyList<BeamNGRallyPacenote>? stagePacenotesToSave = null;
        BeamNGRallyStage? stageToSave = null;
        List<CarDamageEvent>? damageEvents;

        lock (_sync)
        {
            _lastBridgePacketUtc = DateTime.UtcNow;
            ApplyBridgePacketLocked(packet);
            damageEvents = ApplyDamagePacketLocked(packet.Damage);
            stateChange = GetStageStateChangeLocked();
            oldData = _currentGameData;
            newData = CreateGameDataLocked();
            _lastGameData = oldData;
            _currentGameData = newData;

            if (packet.Pacenotes is { Count: > 0 } && ShouldSaveStagePacenotesLocked(packet.Stage, packet.Pacenotes))
            {
                stageToSave = packet.Stage;
                stagePacenotesToSave = packet.Pacenotes;
            }
        }

        if (stagePacenotesToSave is not null)
        {
            try
            {
                var exportPath = BeamNGRallyStageStore.SaveStagePacenotes(stageToSave, stagePacenotesToSave);
                Logger.Info("Saved {0} BeamNG Rally pacenotes to {1}.", stagePacenotesToSave.Count, exportPath);
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "Failed to save the BeamNG Rally stage pacenote export.");
            }
        }

        stateChange?.Let(_onGameStateChanged);
        if (damageEvents is not null)
        {
            foreach (var damageEvent in damageEvents)
            {
                _onCarDamaged?.Invoke(damageEvent);
            }
        }
        _onNewGameData?.Invoke(oldData, newData);
    }

    private void ApplyBridgePacketLocked(BeamNGRallyBridgePacket packet)
    {
        _rallySnapshot.ProtocolVersion = packet.ProtocolVersion;
        if (!string.IsNullOrWhiteSpace(packet.Status))
        {
            _rallySnapshot.BridgeStatus = packet.Status;
        }
        if (!string.IsNullOrWhiteSpace(packet.Error))
        {
            _rallySnapshot.BridgeError = packet.Error;
        }
        if (!string.IsNullOrWhiteSpace(packet.VehicleName))
        {
            _rallySnapshot.VehicleName = packet.VehicleName;
        }
        if (!string.IsNullOrWhiteSpace(packet.VehicleClass))
        {
            _rallySnapshot.VehicleClass = packet.VehicleClass;
        }

        if (packet.Telemetry is not null)
        {
            var telemetry = packet.Telemetry;
            if (telemetry.VehicleId > 0 && telemetry.VehicleId != _rallySnapshot.VehicleId)
            {
                _rallySnapshot.VehicleId = telemetry.VehicleId;
                _rallySnapshot.MaxRPM = 0f;
                _rallySnapshot.IdleRPM = 0f;
                ResetDamageStateLocked(telemetry.VehicleId);
            }

            var maxRpm = Normalize(telemetry.MaxRPM);
            if (maxRpm > 0f)
            {
                _rallySnapshot.MaxRPM = maxRpm;
            }

            var idleRpm = Normalize(telemetry.IdleRPM);
            if (idleRpm > 0f)
            {
                _rallySnapshot.IdleRPM = idleRpm;
            }
        }

        if (packet.Stage is null && string.Equals(packet.Type, "status", StringComparison.OrdinalIgnoreCase))
        {
            _rallySnapshot.ClearStageData();
            _lastStagePacenoteFingerprint = string.Empty;
        }

        if (packet.Stage is not null)
        {
            _rallySnapshot.StageId = packet.Stage.Id;
            _rallySnapshot.StageName = packet.Stage.Name;
            _rallySnapshot.StageActive = packet.Stage.IsActive;
            _rallySnapshot.StageCountingDown = packet.Stage.IsCountingDown;
            _rallySnapshot.StageFinished = packet.Stage.IsFinished;
            _rallySnapshot.StageTimeSeconds = Normalize(packet.Stage.StageTimeSeconds);
            _rallySnapshot.DistanceMeters = Normalize(packet.Stage.DistanceMeters);
            _rallySnapshot.TotalDistanceMeters = Normalize(packet.Stage.TotalDistanceMeters);
            _rallySnapshot.Completion = float.Clamp(Normalize(packet.Stage.Completion), 0f, 1f);
        }

        if (packet.Pacenote is not null)
        {
            _rallySnapshot.CurrentPacenoteId = packet.Pacenote.Id;
            _rallySnapshot.CurrentPacenoteName = packet.Pacenote.Name;
            _rallySnapshot.CurrentPacenoteText = packet.Pacenote.Text;
            _rallySnapshot.CurrentPacenoteVisual = packet.Pacenote.Visual;
            _rallySnapshot.CurrentPacenoteDistanceBeforeMeters = Normalize(packet.Pacenote.DistanceBeforeMeters);
            _rallySnapshot.CurrentPacenoteDistanceAfterMeters = Normalize(packet.Pacenote.DistanceAfterMeters);
            _rallySnapshot.CurrentPacenoteDistanceToMeters = Normalize(packet.Pacenote.DistanceToMeters);
        }

        if (packet.Pacenotes is not null)
        {
            _rallySnapshot.ExportedPacenoteCount = packet.Pacenotes.Count;
        }
    }

    private List<CarDamageEvent>? ApplyDamagePacketLocked(BeamNGRallyDamage? damage)
    {
        if (damage is null || damage.VehicleId <= 0 || damage.Current.Count == 0)
        {
            return null;
        }

        if (_rallySnapshot.VehicleId > 0 && _rallySnapshot.VehicleId != damage.VehicleId)
        {
            // Ignore a callback from an old vehicle after the player has switched cars.
            return null;
        }

        if (_rallySnapshot.VehicleId == 0)
        {
            _rallySnapshot.VehicleId = damage.VehicleId;
            ResetDamageStateLocked(damage.VehicleId);
        }
        else if (_rallySnapshot.Damage.VehicleId != damage.VehicleId)
        {
            ResetDamageStateLocked(damage.VehicleId);
        }

        UpdateDamageSummaryLocked(damage);
        _rallySnapshot.CarDamage = CreateCarDamageSnapshot(damage);
        var damageEvents = CreateDamageEventsLocked(damage);
        CaptureDamageValuesLocked(damage.Current);
        if (!string.IsNullOrWhiteSpace(_rallySnapshot.Damage.LastChanges))
        {
            Logger.Info("BeamNG damage update for vehicle {0}: {1}.",
                damage.VehicleId, _rallySnapshot.Damage.LastChanges);
        }
        return damageEvents;
    }

    private void ResetDamageStateLocked(int vehicleId)
    {
        _damageValues.Clear();
        _rallySnapshot.Damage = new BeamNGRallyDamageSummary
        {
            VehicleId = vehicleId
        };
        _rallySnapshot.CarDamage = CarDamageSnapshot.Unavailable("beamng");
    }

    private void UpdateDamageSummaryLocked(BeamNGRallyDamage damage)
    {
        var summary = new BeamNGRallyDamageSummary
        {
            VehicleId = damage.VehicleId,
            LastChanges = BuildDamageChangeSummary(damage.Changed)
        };
        var activeDamage = new List<string>();

        foreach (var group in damage.Current)
        {
            foreach (var part in group.Value)
            {
                if (!TryReadDamageValue(part.Value, out var value) || !value.IsActive)
                {
                    continue;
                }

                summary.ActiveDamageCount++;
                if (activeDamage.Count < 12)
                {
                    activeDamage.Add($"{group.Key}/{part.Key}");
                }

                var groupName = group.Key.ToLowerInvariant();
                if (groupName == "body")
                {
                    UpdateBodyDamageSummary(summary, part.Key, value.Magnitude);
                    continue;
                }

                if (groupName == "wheels")
                {
                    summary.DamagedWheelCount++;
                    continue;
                }

                if (groupName == "engine")
                {
                    summary.HasEngineDamage = true;
                    if (part.Key.Contains("radiator", StringComparison.OrdinalIgnoreCase) ||
                        part.Key.Contains("coolant", StringComparison.OrdinalIgnoreCase))
                    {
                        summary.HasRadiatorDamage = true;
                    }
                    continue;
                }

                if (groupName == "powertrain")
                {
                    summary.HasPowertrainDamage = true;
                    continue;
                }

                if (groupName == "gearbox")
                {
                    summary.HasGearboxDamage = true;
                    continue;
                }

                if (groupName is "energystorage" or "pressuretank")
                {
                    summary.HasEnergyStorageDamage = true;
                    continue;
                }

                if (groupName == "drivingdynamics")
                {
                    summary.HasDrivingDynamicsDamage = true;
                }
            }
        }

        summary.ActiveDamage = string.Join(", ", activeDamage);
        _rallySnapshot.Damage = summary;
    }

    private List<CarDamageEvent>? CreateDamageEventsLocked(BeamNGRallyDamage damage)
    {
        if (damage.Changed.Count == 0)
        {
            return null;
        }

        List<CarDamageEvent>? events = null;
        foreach (var group in damage.Changed)
        {
            foreach (var part in group.Value)
            {
                if (!TryReadDamageValue(part.Value, out var currentValue))
                {
                    continue;
                }

                var key = GetDamageValueKey(group.Key, part.Key);
                _damageValues.TryGetValue(key, out var previousValue);
                var damageType = ClassifyDamage(group.Key, part.Key);
                if (damageType is null || !ShouldRaiseDamageEvent(damageType.Value, currentValue, previousValue))
                {
                    continue;
                }

                var parameters = new Dictionary<string, object>
                {
                    [CarDamageConstants.GROUP] = group.Key,
                    [CarDamageConstants.PART] = part.Key,
                    [CarDamageConstants.VALUE] = currentValue.Magnitude,
                    [CarDamageConstants.PREVIOUS_VALUE] = previousValue.Magnitude
                };
                if (damageType == CarDamage.Wheel && TryMapBeamNGWheelIndex(part.Key, out var wheelIndex))
                {
                    parameters[CarDamageConstants.WHEELINDEX] = wheelIndex;
                }

                events ??= new List<CarDamageEvent>();
                events.Add(new CarDamageEvent
                {
                    DamageType = damageType.Value,
                    Parameters = parameters
                });
            }
        }

        return events;
    }

    private void CaptureDamageValuesLocked(Dictionary<string, Dictionary<string, JsonElement>> currentDamage)
    {
        _damageValues.Clear();
        foreach (var group in currentDamage)
        {
            foreach (var part in group.Value)
            {
                if (TryReadDamageValue(part.Value, out var value))
                {
                    _damageValues[GetDamageValueKey(group.Key, part.Key)] = value;
                }
            }
        }
    }

    private static void UpdateBodyDamageSummary(BeamNGRallyDamageSummary summary, string partName, float magnitude)
    {
        magnitude = Math.Clamp(magnitude, 0f, 1f);
        summary.BodyDamageMaximum = Math.Max(summary.BodyDamageMaximum, magnitude);
        switch (partName.ToUpperInvariant())
        {
            case "FL":
                summary.BodyDamageFrontLeft = magnitude;
                break;
            case "FR":
                summary.BodyDamageFrontRight = magnitude;
                break;
            case "RL":
                summary.BodyDamageRearLeft = magnitude;
                break;
            case "RR":
                summary.BodyDamageRearRight = magnitude;
                break;
        }
    }

    private static string BuildDamageChangeSummary(Dictionary<string, Dictionary<string, JsonElement>> changedDamage)
    {
        var changes = new List<string>();
        foreach (var group in changedDamage)
        {
            foreach (var part in group.Value)
            {
                if (changes.Count >= 12)
                {
                    return string.Join(", ", changes);
                }

                if (!TryReadDamageValue(part.Value, out var value))
                {
                    continue;
                }

                changes.Add(value.IsActive
                    ? $"{group.Key}/{part.Key}"
                    : $"{group.Key}/{part.Key} cleared");
            }
        }

        return string.Join(", ", changes);
    }

    private static CarDamage? ClassifyDamage(string groupName, string partName)
    {
        if (string.Equals(groupName, "wheels", StringComparison.OrdinalIgnoreCase) &&
            partName.StartsWith("tire", StringComparison.OrdinalIgnoreCase))
        {
            return CarDamage.Wheel;
        }

        if (string.Equals(groupName, "body", StringComparison.OrdinalIgnoreCase))
        {
            return CarDamage.Bodywork;
        }

        if (!string.Equals(groupName, "engine", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (partName.Contains("radiator", StringComparison.OrdinalIgnoreCase) ||
            partName.Contains("coolant", StringComparison.OrdinalIgnoreCase))
        {
            return CarDamage.Radiator;
        }

        if (partName.Contains("waterpump", StringComparison.OrdinalIgnoreCase))
        {
            return CarDamage.WaterPump;
        }

        return CarDamage.Engine;
    }

    private static bool ShouldRaiseDamageEvent(CarDamage damageType, DamageValue current, DamageValue previous)
    {
        if (damageType == CarDamage.Bodywork)
        {
            return current.Magnitude - previous.Magnitude >= BodyDamageEventThreshold;
        }

        return current.IsActive && !previous.IsActive;
    }

    private static bool TryReadDamageValue(JsonElement element, out DamageValue value)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.True:
                value = new DamageValue(true, 1f);
                return true;
            case JsonValueKind.False:
            case JsonValueKind.Null:
                value = default;
                return true;
            case JsonValueKind.Number when element.TryGetDouble(out var number) && double.IsFinite(number):
                var magnitude = (float)Math.Clamp(number, 0d, float.MaxValue);
                value = new DamageValue(magnitude > DamageActivityThreshold, magnitude);
                return true;
            case JsonValueKind.String:
                var text = element.GetString();
                if (bool.TryParse(text, out var booleanValue))
                {
                    value = new DamageValue(booleanValue, booleanValue ? 1f : 0f);
                    return true;
                }

                if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var stringMagnitude) &&
                    float.IsFinite(stringMagnitude))
                {
                    stringMagnitude = Math.Max(0f, stringMagnitude);
                    value = new DamageValue(stringMagnitude > DamageActivityThreshold, stringMagnitude);
                    return true;
                }

                break;
        }

        value = default;
        return false;
    }

    private static CarDamageSnapshot CreateCarDamageSnapshot(BeamNGRallyDamage damage)
    {
        var components = new List<CarDamageComponentSnapshot>();
        var rawComponents = new List<CarDamageRawComponentSnapshot>();

        foreach (var group in damage.Current)
        {
            foreach (var part in group.Value)
            {
                var rawId = $"{group.Key}/{part.Key}";
                if (!TryReadDamageValue(part.Value, out var value))
                {
                    rawComponents.Add(new CarDamageRawComponentSnapshot(
                        rawId,
                        group.Key,
                        part.Key,
                        false,
                        false,
                        0,
                        null,
                        null,
                        null,
                        Array.Empty<float>(),
                        Array.Empty<int>(),
                        part.Value.GetRawText()));
                    continue;
                }

                var severity = GetBeamNGDamageSeverity(value);
                rawComponents.Add(new CarDamageRawComponentSnapshot(
                    rawId,
                    group.Key,
                    part.Key,
                    true,
                    value.IsActive,
                    severity,
                    value.Magnitude,
                    null,
                    null,
                    Array.Empty<float>(),
                    Array.Empty<int>(),
                    part.Value.GetRawText()));

                if (TryDescribeBeamNGDamageComponent(group.Key, part.Key, out var kind, out var location))
                {
                    components.Add(new CarDamageComponentSnapshot(
                        CarDamageIds.ComponentId(kind, location),
                        kind,
                        location,
                        true,
                        value.IsActive,
                        severity,
                        value.Magnitude));
                }
            }
        }

        var groups = new[]
        {
            CreateBeamNGDamageGroup(
                CarDamageIds.Engine,
                components.Where(component => string.Equals(component.Kind, CarDamageIds.Engine, StringComparison.OrdinalIgnoreCase))),
            CreateBeamNGDamageGroup(
                CarDamageIds.Radiator,
                components.Where(component => string.Equals(component.Kind, CarDamageIds.Radiator, StringComparison.OrdinalIgnoreCase))),
            CreateBeamNGDamageGroup(
                CarDamageIds.Differential,
                components.Where(component => string.Equals(component.Kind, CarDamageIds.Differential, StringComparison.OrdinalIgnoreCase))),
            CreateBeamNGDamageGroup(
                CarDamageIds.FrontRight,
                components.Where(component => string.Equals(component.Location, CarDamageIds.FrontRight, StringComparison.OrdinalIgnoreCase))),
            CreateBeamNGDamageGroup(
                CarDamageIds.RearRight,
                components.Where(component => string.Equals(component.Location, CarDamageIds.RearRight, StringComparison.OrdinalIgnoreCase))),
            CreateBeamNGDamageGroup(
                CarDamageIds.RearLeft,
                components.Where(component => string.Equals(component.Location, CarDamageIds.RearLeft, StringComparison.OrdinalIgnoreCase))),
            CreateBeamNGDamageGroup(
                CarDamageIds.FrontLeft,
                components.Where(component => string.Equals(component.Location, CarDamageIds.FrontLeft, StringComparison.OrdinalIgnoreCase)))
        };

        return CarDamageSnapshot.Create(
            "beamng",
            DateTimeOffset.UtcNow,
            groups,
            components,
            rawComponents);
    }

    private static CarDamageGroupSnapshot CreateBeamNGDamageGroup(
        string id,
        IEnumerable<CarDamageComponentSnapshot> sourceComponents)
    {
        var components = sourceComponents.ToArray();
        return new CarDamageGroupSnapshot(
            id,
            components.Length > 0,
            components.Length > 0,
            components.DefaultIfEmpty().Max(component => component?.Severity ?? 0),
            components
                .Select(component => component.Id)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(componentId => componentId, StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    private static int GetBeamNGDamageSeverity(DamageValue value)
    {
        if (!value.IsActive)
        {
            return 0;
        }

        if (value.Magnitude >= 0.9f)
        {
            return 4;
        }

        if (value.Magnitude >= 0.6f)
        {
            return 3;
        }

        if (value.Magnitude >= 0.25f)
        {
            return 2;
        }

        return 1;
    }

    private static bool TryDescribeBeamNGDamageComponent(
        string groupName,
        string partName,
        out string kind,
        out string location)
    {
        var normalizedGroup = NormalizeBeamNGDamageName(groupName);
        var normalizedPart = NormalizeBeamNGDamageName(partName);
        location = GetBeamNGDamageLocation(normalizedPart);

        if (normalizedGroup == "body")
        {
            if (normalizedPart.Contains("frontbumper", StringComparison.Ordinal))
            {
                kind = CarDamageIds.FrontBumper;
                location = string.Empty;
                return true;
            }

            if (normalizedPart.Contains("rearbumper", StringComparison.Ordinal))
            {
                kind = CarDamageIds.RearBumper;
                location = string.Empty;
                return true;
            }

            if (normalizedPart.Contains("leftdoor", StringComparison.Ordinal))
            {
                kind = CarDamageIds.LeftDoor;
                location = string.Empty;
                return true;
            }

            if (normalizedPart.Contains("rightdoor", StringComparison.Ordinal))
            {
                kind = CarDamageIds.RightDoor;
                location = string.Empty;
                return true;
            }

            kind = CarDamageIds.Bodywork;
            return true;
        }

        if (normalizedGroup == "wheels")
        {
            if (normalizedPart.Contains("tire", StringComparison.Ordinal) || normalizedPart.Contains("tyre", StringComparison.Ordinal))
            {
                kind = CarDamageIds.Tyre;
                return true;
            }

            if (normalizedPart.Contains("brake", StringComparison.Ordinal))
            {
                kind = CarDamageIds.Brake;
                return true;
            }

            if (normalizedPart.Contains("susp", StringComparison.Ordinal)
                || normalizedPart.Contains("damper", StringComparison.Ordinal)
                || normalizedPart.Contains("shock", StringComparison.Ordinal)
                || normalizedPart.Contains("controlarm", StringComparison.Ordinal))
            {
                kind = CarDamageIds.Suspension;
                return true;
            }

            kind = CarDamageIds.Wheel;
            return true;
        }

        if (normalizedGroup == "engine")
        {
            kind = normalizedPart.Contains("radiator", StringComparison.Ordinal)
                || normalizedPart.Contains("coolant", StringComparison.Ordinal)
                || normalizedPart.Contains("waterpump", StringComparison.Ordinal)
                || normalizedPart.Contains("intercooler", StringComparison.Ordinal)
                ? CarDamageIds.Radiator
                : CarDamageIds.Engine;
            location = string.Empty;
            return true;
        }

        if (normalizedGroup is "powertrain" or "differential")
        {
            kind = CarDamageIds.Differential;
            location = string.Empty;
            return true;
        }

        if (normalizedGroup == "gearbox")
        {
            kind = CarDamageIds.Engine;
            location = string.Empty;
            return true;
        }

        if (normalizedGroup == "drivingdynamics")
        {
            kind = CarDamageIds.Suspension;
            return true;
        }

        kind = string.Empty;
        location = string.Empty;
        return false;
    }

    private static string GetBeamNGDamageLocation(string normalizedPart)
    {
        if (normalizedPart.Contains("frontleft", StringComparison.Ordinal)
            || normalizedPart.Contains("leftfront", StringComparison.Ordinal)
            || normalizedPart.EndsWith("fl", StringComparison.Ordinal))
        {
            return CarDamageIds.FrontLeft;
        }

        if (normalizedPart.Contains("frontright", StringComparison.Ordinal)
            || normalizedPart.Contains("rightfront", StringComparison.Ordinal)
            || normalizedPart.EndsWith("fr", StringComparison.Ordinal))
        {
            return CarDamageIds.FrontRight;
        }

        if (normalizedPart.Contains("rearleft", StringComparison.Ordinal)
            || normalizedPart.Contains("leftrear", StringComparison.Ordinal)
            || normalizedPart.EndsWith("rl", StringComparison.Ordinal))
        {
            return CarDamageIds.RearLeft;
        }

        if (normalizedPart.Contains("rearright", StringComparison.Ordinal)
            || normalizedPart.Contains("rightrear", StringComparison.Ordinal)
            || normalizedPart.EndsWith("rr", StringComparison.Ordinal))
        {
            return CarDamageIds.RearRight;
        }

        return string.Empty;
    }

    private static string NormalizeBeamNGDamageName(string value)
    {
        return value
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
    }

    private static bool TryMapBeamNGWheelIndex(string partName, out int wheelIndex)
    {
        var wheelName = partName.StartsWith("tire", StringComparison.OrdinalIgnoreCase)
            ? partName[4..]
            : partName;
        var normalizedName = wheelName
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToUpperInvariant();

        wheelIndex = normalizedName switch
        {
            "FL" or "FRONTLEFT" or "LEFTFRONT" => 0,
            "FR" or "FRONTRIGHT" or "RIGHTFRONT" => 1,
            "RL" or "REARLEFT" or "LEFTREAR" => 2,
            "RR" or "REARRIGHT" or "RIGHTREAR" => 3,
            _ => -1
        };
        return wheelIndex >= 0;
    }

    private static string GetDamageValueKey(string groupName, string partName)
    {
        return string.Concat(groupName, "\u001f", partName);
    }

    private void UpdateTelemetrySnapshotLocked(BeamNGOutGaugePacket outGauge)
    {
        _rallySnapshot.FuelFraction = Normalize(outGauge.FuelFraction);
        _rallySnapshot.EngineTemperatureCelsius = Normalize(outGauge.EngineTemperatureCelsius);
        _rallySnapshot.OilTemperatureCelsius = Normalize(outGauge.OilTemperatureCelsius);
        _rallySnapshot.TurboBar = Normalize(outGauge.TurboBar);
    }

    private GameStateChangeEvent? GetStageStateChangeLocked()
    {
        GameState targetState;
        if (string.IsNullOrWhiteSpace(_rallySnapshot.StageId))
        {
            targetState = GameState.Unknown;
        }
        else if (_rallySnapshot.StageFinished)
        {
            targetState = GameState.RaceEnd;
        }
        else if (_rallySnapshot.StageCountingDown)
        {
            targetState = GameState.CountingDown;
        }
        else if (_rallySnapshot.StageActive)
        {
            targetState = _gameState is GameState.Unknown or GameState.RaceEnd
                ? GameState.RaceBegin
                : GameState.Racing;
        }
        else
        {
            targetState = GameState.RaceBegin;
        }

        return CreateStateChangeLocked(targetState);
    }

    private void PublishGameStateChange(GameState state)
    {
        GameStateChangeEvent? stateChange;
        lock (_sync)
        {
            stateChange = CreateStateChangeLocked(state);
        }

        stateChange?.Let(_onGameStateChanged);
    }

    private GameStateChangeEvent? CreateStateChangeLocked(GameState state)
    {
        if (_gameState == state)
        {
            return null;
        }

        var previousState = _gameState;
        _gameState = state;
        var parameters = new Dictionary<string, object>();
        if (state == GameState.RaceEnd)
        {
            parameters[GameStateRaceEndProperty.FINISH_TIME] = _rallySnapshot.StageTimeSeconds;
            parameters[GameStateRaceEndProperty.FINISH_STATE] = GameStateRaceEnd.Normal;
        }

        return new GameStateChangeEvent
        {
            LastGameState = previousState,
            NewGameState = state,
            Parameters = parameters
        };
    }

    private GameData CreateGameDataLocked()
    {
        var now = DateTime.UtcNow;
        var snapshot = _rallySnapshot.Clone();
        var outGauge = _lastOutGauge;
        var hasStage = !string.IsNullOrWhiteSpace(snapshot.StageId);
        var elapsedSeconds = _firstOutGaugeUtc == default ? 0f : (float)(now - _firstOutGaugeUtc).TotalSeconds;
        var rpm = Normalize(outGauge?.Rpm ?? 0f);
        var maxRpm = ResolveMaximumRpm(snapshot.MaxRPM, rpm);

        return new GameData
        {
            TimeStamp = now,
            Time = hasStage ? snapshot.StageTimeSeconds : elapsedSeconds,
            LapTime = hasStage ? snapshot.StageTimeSeconds : elapsedSeconds,
            LapDistance = snapshot.DistanceMeters,
            CompletionRate = snapshot.Completion,
            TrackLength = snapshot.TotalDistanceMeters,
            Speed = Normalize(outGauge?.SpeedMetersPerSecond ?? 0f) * 3.6f,
            RPM = rpm,
            MaxRPM = maxRpm,
            IdleRPM = Math.Clamp(Normalize(snapshot.IdleRPM), 0f, maxRpm),
            Gear = outGauge?.Gear ?? 0,
            Throttle = Normalize(outGauge?.Throttle ?? 0f),
            Brake = Normalize(outGauge?.Brake ?? 0f),
            Clutch = Normalize(outGauge?.Clutch ?? 0f),
            HandBrake = outGauge is not null && (outGauge.ShowLights & HandbrakeLight) != 0 ? 1f : 0f,
            HandBrakeValid = outGauge is not null,
            ShiftLightsRPMValid = false,
            CarDamage = snapshot.CarDamage,
            GameSpecificData = snapshot
        };
    }

    private bool ShouldSaveStagePacenotesLocked(BeamNGRallyStage? stage, IReadOnlyList<BeamNGRallyPacenote> pacenotes)
    {
        var stageId = stage?.Id ?? _rallySnapshot.StageId;
        var fingerprint = string.Join("|", new[]
        {
            stageId,
            pacenotes.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            pacenotes[0].Id,
            pacenotes[^1].Id
        });

        if (string.Equals(_lastStagePacenoteFingerprint, fingerprint, StringComparison.Ordinal))
        {
            return false;
        }

        _lastStagePacenoteFingerprint = fingerprint;
        return true;
    }

    private static int GetOutGaugePort(IGame game)
    {
        return game.GameConfigurations.TryGetValue(UdpGameConfig.Name, out var config) && config is UdpGameConfig udpConfig
            ? udpConfig.Port
            : DefaultOutGaugePort;
    }

    private static float Normalize(float value)
    {
        return float.IsFinite(value) ? value : 0f;
    }

    private static float ResolveMaximumRpm(float bridgeMaximumRpm, float currentRpm)
    {
        var reportedMaximumRpm = Normalize(bridgeMaximumRpm);
        if (reportedMaximumRpm > 0f)
        {
            return reportedMaximumRpm;
        }

        // Older bridge builds did not provide an RPM limit. Avoid the dashboard's
        // zero-denominator path until a current bridge reports the real vehicle value.
        return Math.Max(FallbackMaximumRpm, currentRpm * 1.1f);
    }

    private readonly record struct DamageValue(bool IsActive, float Magnitude);
}

internal static class BeamNGDelegateExtensions
{
    public static void Let<T>(this T? value, Action<T>? action)
        where T : class
    {
        if (value is not null)
        {
            action?.Invoke(value);
        }
    }
}
