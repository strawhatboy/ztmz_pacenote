using System;
using System.Timers;
using ZTMZ.PacenoteTool.Base.Game;
using NLog;

namespace ZTMZ.PacenoteTool.AC;

public class ACRGameDataReader : SharedMemoryGameReader, IGameDataReader, IGameLivePacenoteSource
{
    private readonly ILogger _logger = LogManager.GetCurrentClassLogger();
    private readonly System.Timers.Timer _readTimer;
    public const int TARGET_FPS = 60;
    private const double TIMER_INTERVAL_MS = 1000.0 / TARGET_FPS; // ~16.67ms for 60 FPS
    private GameState _gameState = GameState.Unknown;
    private GameData _lastGameData;
    private GameData _currentGameData;
    private ACRGameRawData _lastRawGameData = new ACRGameRawData();
    private ACRGameRawData _currentRawGameData = new ACRGameRawData();
    
    private bool _isDataAvailable;
    private bool _isInitialized;
    private AC_STATUS? _lastProbedStatus;
    private AC_SESSION_TYPE? _lastProbedSessionType;
    private AC_FLAGS? _lastProbedFlag;
    private GameState? _lastProbedMappedState;
    private int _lastProbedGreenFlag;
    private int _lastProbedChequeredFlag;
    private ACRLivePacenoteBridge? _livePacenoteBridge;
    private ACRVehicleMemoryReader? _vehicleMemory;
    private int _reading;
    private readonly ACRStageEntry _stageEntry = new();

    public ACRGameDataReader() : base(
        (typeof(ACRGraphicsData), "Local\\acpmf_graphics"),
        (typeof(ACRPhysicsData), "Local\\acpmf_physics"),
        (typeof(ACRStaticInfo), "Local\\acpmf_static"))
    {
        _readTimer = new System.Timers.Timer(TIMER_INTERVAL_MS);
        _readTimer.Elapsed += OnTimerElapsed;
        _readTimer.AutoReset = true;
    }

    public GameState GameState
    {
        get => _gameState;
        set => PublishGameState(value, null);
    }

    private void PublishGameState(GameState value, System.Collections.Generic.Dictionary<string, object>? parameters)
    {
        if (_gameState != value)
        {
            var previousState = _gameState;
            _gameState = value;
            var graphics = _currentRawGameData.GraphicsData;
            _logger.Info(
                "ACR emitted state transition {0} -> {1}; raw status={2}({3}), session={4}({5}), flag={6}({7}), timeMs={8}, distance={9:F1}, normalized={10:F4}, speed={11:F1}.",
                previousState,
                value,
                graphics.CurrentStatus,
                (int)graphics.CurrentStatus,
                graphics.SessionType,
                (int)graphics.SessionType,
                graphics.CurrentFlag,
                (int)graphics.CurrentFlag,
                graphics.CurrentTimeMillis,
                graphics.TotalDistanceTraveled,
                graphics.NormalizedCarPos,
                _currentRawGameData.PhysicsData.SpeedKilometersPerHour);
            onGameStateChanged?.Invoke(new GameStateChangeEvent
            {
                LastGameState = previousState,
                NewGameState = value,
                Parameters = parameters ?? new System.Collections.Generic.Dictionary<string, object>()
            });
        }
    }

    public GameData LastGameData
    {
        get => _lastGameData;
        set => _lastGameData = value;
    }

    public GameData CurrentGameData
    {
        get => _currentGameData;
        set => _currentGameData = value;
    }

    public ACRGameRawData LastRawGameData => _lastRawGameData;
    public ACRGameRawData CurrentRawGameData => _currentRawGameData;

    public string TrackName => _currentRawGameData.StaticInfo.TrackName ?? string.Empty;

    public string CarName => _currentRawGameData.StaticInfo.VehicleModel ?? string.Empty;

    public string CarClass
    {
        get
        {
            var carName = CarName;
            var end = carName.Length - 1;
            while (end >= 0 && char.IsWhiteSpace(carName[end]))
            {
                end--;
            }

            if (end < 0)
            {
                return string.Empty;
            }

            var start = end;
            while (start >= 0 && !char.IsWhiteSpace(carName[start]))
            {
                start--;
            }

            return carName[(start + 1)..(end + 1)];
        }
    }

    public event Action<GameData, GameData>? onNewGameData;
    public event Action<bool>? onGameDataAvailabilityChanged;
    public event Action<GameStateChangeEvent>? onGameStateChanged;
    public event Action<CarDamageEvent>? onCarDamaged;
    public event Action? onCarReset;
    public event Action<LivePacenoteEvent>? onLivePacenote;

