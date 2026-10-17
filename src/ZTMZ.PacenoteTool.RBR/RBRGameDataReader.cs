

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Timers;
using IniParser;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ZTMZ.PacenoteTool.RBR;

public class RBRGameDataReader : UdpGameDataReader, IGameRuntimeDiagnosticsProvider
{
    private NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
    public static float G = 9.8f;
    public override bool IsUdpTelemetryOptional => true;

    public override GameState GameState
    {
        set => SetGameState(value);
        get => this._gameState;
    }

    private void SetGameState(
        GameState value,
        Dictionary<string, object>? parameters = null,
        bool forceEvent = false)
    {
        var lastGameState = this._gameState;
        if (!forceEvent && lastGameState == value)
        {
            return;
        }

        this._gameState = value;
        if (value == GameState.RaceBegin || value == GameState.AdHocRaceBegin)
        {
            TryReadCurrentRPMInfo();
            parameters ??= new Dictionary<string, object>
            {
                { GameStateRaceBeginProperty.IS_REPLAY, _stateDetector.IsReplaySession }
            };
        }
        else if (value == GameState.RaceEnd &&
                 !_stateDetector.IsReplaySession &&
                 _stateDetector.IsPostGame)
        {
            parameters ??= new Dictionary<string, object>
            {
                { GameStateRaceEndProperty.FINISH_TIME, _stateDetector.FinishTime },
                { GameStateRaceEndProperty.FINISH_STATE, GameStateRaceEnd.Normal }
            };
        }

        if (value == GameState.RaceEnd)
        {
            _logger.Debug(
                "RBR race end: normal={0}, replay={1}, raceEnded={2}, falseStart={3}",
                _stateDetector.IsPostGame && !_stateDetector.IsReplaySession,
                _stateDetector.IsReplaySession,
                _stateDetector.SawRaceEnded,
                _stateDetector.SawFalseStart);
        }

        this._onGameStateChanged?.Invoke(new GameStateChangeEvent
        {
            LastGameState = lastGameState,
            NewGameState = this._gameState,
            Parameters = parameters ?? new Dictionary<string, object>()
        });
    }

    public override GameData LastGameData { get => _lastGameData; set => _lastGameData = value; }
    public override GameData CurrentGameData { get => _currentGameData; set => _currentGameData = value; }

    /// <summary>
    /// TrackName here is in the format: [TrackNo]TrackName
    ///     Example: [198]Sipirkakim II Snow
    /// </summary>
    public override string TrackName 
    {
        get 
        {
            var trackName = memDataReader.GetTrackNameFromMemory();
            if (string.IsNullOrEmpty(trackName)) 
            {
                trackName = ((RBRGamePacenoteReader)_game.GamePacenoteReader).GetTrackNameFromConfigById(_currentMemData.TrackId);
            }

            return string.Format("[{0}]{1}", _currentMemData.TrackId, trackName);
        }
    }

    public override string CarName {
        get 
        {
            var carDef = readCurrentCarDef();
            return string.Format("[{0}]{1}", carDef?.RsfID, carDef?.Name ?? "UnknownCar");
        }
    }

    public override string CarClass {
        get
        {
            var carDef = readCurrentCarDef();
            return carDef?.Class ?? "UnknownCarClass";
        }
    }

    public static float MEM_REFRESH_INTERVAL = 33.3f; // 33.3ms = 30Hz
    public GameState _gameState;
    private GameData _lastGameData;

    private GameData _currentGameData;
    private RBRMemData _currentMemData;

    public RBRMemDataReader memDataReader = new();

    private Timer _timer = new();

    private event Action<GameData, GameData> _onNewGameData;

    private readonly RBRGameStateDetector _stateDetector = new();
    private RBRGameState? _lastLoggedRawGameState;
    private bool _memoryReaderInitialized;
    private int _isUninitializing;

    private Dictionary<int, float> _currentGearShiftRPM = new();
    private float _currentRPMLimit = 0;

    public object CaptureRuntimeDiagnostics()
    {
        return memDataReader.CaptureDamageTelemetryDiagnostics();
    }

