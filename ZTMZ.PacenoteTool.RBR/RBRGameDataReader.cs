

using System;
using System.Timers;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.RBR;

public class RBRGameDataReader : UdpGameDataReader
{
    public static float G = 9.8f;
    public override GameState GameState
    {
        set
        {
            var lastGameState = this._gameState;
            if (lastGameState == value) 
            {
                // GameState not changed
                return;
            }
            this._gameState = value;
            this._onGameStateChanged?.Invoke(new GameStateChangeEvent { LastGameState = lastGameState, NewGameState = this._gameState });
        }
        get => this._gameState;
    }
    public override GameData LastGameData { get => _lastGameData; set => _lastGameData = value; }

    /// <summary>
    /// TrackName here is in the format: [TrackNo]TrackName
    ///     Example: [198]Sipirkakim II Snow
    /// </summary>
    public override string TrackName 
    {
        get 
        {
            var trackName = _memDataReader.GetTrackNameFromMemory();
            if (string.IsNullOrEmpty(trackName)) 
            {
                trackName = ((RBRGamePacenoteReader)_game.GamePacenoteReader).GetTrackNameFromConfigById(_currentMemData.TrackId);
            }

            return string.Format("[{0}]{1}", _currentMemData.TrackId, trackName);
        }
    }

    public static float MEM_REFRESH_INTERVAL = 25f; 
    public GameState _gameState;
    private GameData _lastGameData;

    private GameData _currentGameData;
    private RBRMemData _currentMemData;

    private RBRMemDataReader _memDataReader = new();

    private Timer _timer = new();

    private event Action<GameData, GameData> _onNewGameData;

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

    public override bool Initialize(IGame game)
    {
        var udpInitResult = base.Initialize(game);
        if (!udpInitResult) 
        {
            return udpInitResult;
        }

        // init memory reader?
        _memDataReader.OpenProcess(game);
        _timer.Elapsed += MemDataPullHandler;
        _timer.Interval = MEM_REFRESH_INTERVAL; // 25ms = 40Hz
        _timer.Start();
        return true;
    }

    public override void Uninitialize(IGame game)
    {
        base.Uninitialize(game);
        _memDataReader.CloseProgress();
        _timer.Elapsed -= MemDataPullHandler;
        _timer.Stop();
    }

    private void MemDataPullHandler(object? sender, ElapsedEventArgs e) 
    {
        var memData = _memDataReader.ReadMemData(_game);
        
        _lastGameData = _currentGameData;
        _currentGameData = GetGameDataFromMemory(_currentGameData, memData);
        _currentMemData = memData;
        _onNewGameData?.Invoke(_lastGameData, _currentGameData);

        this.GameState = getGameStateFromMemory(memData);
    }

    private GameState getGameStateFromMemory(RBRMemData memData)
    {
        if (memData.StageStartCountdown > 0)
        {
            return GameState.RaceBegin;
        }

        var state = (RBRGameState)memData.GameStateId;
        if (state == RBRGameState.Unknown0 || 
            state == RBRGameState.Unknown1 || 
            state == RBRGameState.Unknown2 ||
            state == RBRGameState.Unknown3 ||
            state == RBRGameState.Unknown4)
        {
            return GameState.Unknown;
        } else if (state == RBRGameState.RaceBegin)
        {
            return GameState.RaceBegin;
        } else if (state == RBRGameState.Racing)
        {
            return GameState.Racing;
        } else if (state == RBRGameState.Paused)
        {
            return GameState.Paused;
        } else if (state == RBRGameState.RaceEndOrReplay0 || state == RBRGameState.RaceEnd || state == RBRGameState.RaceEndOrReplay1)
        {
            return GameState.RaceEnd;
        }

        return GameState.Unknown;
    }

    public override void onNewUdpMessage(byte[] oldMsg, byte[] newMsg)
    {
        var lastUdp = oldMsg.CastToStruct<RBRUdpData>();
        var newUdp = newMsg.CastToStruct<RBRUdpData>();

        _currentGameData = getGameDataFromUdp(_currentGameData, newUdp);
    }

    private GameData getGameDataFromUdp(GameData gameData, RBRUdpData data)
    {
        gameData.TimeStamp = DateTime.Now;
        gameData.Time = data.stage.raceTime;
        gameData.LapTime = data.stage.raceTime;
        gameData.LapDistance = data.stage.distanceToEnd / (1 - data.stage.progress) * data.stage.progress;
        gameData.CompletionRate = data.stage.progress;
        gameData.Speed = data.car.speed;
        // gameData.TrackLength = data.stage.distanceToEnd / (1 - data.stage.progress);
        // gameData.SpeedRearLeft = data.contro[0];
        // gameData.SpeedRearRight = data.car.wheelSpeed[1];
        // gameData.SpeedFrontLeft = data.car.wheelSpeed[2];
        // gameData.SpeedFrontRight = data.car.wheelSpeed[3];

        gameData.Clutch = data.control.clutch;
        gameData.Brake = data.control.brake;
        gameData.Throttle = data.control.throttle;
        gameData.Steering = data.control.steering;
        gameData.Gear = data.control.gear;
        
        
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
        // TODO: implement
        gameData.TimeStamp = DateTime.Now;
        gameData.Clutch = data.Clutch;
        gameData.Brake = data.Brake;
        gameData.Throttle = data.Throttle;
        gameData.Steering = data.Steering;
        gameData.Gear = data.GearId;
        gameData.Speed = data.SpeedKMH;
        gameData.TrackLength = data.TrackLength;
        gameData.LapDistance = data.DistanceFromStart;
        gameData.CompletionRate = data.DistanceFromStart / data.TrackLength;
        gameData.MaxRPM = 10000f;
        gameData.MaxGears = 6;
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

        return gameData;
    }
}