    public bool Initialize(IGame game)
    {
        if (_isInitialized)
        {
            return true;
        }

        try
        {
            // Get refresh rate from config if available
            if (game.GameConfigurations.ContainsKey(MemoryGameConfig.Name))
            {
                var memConfig = game.GameConfigurations[MemoryGameConfig.Name] as MemoryGameConfig;
                if (memConfig != null && memConfig.RefreshRate > 0)
                {
                    _readTimer.Interval = 1000.0 / memConfig.RefreshRate;
                    _logger.Info("ACRGameDataReader using configured refresh rate: {0} FPS", memConfig.RefreshRate);
                }
            }

            _livePacenoteBridge ??= new ACRLivePacenoteBridge(
                PublishLivePacenote,
                PublishBridgeGameState,
                PublishBridgeCarDamage);
            if (OperatingSystem.IsWindows()) _vehicleMemory ??= new ACRVehicleMemoryReader();
            _isInitialized = true;
            _readTimer.Start();

            if (TryOpenAllDebounced())
            {
                _logger.Info("ACRGameDataReader initialized successfully; live-pacenote bridge listening on {0}.", ACRLivePacenoteBridge.PipeName);
            }
            else
            {
                _logger.Info("ACR shared memory is not ready; live-pacenote bridge is listening on {0} and shared-memory reads will retry.", ACRLivePacenoteBridge.PipeName);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error initializing ACRGameDataReader");
            return false;
        }
    }

    public void Uninitialize(IGame game)
    {
        _isInitialized = false;
        _readTimer.Stop();
        var vehicleMemory = System.Threading.Interlocked.Exchange(ref _vehicleMemory, null);
        if (OperatingSystem.IsWindows()) vehicleMemory?.Dispose();
        _livePacenoteBridge?.Dispose();
        _livePacenoteBridge = null;
        CloseAll();
        ResetStateProbe();
        _stageEntry.Reset();
        _logger.Info("ACRGameDataReader uninitialized");
    }

    public ACRGameRawData ReadAll()
    {
        var data = new ACRGameRawData();
        
        if (TryReadUnsafe<ACRGraphicsData>(out var graphicsData))
        {
            data.GraphicsData = graphicsData;
        }
        
        if (TryReadUnsafe<ACRPhysicsData>(out var physicsData))
        {
            data.PhysicsData = physicsData;
            data.PhysicsDataValid = true;
        }
        
        if (TryReadUnsafe<ACRStaticInfo>(out var staticInfo))
        {
            data.StaticInfo = staticInfo;
        }
        
        return data;
    }

    private void OnTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        if (!_isInitialized || System.Threading.Interlocked.CompareExchange(ref _reading, 1, 0) != 0)
        {
            return;
        }

        try
        {
            if (!_isInitialized) return;
            // Try to reconnect if disconnected
            if (!IsAllConnected)
            {
                TryOpenAllDebounced();
            }

            // Check if we have any data available
            bool hasData = IsAnyConnected;
            
            if (hasData != _isDataAvailable)
            {
                _isDataAvailable = hasData;
                onGameDataAvailabilityChanged?.Invoke(hasData);
                
                if (hasData)
                {
                    _logger.Info("AC game data became available");
                    _logger.Info("ACR state probe: shared memory became available; waiting for bridge race-start resources.");
                }
                else
                {
                    _logger.Info("AC game data became unavailable");
                }
            }

            if (!hasData)
            {
                _currentGameData.ForceFeedback = null;
                return;
            }

            // Read all raw data
            _lastRawGameData = _currentRawGameData;
            _currentRawGameData = ReadAll();

            // Convert raw data to GameData
            _lastGameData = _currentGameData;
            var vehicle = OperatingSystem.IsWindows() ? _vehicleMemory?.Read() : null;
            _currentGameData = ACRGameDataConverter.Convert(_currentRawGameData, vehicle);

            // Update game state
            UpdateGameState();

            // Raise new data event
            onNewGameData?.Invoke(_lastGameData, _currentGameData);

            // Check for car damage
            CheckCarDamage();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error in timer elapsed");
        }
        finally { System.Threading.Volatile.Write(ref _reading, 0); }
    }

    private void UpdateGameState()
    {
        if (OperatingSystem.IsWindows())
        {
            var entry = _stageEntry.Observe(_vehicleMemory?.StageState);
            if (entry is not null)
            {
                _logger.Info("ACR entered native {0} for race {1}; publishing stage welcome before countdown.", entry.SourceToken, entry.StageId);
                PublishLivePacenote(entry);
            }
        }
        var mappedState = MapRawStatusToGameState(_currentRawGameData.GraphicsData.CurrentStatus);
        RecordStateProbe(mappedState);

        // ACR's shared-memory status does not distinguish a rally stage's start sequence
        // from normal session states. RaceBegin and Racing are emitted by the bridge's
        // EventCountdown and EventCountdownGo resources instead.
    }

    private static GameState MapRawStatusToGameState(AC_STATUS status)
    {
        return status switch
        {
            AC_STATUS.AC_OFF => GameState.Unknown,
            AC_STATUS.AC_REPLAY => GameState.RaceEnd,
            AC_STATUS.AC_LIVE => GameState.Racing,
            AC_STATUS.AC_PAUSE => GameState.Paused,
            _ => GameState.Unknown
        };
    }