    private RBRCarDef readCurrentCarDef() {
        var rbr_root = ((RBRGamePacenoteReader)_game.GamePacenoteReader).RBRRootDir;
        if (!Directory.Exists(rbr_root)) {
            return null;
        }

        var car_def_file = Path.Combine(rbr_root, "Cars\\Cars.ini");
        if (!RBRHelper.checkIfIniFileValid(car_def_file)) {
            return null;
        }

        var carIni = new FileIniDataParser().ReadFile(car_def_file);
        var carSlotId = _currentMemData.CarModelId;
        var carSectionName = $"Car{carSlotId:00}";
        if (!carIni.Sections.ContainsSection(carSectionName)) {
            return null;
        }

        var carDef = new RBRCarDef();
        var carSection = carIni[carSectionName];
        if (carSection.ContainsKey("CarName")) {
            carDef.Name = carSection["CarName"];
        }
        if (carSection.ContainsKey("RSFCarID")) {
            if (int.TryParse(carSection["RSFCarID"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var rsfId))
            {
                carDef.RsfID = rsfId;
            }
            else
            {
                _logger.Warn("Unable to parse RSFCarID for RBR car slot {0}.", carSlotId);
            }
        }

        var car_group_map_file = Path.Combine(rbr_root, "rsfdata\\cache\\cars.json");
        var car_class_file = Path.Combine(rbr_root, "rsfdata\\cache\\cargroups.json");
        if (!File.Exists(car_group_map_file) || !File.Exists(car_class_file)) {
            return carDef;
        }

        using (var reader = new StreamReader(car_group_map_file)) {
            var car_group_map = JArray.Load(new JsonTextReader(reader));
            bool found = false;
            foreach (var car_group in car_group_map) {
                var car_group_obj = car_group as JObject;
                if (car_group_obj != null && car_group_obj.ContainsKey("id") && car_group_obj["id"].Value<int>() == carDef.RsfID) {
                    var group_id = car_group_obj["base_group_id"].Value<int>();
                    // get car class by group_id
                    using (var car_class_reader = new StreamReader(car_class_file)) {
                        var car_class_map = JArray.Load(new JsonTextReader(car_class_reader));
                        foreach (var car_class in car_class_map) {
                            var car_class_obj = car_class as JObject;
                            if (car_class_obj != null && car_class_obj.ContainsKey("id") && car_class_obj["id"].Value<int>() == group_id) {
                                carDef.Class = car_class_obj["name"].Value<string>();
                                found = true;
                                break;
                            }
                        }
                    }
                }
                if (found) {
                    break;
                }
            }
        }

        return carDef;
    }

    private void readCurrentRPMInfo() {
        _currentGearShiftRPM.Clear();
        _currentRPMLimit = 0;

        var rbr_root = ((RBRGamePacenoteReader)_game.GamePacenoteReader).RBRRootDir;
        if (!Directory.Exists(rbr_root)) {
            return;
        }

        // actually the car slot id with corresonding physics folder
        var rbr_physics_dict = new Dictionary<int, string> {
            { 0, "rsfdata\\Physics\\c_xsara" },
            { 1, "rsfdata\\Physics\\h_accent" },
            { 2, "rsfdata\\Physics\\mg_zr" },
            { 3, "rsfdata\\Physics\\m_lancer" },
            { 4, "rsfdata\\Physics\\p_206" },
            { 5, "rsfdata\\Physics\\s_i2003" },
            { 6, "rsfdata\\Physics\\t_coroll" },
            { 7, "rsfdata\\Physics\\s_i2000" },
        };

        var carSlotId = _currentMemData.CarModelId;
        if (!rbr_physics_dict.ContainsKey(carSlotId)) {
            return;
        }

        var physics_file = Path.Combine(rbr_root, rbr_physics_dict[carSlotId], "common.lsp");
        if (!File.Exists(physics_file)) {
            return;
        }

        var lines = File.ReadAllLines(physics_file);
        var gearUpShift = new Dictionary<int, float>();
        foreach (var line in lines) {
            var slines = line.Trim().Split(new char[]{ ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (slines.Length < 2) {
                continue;
            }

            var key = slines[0];
            var value = slines[1];
            if (key == "RPMLimit") {
                if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var rpmLimit))
                {
                    _currentRPMLimit = rpmLimit;
                }
                else
                {
                    _logger.Warn("Unable to parse RBR RPM limit value '{0}'.", value);
                }
            } else if (key.StartsWith("Gear") && key.EndsWith("Upshift")) {
                if (key.Length <= 4 ||
                    !int.TryParse(key.Substring(4, 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var fakeGear) ||
                    !float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var upshiftRpm))
                {
                    _logger.Warn("Unable to parse RBR upshift value '{0}={1}'.", key, value);
                    continue;
                }

                gearUpShift[fakeGear - 1] = upshiftRpm;
            }
        }

        var maxGear = gearUpShift.Count > 2 ? gearUpShift.Count - 2 : 5;

        // fill zero gear shift rpm
        if (gearUpShift.ContainsKey(-1) && gearUpShift.ContainsKey(0) && gearUpShift.ContainsKey(1) && gearUpShift[-1] == 0) {
            gearUpShift[-1] = gearUpShift[0] == 0 ? gearUpShift[1] : gearUpShift[0];
        }
        if (gearUpShift.ContainsKey(0) && gearUpShift.ContainsKey(1) && gearUpShift[0] == 0) {
            gearUpShift[0] = gearUpShift[1];
        }
        if (gearUpShift.ContainsKey(maxGear) && gearUpShift[maxGear] == 0) {
            gearUpShift[maxGear] = _currentRPMLimit;
        }

        _currentGearShiftRPM = gearUpShift;

        _logger.Info("Read current RPM info: {0}, {1}, {2}", _currentRPMLimit, maxGear, string.Join(", ", gearUpShift));
    }

    private void TryReadCurrentRPMInfo()
    {
        try
        {
            readCurrentRPMInfo();
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to read RBR RPM information at stage start.");
        }
    }

    public override event Action<GameData, GameData> onNewGameData
    {
        add 
        {
            _onNewGameData += value;
        }
        remove { _onNewGameData -= value; }
    }
    private event Action<GameStateChangeEvent> _onGameStateChanged;
    public override event Action<GameStateChangeEvent> onGameStateChanged
    {
        add { _onGameStateChanged += value; }
        remove { _onGameStateChanged -= value; }
    }
    private event Action<CarDamageEvent> _onCarDamaged;
    public override event Action<CarDamageEvent> onCarDamaged
    {
        add { _onCarDamaged += value; }
        remove { _onCarDamaged -= value; }
    }
    private event Action _onCarReset;
    public override event Action onCarReset
    {
        add { _onCarReset += value; }
        remove { _onCarReset -= value; }
    }

    public override bool Initialize(IGame game)
    {
        _logger.Info("Initializing RBRGameDataReader");
        if (_memoryReaderInitialized || isInitialized)
        {
            _logger.Warn("RBRGameDataReader is already initialized.");
            return false;
        }

        _stateDetector.Reset();
        _lastLoggedRawGameState = null;
        _gameState = GameState.Unknown;
        System.Threading.Interlocked.Exchange(ref _isUninitializing, 0);

        // We use udp data for RBR, but only optional.
        try
        {
            var udpInitResult = base.Initialize(game);
            if (!udpInitResult)
            {
                _logger.Warn("UDP telemetry was not initialized for RBR; memory telemetry will be used.");
            }
        }
        catch (PortAlreadyInUseException)
        {
            _logger.Warn("UDP telemetry was not initialized for RBR because the port is already in use; memory telemetry will be used.");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to initialize UDPGameDataReader");
        }

        Debug.Assert(game.GameConfigurations.ContainsKey(MemoryGameConfig.Name));
        var memConfig = game.GameConfigurations[MemoryGameConfig.Name] as MemoryGameConfig;
        if (memConfig == null || memConfig.RefreshRate <= 0)
        {
            _logger.Error("Failed to get a valid MemoryGameConfig from game.GameConfigurations");
            if (isInitialized)
            {
                base.Uninitialize(game);
            }
            return false;
        }

        MEM_REFRESH_INTERVAL = 1000f / memConfig.RefreshRate;

        _logger.Info("RBRMemDataReader trying to open process {0}", game.Executable);
        // init memory reader? with retry?
        var retry = 3;
        var success = false;
        while (retry-- > 0)
        {
            if (memDataReader.OpenProcess(game))
            {
                _logger.Info("Memory reader opened.");
                success = true;
                break;
            }
            _logger.Error($"Failed to open memory reader, retrying... {retry} times left.");
            // sleep?
            System.Threading.Thread.Sleep(1000);
        }
        if (!success) {
            _logger.Error("Failed to open memory reader.");
            if (isInitialized)
            {
                base.Uninitialize(game);
            }
            return false;
        }

        _timer.Stop();
        _timer.Elapsed -= MemDataPullHandler;
        _timer.AutoReset = false;
        _timer.Elapsed += MemDataPullHandler;
        _timer.Interval = MEM_REFRESH_INTERVAL;
        _memoryReaderInitialized = true;
        _timer.Start();

        _logger.Info("RBRGameDataReader initialized.");
        return true;
    }

    public override void Uninitialize(IGame game)
    {
        System.Threading.Interlocked.Exchange(ref _isUninitializing, 1);
        _memoryReaderInitialized = false;
        _timer.Stop();
        _timer.Elapsed -= MemDataPullHandler;
        memDataReader.CloseProgress();
        _currentGameData.ForceFeedback = null;
        _stateDetector.Reset();
        _lastLoggedRawGameState = null;
        _gameState = GameState.Unknown;

        if (isInitialized)
        {
            base.Uninitialize(game);
        }
    }

    private void MemDataPullHandler(object? sender, ElapsedEventArgs e)
    {
        try
        {
            if (System.Threading.Volatile.Read(ref _isUninitializing) != 0 || !_memoryReaderInitialized)
            {
                return;
            }

            var memData = memDataReader.ReadMemData(_game);
            if (System.Threading.Volatile.Read(ref _isUninitializing) != 0)
            {
                return;
            }

            _lastGameData = _currentGameData;
            _currentGameData = GetGameDataFromMemory(_currentGameData, memData);
            _currentMemData = memData;
            _onNewGameData?.Invoke(_lastGameData, _currentGameData);

            UpdateGameStateFromMemory(memData);
        }
        catch (Exception ex)
        {
            _currentGameData.ForceFeedback = null;
            _logger.Error(ex, "Failed to poll RBR memory data.");
        }
        finally
        {
            if (System.Threading.Volatile.Read(ref _isUninitializing) == 0 && _memoryReaderInitialized)
            {
                _timer.Start();
            }
        }
    }

    internal void UpdateGameStateFromMemory(RBRMemData memData)
    {
        var rawState = (RBRGameState)memData.GameStateId;
        if (_lastLoggedRawGameState != rawState)
        {
            _logger.Debug(
                "RBR raw game state changed to {0} ({1}); countdown={2:F3}, raceEnded={3}, falseStart={4}",
                rawState,
                memData.GameStateId,
                memData.StageStartCountdown,
                memData.RaceEnded,
                memData.FalseStart);
            _lastLoggedRawGameState = rawState;
        }

        var detection = _stateDetector.Detect(
            memData,
            GetPlayWhenReplay(),
            GameState,
            _currentGameData.CompletionRate,
            _currentGameData.LapTime);

        if (detection.CountdownNumber.HasValue)
        {
            SetGameState(
                detection.State,
                new Dictionary<string, object> { { "number", detection.CountdownNumber.Value } },
                forceEvent: true);
            return;
        }

        if (detection.FinishState is { } finishState)
        {
            _logger.Debug("RBR native result: {0}", finishState);
            SetGameState(detection.State, new Dictionary<string, object>
            {
                { GameStateRaceEndProperty.FINISH_STATE, finishState }
            });
        }
        else
        {
            GameState = detection.State;
        }
    }

    private bool GetPlayWhenReplay()
    {
        if (_game.GameConfigurations.TryGetValue(CommonGameConfigs.Name, out var config) &&
            config is CommonGameConfigs commonConfig &&
            commonConfig.PropertyValue.Count > 0 &&
            commonConfig.PropertyValue[0] is bool playWhenReplay)
        {
            return playWhenReplay;
        }

        return false;
    }

    public override void onNewUdpMessage(byte[] oldMsg, byte[] newMsg)
    {
        base.onNewUdpMessage(oldMsg, newMsg);
        var lastUdp = oldMsg.CastToStruct<RBRUdpData>();
        var newUdp = newMsg.CastToStruct<RBRUdpData>();

        // we're using acceleration to detect collision now, available in RBR.
        // use UDP data for collision detection, because it's time consistent.
        // memory data is read sequentially, not time consistent.
        GameData lastUdpGameData = getGameDataFromUdp(new GameData(), lastUdp);
        GameData newUdpGameData = getGameDataFromUdp(new GameData(), newUdp);
        if (Config.Instance.PlayCollisionSound && _currentGameData.Speed != 0) {
            // detect with G_long
            CollisionSeverity severity = CollisionSeverity.None;
            if (newUdpGameData.G_long <= -Config.Instance.CollisionSpeedChangeThreshold_Severe) {
                _logger.Trace($"Acceleration: {newUdpGameData.G_long}");
                severity = CollisionSeverity.Severe;
            } else if (newUdpGameData.G_long <= -Config.Instance.CollisionSpeedChangeThreshold_Medium) {
                _logger.Trace($"Acceleration: {newUdpGameData.G_long}");
                severity = CollisionSeverity.Medium;
            } else if (newUdpGameData.G_long <= -Config.Instance.CollisionSpeedChangeThreshold_Slight) {
                _logger.Trace($"Acceleration: {newUdpGameData.G_long}");
                severity = CollisionSeverity.Slight;
            }

            if (severity != CollisionSeverity.None) {
                _onCarDamaged?.Invoke(new CarDamageEvent
                {
                    DamageType = CarDamage.Collision,
                    Parameters = new Dictionary<string, object> { { CarDamageConstants.SEVERITY, severity } }
                });
            }
        }

        _currentGameData = getGameDataFromUdp(_currentGameData, newUdp);
    }

    private GameData getGameDataFromUdp(GameData gameData, RBRUdpData data)
    {
        // gameData.TimeStamp = DateTime.Now;
        gameData.Time = data.stage.raceTime;
        // gameData.LapTime = data.stage.raceTime;
        // gameData.LapDistance = data.stage.distanceToEnd / (100 - data.stage.progress) * data.stage.progress;
        // gameData.CompletionRate = data.stage.progress;
        // gameData.Speed = data.car.speed;
        // gameData.TrackLength = data.stage.distanceToEnd / (100 - data.stage.progress);
        // gameData.SpeedRearLeft = data.contro[0];
        // gameData.SpeedRearRight = data.car.wheelSpeed[1];
        // gameData.SpeedFrontLeft = data.car.wheelSpeed[2];
        // gameData.SpeedFrontRight = data.car.wheelSpeed[3];

        // gameData.Clutch = data.control.clutch;
        // gameData.Brake = data.control.brake;
        // gameData.Throttle = data.control.throttle;
        // gameData.Steering = data.control.steering;
        // gameData.Gear = data.control.gear;
        // gameData.HandBrake = data.control.handbrake;
        // gameData.HandBrakeValid = true;
        
        
        // gameData.MaxGears = data.MaxGears;
        // gameData.RPM = data.control.
        // gameData.MaxRPM = data.MaxRPM;
        // gameData.IdleRPM = data.IdleRPM;
        // gameData.G_lat = data.G_lat;
        // gameData.G_long = data.G_long;

        gameData.BrakeTempRearLeft = data.car.suspensionLB.wheel.brakeDisk.temperature;
        gameData.BrakeTempRearRight = data.car.suspensionRB.wheel.brakeDisk.temperature;
        gameData.BrakeTempFrontLeft = data.car.suspensionLF.wheel.brakeDisk.temperature;
        gameData.BrakeTempFrontRight = data.car.suspensionRF.wheel.brakeDisk.temperature;

        // gameData.SuspensionRearLeft = data.car.suspensionLB.;
        // gameData.SuspensionRearRight = data.SuspensionRearRight;
        // gameData.SuspensionFrontLeft = data.SuspensionFrontLeft;
        // gameData.SuspensionFrontRight = data.SuspensionFrontRight;

        gameData.SuspensionSpeedRearLeft = data.car.suspensionLB.damper.pistonVelocity;
        gameData.SuspensionSpeedRearRight = data.car.suspensionRB.damper.pistonVelocity;
        gameData.SuspensionSpeedFrontLeft = data.car.suspensionLF.damper.pistonVelocity;
        gameData.SuspensionSpeedFrontRight = data.car.suspensionRF.damper.pistonVelocity;

        // surge: forward/backward
        gameData.G_long = data.car.accelerations.surge;

        // sway: left/right
        gameData.G_lat = -data.car.accelerations.sway;

        return gameData;
    }

    private GameData GetGameDataFromMemory(GameData gameData, RBRMemData data)
    { 
        gameData.TimeStamp = DateTime.UtcNow;
        gameData.Clutch = data.Clutch;
        gameData.Brake = data.Brake;
        gameData.Throttle = data.Throttle;
        gameData.Steering = data.Steering;
        gameData.HandBrake = data.Handbrake;
        gameData.HandBrakeValid = true;
        gameData.Gear = data.GearId;
        gameData.Speed = data.SpeedKMH;
        gameData.TrackLength = data.TrackLength;
        gameData.LapDistance = data.DistanceFromStart;
        var totalDistance = data.DistanceToFinish + data.DistanceFromStart;
        gameData.CompletionRate = float.IsFinite(totalDistance) && totalDistance > 0
            ? data.DistanceFromStart / totalDistance
            : 0;
        gameData.LapTime = data.RaceTime;
        var currentGearShiftRpm = 0f;
        var hasCurrentGearShiftRpm = _currentGearShiftRPM.Count >= 2 &&
                                     _currentGearShiftRPM.TryGetValue(data.GearId, out currentGearShiftRpm) &&
                                     currentGearShiftRpm > 0;
        gameData.ShiftLightsRPMValid = hasCurrentGearShiftRpm;
        gameData.MaxRPM = hasCurrentGearShiftRpm ? _currentRPMLimit : 7500;
        gameData.MaxGears = hasCurrentGearShiftRpm ? _currentGearShiftRPM.Count - 2 : 5;   // 5 gears by default
        gameData.ShiftLightsRPMStart = hasCurrentGearShiftRpm ? currentGearShiftRpm - 1000 : 5750;
        gameData.ShiftLightsRPMEnd = hasCurrentGearShiftRpm ? currentGearShiftRpm : 6500;
        gameData.ShiftLightsFraction = (data.EngineRPM - gameData.ShiftLightsRPMStart) / (gameData.ShiftLightsRPMEnd - gameData.ShiftLightsRPMStart);
        // var xInertia = (data.XSpeed - _currentMemData.XSpeed) / MEM_REFRESH_INTERVAL;
        // var yInertia = (data.YSpeed - _currentMemData.YSpeed) / MEM_REFRESH_INTERVAL;
        // var inertia = (float)Math.Sqrt(xInertia * xInertia + yInertia * yInertia);
        // if (inertia != 0)
        // {
        //     var intertiaAngle = (float)Math.Asin(yInertia / inertia);
        //     var actualInertiaAngle = intertiaAngle + data.ZSpin;
        //     gameData.G_lat = inertia * (float)Math.Cos(actualInertiaAngle);
        //     gameData.G_long = inertia * (float)Math.Sin(actualInertiaAngle);
        // }

        gameData.PosX = data.X;
        gameData.PosY = data.Y;
        gameData.PosZ = data.Z;

        gameData.RPM = data.EngineRPM;
        
        // ground speed instead of wheel speed.
        // gameData.Speed = MathF.Sqrt(MathF.Pow(data.XSpeed, 2f) + MathF.Pow(data.YSpeed, 2f) + MathF.Pow(data.ZSpeed, 2f)); 

        gameData.GameSpecificData = data;
        gameData.CarDamage = RBRCarDamageSnapshotFactory.Create(data.DamageTelemetry);
        gameData.Engine = data.Engine;
        gameData.Turbo = data.Turbo;
        gameData.ForceFeedback = data.ForceFeedback;

        return gameData;
    }
}
