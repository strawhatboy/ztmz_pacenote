using System;
using System.Timers;
using ZTMZ.PacenoteTool.Base.Game;
using NLog;

namespace ZTMZ.PacenoteTool.AC;

public class ACRGameDataReader : SharedMemoryGameReader, IGameDataReader
{
    private readonly ILogger _logger = LogManager.GetCurrentClassLogger();
    private readonly Timer _readTimer;
    public const int TARGET_FPS = 60;
    private const double TIMER_INTERVAL_MS = 1000.0 / TARGET_FPS; // ~16.67ms for 60 FPS

    private GameState _gameState = GameState.Unknown;
    private GameData _lastGameData;
    private GameData _currentGameData;
    private ACRGameRawData _lastRawGameData = new ACRGameRawData();
    private ACRGameRawData _currentRawGameData = new ACRGameRawData();
    
    private bool _isDataAvailable;
    private bool _isInitialized;

    public ACRGameDataReader() : base(
        (typeof(ACRGraphicsData), "Local\\acpmf_graphics"),
        (typeof(ACRPhysicsData), "Local\\acpmf_physics"),
        (typeof(ACRStaticInfo), "Local\\acpmf_static"))
    {
        _readTimer = new Timer(TIMER_INTERVAL_MS);
        _readTimer.Elapsed += OnTimerElapsed;
        _readTimer.AutoReset = true;
    }

    public GameState GameState
    {
        get => _gameState;
        set
        {
            if (_gameState != value)
            {
                var previousState = _gameState;
                _gameState = value;
                onGameStateChanged?.Invoke(new GameStateChangeEvent
                {
                    LastGameState = previousState,
                    NewGameState = value
                });
            }
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

    public string CarClass => string.Empty; // AC doesn't provide car class in shared memory

    public event Action<GameData, GameData>? onNewGameData;
    public event Action<bool>? onGameDataAvailabilityChanged;
    public event Action<GameStateChangeEvent>? onGameStateChanged;
    public event Action<CarDamageEvent>? onCarDamaged;
    public event Action? onCarReset;

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

            if (TryOpenAllDebounced())
            {
                _isInitialized = true;
                _readTimer.Start();
                _logger.Info("ACRGameDataReader initialized successfully");
                
                return true;
            }

            _logger.Warn("Failed to open AC shared memory files");
            return false;
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
        CloseAll();
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
        }
        
        if (TryReadUnsafe<ACRStaticInfo>(out var staticInfo))
        {
            data.StaticInfo = staticInfo;
        }
        
        return data;
    }

    private void OnTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        if (!_isInitialized)
        {
            return;
        }

        try
        {
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
                // let's racebegin, damn
                    GameState = GameState.AdHocRaceBegin;
                }
                else
                {
                    _logger.Info("AC game data became unavailable");
                }
            }

            if (!hasData)
            {
                return;
            }

            // Read all raw data
            _lastRawGameData = _currentRawGameData;
            _currentRawGameData = ReadAll();

            // Convert raw data to GameData
            _lastGameData = _currentGameData;
            _currentGameData = ConvertToGameData(_currentRawGameData);

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
    }

    private GameData ConvertToGameData(ACRGameRawData rawData)
    {
        var gameData = new GameData
        {
            // Time and lap information
            Time = rawData.GraphicsData.CurrentTimeMillis / 1000.0f,
            LapTime = rawData.GraphicsData.CurrentTimeMillis / 1000.0f,
            LapDistance = rawData.GraphicsData.TotalDistanceTraveled,
            CompletionRate = rawData.StaticInfo.TrackLengthMeters > 0
                ? Math.Clamp(rawData.GraphicsData.TotalDistanceTraveled / rawData.StaticInfo.TrackLengthMeters, 0, 1)
                : 0,
            TrackLength = rawData.StaticInfo.TrackLengthMeters,

            // Speed and wheels
            Speed = rawData.PhysicsData.SpeedKilometersPerHour,
            SpeedFrontLeft = rawData.PhysicsData.WheelAngularVelocity?[0] ?? 0,
            SpeedFrontRight = rawData.PhysicsData.WheelAngularVelocity?[1] ?? 0,
            SpeedRearLeft = rawData.PhysicsData.WheelAngularVelocity?[2] ?? 0,
            SpeedRearRight = rawData.PhysicsData.WheelAngularVelocity?[3] ?? 0,

            // Pedals
            Clutch = 1 - rawData.PhysicsData.ClutchPosition,    // reversed
            Brake = rawData.PhysicsData.BrakeInput,
            Throttle = rawData.PhysicsData.ThrottleInput,
            HandBrake = 0, // AC doesn't provide handbrake in shared memory
            HandBrakeValid = false,

            // Steering and transmission
            Steering = rawData.PhysicsData.SteeringAngle,
            Gear = rawData.PhysicsData.CurrentGear - 1, // Convert to 0-based index
            MaxGears = 6, // AC doesn't provide max gears directly
            RPM = rawData.PhysicsData.EngineRPM,
            MaxRPM = 8000,
            IdleRPM = 1000, // Approximate idle RPM
            ShiftLightsFraction = 0,
            ShiftLightsRPMStart = 0,
            ShiftLightsRPMEnd = 0,
            ShiftLightsRPMValid = false,

            // G-forces
            G_long = rawData.PhysicsData.AccelerationG?[2] ?? 0,
            G_lat = rawData.PhysicsData.AccelerationG?[0] ?? 0,

            // Brake temperatures
            BrakeTempFrontLeft = rawData.PhysicsData.BrakeTemperatures?[0] ?? 0,
            BrakeTempFrontRight = rawData.PhysicsData.BrakeTemperatures?[1] ?? 0,
            BrakeTempRearLeft = rawData.PhysicsData.BrakeTemperatures?[2] ?? 0,
            BrakeTempRearRight = rawData.PhysicsData.BrakeTemperatures?[3] ?? 0,

            // Suspension
            SuspensionFrontLeft = rawData.PhysicsData.SuspensionTravelDistance?[0] ?? 0,
            SuspensionFrontRight = rawData.PhysicsData.SuspensionTravelDistance?[1] ?? 0,
            SuspensionRearLeft = rawData.PhysicsData.SuspensionTravelDistance?[2] ?? 0,
            SuspensionRearRight = rawData.PhysicsData.SuspensionTravelDistance?[3] ?? 0,
        };

        return gameData;
    }

    private void UpdateGameState()
    {
        var status = _currentRawGameData.GraphicsData.CurrentStatus;
        
        GameState newState = status switch
        {
            AC_STATUS.AC_OFF => GameState.Unknown,
            AC_STATUS.AC_REPLAY => GameState.RaceEnd,
            AC_STATUS.AC_LIVE => GameState.Racing,
            AC_STATUS.AC_PAUSE => GameState.Paused,
            _ => GameState.Unknown
        };

        // GameState = newState;

        // always return GameState.Racing for now
        GameState = GameState.Racing;
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

    public new void Dispose()
    {
        _readTimer?.Stop();
        _readTimer?.Dispose();
        base.Dispose();
    }
}
