

using System;
using System.Collections.Generic;
using System.Diagnostics;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.Codemasters;

public class DirtGameDataReader : UdpGameDataReader
{
    public override GameState GameState
    {
        set
        {
            var lastGameState = this._gameState;
            this._gameState = value;
            this.onGameStateChanged?.Invoke(new GameStateChangeEvent { LastGameState = lastGameState, NewGameState = this._gameState });
        }
        get => this._gameState;
    }

    public override GameData LastGameData { get => _lastGameData; set => _lastGameData = value; }
    public override string TrackName => DRHelper.Instance.GetItinerary(_game, _lastRawData.TrackLength.ToString(".2f"), _lastRawData.PosZ );

    public GameState _gameState;
    private GameData _lastGameData;

    private DirtRawData _lastRawData;

    private GameData _currentGameData;
    private DirtRawData _currentRawData;

    public override event Action<GameData, GameData> onNewGameData;
    public override event Action<GameStateChangeEvent> onGameStateChanged;
    public override event Action<CarDamageEvent> onCarDamaged;

    public override void onNewUdpMessage(byte[] lastMsg, byte[] newMsg)
    {
        DirtRawData message = new DirtRawData();
        message.TimeStamp = DateTime.Now;
        message.Time = BitConverter.ToSingle(newMsg, 0);
        message.LapTime = BitConverter.ToSingle(newMsg, 4);
        message.LapDistance = BitConverter.ToSingle(newMsg, 8);
        message.CompletionRate = BitConverter.ToSingle(newMsg, 12);
        message.Speed = BitConverter.ToSingle(newMsg, 28) * 3.6f; // m/s -> km/h
        message.TrackLength = BitConverter.ToSingle(newMsg, 244);
        message.PosX = BitConverter.ToSingle(newMsg, 16);
        message.PosY = BitConverter.ToSingle(newMsg, 20);
        message.PosZ = BitConverter.ToSingle(newMsg, 24);
        message.SpeedX = BitConverter.ToSingle(newMsg, 32);
        message.SpeedY = BitConverter.ToSingle(newMsg, 36);
        message.SpeedZ = BitConverter.ToSingle(newMsg, 40);
        message.RollX = BitConverter.ToSingle(newMsg, 44);
        message.RollY = BitConverter.ToSingle(newMsg, 48);
        message.RollZ = BitConverter.ToSingle(newMsg, 52);
        message.PitchX = BitConverter.ToSingle(newMsg, 56);
        message.PitchY = BitConverter.ToSingle(newMsg, 60);
        message.PitchZ = BitConverter.ToSingle(newMsg, 64);
        message.CarPos = BitConverter.ToSingle(newMsg, 39 << 2);
        message.SpeedFrontLeft = BitConverter.ToSingle(newMsg, 27 << 2) * 3.6f; // m/s -> km/h
        message.SpeedFrontRight = BitConverter.ToSingle(newMsg, 28 << 2) * 3.6f; // m/s -> km/h
        message.SpeedRearLeft = BitConverter.ToSingle(newMsg, 25 << 2) * 3.6f; // m/s -> km/h
        message.SpeedRearRight = BitConverter.ToSingle(newMsg, 26 << 2) * 3.6f; // m/s -> km/h

        message.Clutch = BitConverter.ToSingle(newMsg, 32 << 2);
        message.Brake = BitConverter.ToSingle(newMsg, 31 << 2);
        message.Throttle = BitConverter.ToSingle(newMsg, 29 << 2);

        message.Steering = BitConverter.ToSingle(newMsg, 30 << 2);
        message.Gear = BitConverter.ToSingle(newMsg, 33 << 2);
        message.MaxGears = BitConverter.ToSingle(newMsg, 65 << 2);
        message.RPM = BitConverter.ToSingle(newMsg, 37 << 2) * 10f;
        message.MaxRPM = BitConverter.ToSingle(newMsg, 63 << 2) * 10f;
        message.IdleRPM = BitConverter.ToSingle(newMsg, 64 << 2) * 10f;
        message.G_lat = BitConverter.ToSingle(newMsg, 34 << 2);
        message.G_long = BitConverter.ToSingle(newMsg, 35 << 2);

        message.BrakeTempRearLeft = BitConverter.ToSingle(newMsg, 51 << 2);
        message.BrakeTempRearRight = BitConverter.ToSingle(newMsg, 52 << 2);
        message.BrakeTempFrontLeft = BitConverter.ToSingle(newMsg, 53 << 2);
        message.BrakeTempFrontRight = BitConverter.ToSingle(newMsg, 54 << 2);

        message.SuspensionRearLeft = BitConverter.ToSingle(newMsg, 17 << 2);
        message.SuspensionRearRight = BitConverter.ToSingle(newMsg, 18 << 2);
        message.SuspensionFrontLeft = BitConverter.ToSingle(newMsg, 19 << 2);
        message.SuspensionFrontRight = BitConverter.ToSingle(newMsg, 20 << 2);

        message.SuspensionSpeedRearLeft = BitConverter.ToSingle(newMsg, 21 << 2);
        message.SuspensionSpeedRearRight = BitConverter.ToSingle(newMsg, 22 << 2);
        message.SuspensionSpeedFrontLeft = BitConverter.ToSingle(newMsg, 23 << 2);
        message.SuspensionSpeedFrontRight = BitConverter.ToSingle(newMsg, 24 << 2);

        message.CurrentLap = BitConverter.ToSingle(newMsg, 36 << 2);
        message.LapsComplete = BitConverter.ToSingle(newMsg, 59 << 2);
        message.LastLapTime = BitConverter.ToSingle(newMsg, 62 << 2);
        message.TotalLaps = BitConverter.ToSingle(newMsg, 60 << 2);

        message.Sector = BitConverter.ToSingle(newMsg, 48 << 2);
        message.Sector1Time = BitConverter.ToSingle(newMsg, 49 << 2);
        message.Sector2Time = BitConverter.ToSingle(newMsg, 50 << 2);

        _currentRawData = message;

        var newGameData = RawData2GameData(message);

        onNewGameData?.Invoke(_lastGameData, newGameData);
        _currentGameData = newGameData;

        if (!newGameData.Equals(this._lastGameData))
        {
            var spdDiff = _lastGameData.Speed - newGameData.Speed;
            if (Config.Instance.PlayCollisionSound && newGameData.Speed != 0)
            {
                int severity = -1;
                // collision happens. speed == 0 means reset or end stage
                if (spdDiff >= Config.Instance.CollisionSpeedChangeThreshold_Severe)
                    severity = 2;
                else if (spdDiff >= Config.Instance.CollisionSpeedChangeThreshold_Medium)
                    severity = 1;
                else if (spdDiff >= Config.Instance.CollisionSpeedChangeThreshold_Slight)
                    severity = 0;
                    
                if (severity != -1) 
                {
                    onCarDamaged?.Invoke(new CarDamageEvent
                    {
                        DamageType = CarDamage.Collision,
                        Parameters = new Dictionary<string, object> { { CarDamageConstants.SEVERITY, severity } }
                    });
                }
            }

            if (newGameData.LapTime > 0 && this.GameState != GameState.Racing)
            {
                this.GameState = GameState.Racing;
            }
            else if (newGameData.LapTime == 0 && newGameData.LapDistance <= 0)
            {
                // CountDown not works in Daily Event, only works for TimeTrial
                //if (message.Time != this.LastMessage.Time)
                //{
                //    if (this.GameState != GameState.CountDown)
                //        this.GameState = GameState.CountDown;
                //}
                //else 
                if (newGameData.Time == 0 && this.GameState != GameState.RaceEnd)
                {
                    this.GameState = GameState.RaceEnd;
                }
                else if (this.GameState != GameState.RaceBegin)
                {
                    this.GameState = GameState.RaceBegin;
                }
            }
            //else if (message.LapTime == 0 && this.GameState != GameState.RaceEnd)
            //{
            //    this.GameState = GameState.RaceEnd;
            //}
        }
        else
        {
            if (this.GameState == GameState.Racing || this.GameState == GameState.RaceBegin ||
                this.GameState == GameState.CountingDown)
            {
                this.GameState = GameState.Paused;
            }
        }

        _lastGameData = newGameData;
        _lastRawData = message;
    }

    private GameData RawData2GameData(DirtRawData data) 
    {
        GameData gameData = new();
        gameData.TimeStamp = data.TimeStamp;
        gameData.Time = data.Time;
        gameData.LapTime = data.LapTime;
        gameData.LapDistance = data.LapDistance;
        gameData.CompletionRate = data.CompletionRate;
        gameData.Speed = data.Speed;
        gameData.TrackLength = data.TrackLength;
        gameData.CarPos = data.CarPos;
        gameData.SpeedFrontLeft = data.SpeedFrontLeft;
        gameData.SpeedFrontRight = data.SpeedFrontRight;
        gameData.SpeedRearLeft = data.SpeedRearLeft;
        gameData.SpeedRearRight = data.SpeedRearRight;

        gameData.Clutch = data.Clutch;
        gameData.Brake = data.Brake;
        gameData.Throttle = data.Throttle;

        gameData.Steering = data.Steering;
        gameData.Gear = data.Gear;
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

        return gameData;
    }
}

