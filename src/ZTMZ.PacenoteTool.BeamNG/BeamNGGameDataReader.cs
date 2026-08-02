using System;
using System.Collections.Generic;
using System.Net;
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
    public const int DefaultOutGaugePort = 44444;
    private const uint HandbrakeLight = 1u << 2;

    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    private readonly object _sync = new();

    private UdpReceiver? _bridgeReceiver;
    private GameState _gameState;
    private GameData _lastGameData;
    private GameData _currentGameData;
    private BeamNGOutGaugePacket? _lastOutGauge;
    private BeamNGRallySnapshot _rallySnapshot = new();
    private DateTime _firstOutGaugeUtc;
    private bool _reportedUnsupportedProtocol;
    private string _lastStagePacenoteFingerprint = string.Empty;

    private event Action<GameData, GameData>? _onNewGameData;
    private event Action<GameStateChangeEvent>? _onGameStateChanged;

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
            Logger.Warn(exception,
                "BeamNG Rally bridge listener could not start on UDP port {0}; OutGauge telemetry will remain available.",
                BeamNGRallyBridgeConfig.DefaultPort);
        }

        lock (_sync)
        {
            _bridgeReceiver = bridgeReceiver;
            _gameState = GameState.Unknown;
            _lastGameData = default;
            _currentGameData = default;
            _lastOutGauge = null;
            _rallySnapshot = new BeamNGRallySnapshot();
            _firstOutGaugeUtc = default;
            _reportedUnsupportedProtocol = false;
            _lastStagePacenoteFingerprint = string.Empty;
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

        lock (_sync)
        {
            ApplyBridgePacketLocked(packet);
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

        return new GameData
        {
            TimeStamp = now,
            Time = hasStage ? snapshot.StageTimeSeconds : elapsedSeconds,
            LapTime = hasStage ? snapshot.StageTimeSeconds : elapsedSeconds,
            LapDistance = snapshot.DistanceMeters,
            CompletionRate = snapshot.Completion,
            TrackLength = snapshot.TotalDistanceMeters,
            Speed = Normalize(outGauge?.SpeedMetersPerSecond ?? 0f) * 3.6f,
            RPM = Normalize(outGauge?.Rpm ?? 0f),
            Gear = outGauge?.Gear ?? 0,
            Throttle = Normalize(outGauge?.Throttle ?? 0f),
            Brake = Normalize(outGauge?.Brake ?? 0f),
            Clutch = Normalize(outGauge?.Clutch ?? 0f),
            HandBrake = outGauge is not null && (outGauge.ShowLights & HandbrakeLight) != 0 ? 1f : 0f,
            HandBrakeValid = outGauge is not null,
            ShiftLightsRPMValid = false,
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