    private void RecordStateProbe(GameState mappedState)
    {
        var graphics = _currentRawGameData.GraphicsData;
        var physics = _currentRawGameData.PhysicsData;
        var stateChanged = !_lastProbedStatus.HasValue
            || _lastProbedStatus.Value != graphics.CurrentStatus
            || _lastProbedSessionType != graphics.SessionType
            || _lastProbedFlag != graphics.CurrentFlag
            || _lastProbedMappedState != mappedState
            || _lastProbedGreenFlag != graphics.GreenFlagGlobal
            || _lastProbedChequeredFlag != graphics.ChequeredFlagGlobal;
        if (!stateChanged)
        {
            return;
        }

        _lastProbedStatus = graphics.CurrentStatus;
        _lastProbedSessionType = graphics.SessionType;
        _lastProbedFlag = graphics.CurrentFlag;
        _lastProbedMappedState = mappedState;
        _lastProbedGreenFlag = graphics.GreenFlagGlobal;
        _lastProbedChequeredFlag = graphics.ChequeredFlagGlobal;

        _logger.Debug(
            "ACR state probe: status={0}({1}), session={2}({3}), flag={4}({5}), mapped={6}, emitted={7}, packets={8}/{9}, timeMs={10}, sessionClock={11:F3}, remaining={12:F3}, laps={13}/{14}, distance={15:F1}, normalized={16:F4}, flags(green/checkered)={17}/{18}, setup={19}, pitBox={20}, pitLane={21}, replaySpeed={22:F2}, speed={23:F1}, throttle={24:F2}, brake={25:F2}, gear={26}.",
            graphics.CurrentStatus,
            (int)graphics.CurrentStatus,
            graphics.SessionType,
            (int)graphics.SessionType,
            graphics.CurrentFlag,
            (int)graphics.CurrentFlag,
            mappedState,
            GameState,
            graphics.PacketIdentifier,
            physics.PacketIdentifier,
            graphics.CurrentTimeMillis,
            graphics.SessionClock,
            graphics.SessionRemainingTime,
            graphics.LapsCompleted,
            graphics.TotalLapsInSession,
            graphics.TotalDistanceTraveled,
            graphics.NormalizedCarPos,
            graphics.GreenFlagGlobal,
            graphics.ChequeredFlagGlobal,
            graphics.SetupMenuVisible,
            graphics.InPitBox,
            graphics.InPitLaneArea,
            graphics.ReplaySpeedMultiplier,
            physics.SpeedKilometersPerHour,
            physics.ThrottleInput,
            physics.BrakeInput,
            physics.CurrentGear);
    }

    private void ResetStateProbe()
    {
        _lastProbedStatus = null;
        _lastProbedSessionType = null;
        _lastProbedFlag = null;
        _lastProbedMappedState = null;
        _lastProbedGreenFlag = 0;
        _lastProbedChequeredFlag = 0;
    }

    private void CheckCarDamage()
    {
        // Compare damage between last and current frame
        if (_lastRawGameData.PhysicsData.VehicleDamage != null && 
            _currentRawGameData.PhysicsData.VehicleDamage != null)
        {
            for (int i = 0; i < _currentRawGameData.PhysicsData.VehicleDamage.Length && 
                            i < _lastRawGameData.PhysicsData.VehicleDamage.Length; i++)
            {
                float damageIncrease = _currentRawGameData.PhysicsData.VehicleDamage[i] - 
                                      _lastRawGameData.PhysicsData.VehicleDamage[i];
                
                if (damageIncrease > 0.01f) // Threshold to avoid noise
                {
                    var damageEvent = new CarDamageEvent
                    {
                        DamageType = CarDamage.Bodywork,
                        Parameters = new System.Collections.Generic.Dictionary<string, object>
                        {
                            { "zone_index", i },
                            { "damage_increase", damageIncrease }
                        }
                    };
                    onCarDamaged?.Invoke(damageEvent);
                }
            }
        }
    }

    private void PublishLivePacenote(LivePacenoteEvent pacenote)
    {
        onLivePacenote?.Invoke(pacenote);
    }

    private void PublishBridgeGameState(
        GameState gameState,
        System.Collections.Generic.Dictionary<string, object>? parameters)
    {
        PublishGameState(gameState, parameters);
    }

    private void PublishBridgeCarDamage(CarDamageEvent damageEvent)
    {
        onCarDamaged?.Invoke(damageEvent);
    }

    public new void Dispose()
    {
        _isInitialized = false;
        _readTimer?.Stop();
        _readTimer?.Dispose();
        var vehicleMemory = System.Threading.Interlocked.Exchange(ref _vehicleMemory, null);
        if (OperatingSystem.IsWindows()) vehicleMemory?.Dispose();
        base.Dispose();
    }
}
