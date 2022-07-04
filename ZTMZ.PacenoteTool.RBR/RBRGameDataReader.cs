

using System;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.RBR;

public class RBRGameDataReader : UdpGameDataReader
{
    public override GameState GameState
    {
        set
        {
            var lastGameState = this._gameState;
            this._gameState = value;
            this._onGameStateChanged?.Invoke(new GameStateChangeEvent { LastGameState = lastGameState, NewGameState = this._gameState });
        }
        get => this._gameState;
    }
    public override GameData LastGameData { get => _lastGameData; set => _lastGameData = value; }

    public override string TrackName => throw new System.NotImplementedException();
    public GameState _gameState;
    private GameData _lastGameData;

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
        return true;
    }

    public override void onNewUdpMessage(byte[] oldMsg, byte[] newMsg)
    {
        var lastUdp = oldMsg.CastToStruct<RBRUdpData>();
        var newUdp = newMsg.CastToStruct<RBRUdpData>();
    }

    private GameData getGameData(RBRUdpData data)
    {
        var gameData = new GameData();
        gameData.TimeStamp = DateTime.Now;
        gameData.Time = data.stage.raceTime;
        gameData.LapTime = data.stage.raceTime;
        gameData.LapDistance = data.stage.distanceToEnd / (1 - data.stage.progress) * data.stage.progress;
        gameData.CompletionRate = data.stage.progress;
        gameData.Speed = data.car.speed;
        gameData.TrackLength = data.stage.distanceToEnd / (1 - data.stage.progress);
        // gameData.SpeedRearLeft = data.contro[0];
        // gameData.SpeedRearRight = data.car.wheelSpeed[1];
        // gameData.SpeedFrontLeft = data.car.wheelSpeed[2];
        // gameData.SpeedFrontRight = data.car.wheelSpeed[3];

        gameData.Clutch = data.control.clutch;
        gameData.Brake = data.control.brake;
        gameData.Throttle = data.control.throttle;
        gameData.Steering = data.control.steering;
        gameData.Gear = data.control.gear;
        
        
        gameData.MaxGears = data.MaxGears;
        gameData.RPM = data.RPM;
        gameData.MaxRPM = data.MaxRPM;
        gameData.IdleRPM = data.IdleRPM;
        gameData.G_lat = data.G_lat;
        gameData.G_long = data.G_long;

        gameData.BrakeTempRearLeft = data.BrakeTempRearLeft;
        gameData.BrakeTempRearRight = data.BrakeTempRearRight;
        gameData.BrakeTempFrontLeft = data.BrakeTempFrontLeft;
        gameData.BrakeTempFrontRight = data.BrakeTempFrontRight;

        gameData.SuspensionRearLeft = data.SuspensionRearLeft;
        gameData.SuspensionRearRight = data.SuspensionRearRight;
        gameData.SuspensionFrontLeft = data.SuspensionFrontLeft;
        gameData.SuspensionFrontRight = data.SuspensionFrontRight;

        gameData.SuspensionSpeedRearLeft = data.SuspensionSpeedRearLeft;
        gameData.SuspensionSpeedRearRight = data.SuspensionSpeedRearRight;
        gameData.SuspensionSpeedFrontLeft = data.SuspensionSpeedFrontLeft;
        gameData.SuspensionSpeedFrontRight = data.SuspensionSpeedFrontRight;

        gameData.Stage = udpData.stage;
        gameData.Control = udpData.control;
        gameData.Car = udpData.car;
        return gameData;
    }
}
