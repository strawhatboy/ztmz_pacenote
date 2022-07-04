

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
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
            this._onGameStateChanged?.Invoke(new GameStateChangeEvent { LastGameState = lastGameState, NewGameState = this._gameState });
        }
        get => this._gameState;
    }

    public override GameData LastGameData { get => _lastGameData; set => _lastGameData = value; }
    public override string TrackName => DRHelper.Instance.GetItinerary(_game, _currentRawData.TrackLength.ToString("f2", CultureInfo.InvariantCulture), _currentRawData.PosZ );

    public GameState _gameState;
    private GameData _lastGameData;

    private DirtRawData _lastRawData;

    private GameData _currentGameData;
    private DirtRawData _currentRawData;

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

    public override void onNewUdpMessage(byte[] lastMsg, byte[] newMsg)
    {
        DirtRawData message = RawBytesData2RawData(newMsg);
        DirtRawData lastMessage = RawBytesData2RawData(lastMsg);

        _currentRawData = message;

        var newGameData = RawData2GameData(message);

        _onNewGameData?.Invoke(_lastGameData, newGameData);
        _currentGameData = newGameData;

        if (!message.Equals(lastMessage))
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
                    _onCarDamaged?.Invoke(new CarDamageEvent
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

        gameData.PosX = data.PosX;
        gameData.PosY = data.PosY;
        gameData.PosZ = data.PosZ;

        return gameData;
    }

    private DirtRawData RawBytesData2RawData(byte[] raw)
    {
        DirtRawData message = new DirtRawData();
        if (raw == null || raw.Length == 0) 
        {
            return message;
        }
        message.TimeStamp = DateTime.Now;
        message.Time = BitConverter.ToSingle(raw, 0);
        message.LapTime = BitConverter.ToSingle(raw, 4);
        message.LapDistance = BitConverter.ToSingle(raw, 8);
        message.CompletionRate = BitConverter.ToSingle(raw, 12);
        message.Speed = BitConverter.ToSingle(raw, 28) * 3.6f; // m/s -> km/h
        message.TrackLength = BitConverter.ToSingle(raw, 244);
        message.PosX = BitConverter.ToSingle(raw, 16);
        message.PosY = BitConverter.ToSingle(raw, 20);
        message.PosZ = BitConverter.ToSingle(raw, 24);
        message.SpeedX = BitConverter.ToSingle(raw, 32);
        message.SpeedY = BitConverter.ToSingle(raw, 36);
        message.SpeedZ = BitConverter.ToSingle(raw, 40);
        message.RollX = BitConverter.ToSingle(raw, 44);
        message.RollY = BitConverter.ToSingle(raw, 48);
        message.RollZ = BitConverter.ToSingle(raw, 52);
        message.PitchX = BitConverter.ToSingle(raw, 56);
        message.PitchY = BitConverter.ToSingle(raw, 60);
        message.PitchZ = BitConverter.ToSingle(raw, 64);
        message.CarPos = BitConverter.ToSingle(raw, 39 << 2);
        message.SpeedFrontLeft = BitConverter.ToSingle(raw, 27 << 2) * 3.6f; // m/s -> km/h
        message.SpeedFrontRight = BitConverter.ToSingle(raw, 28 << 2) * 3.6f; // m/s -> km/h
        message.SpeedRearLeft = BitConverter.ToSingle(raw, 25 << 2) * 3.6f; // m/s -> km/h
        message.SpeedRearRight = BitConverter.ToSingle(raw, 26 << 2) * 3.6f; // m/s -> km/h

        message.Clutch = BitConverter.ToSingle(raw, 32 << 2);
        message.Brake = BitConverter.ToSingle(raw, 31 << 2);
        message.Throttle = BitConverter.ToSingle(raw, 29 << 2);

        message.Steering = BitConverter.ToSingle(raw, 30 << 2);
        message.Gear = BitConverter.ToSingle(raw, 33 << 2);
        message.MaxGears = BitConverter.ToSingle(raw, 65 << 2);
        message.RPM = BitConverter.ToSingle(raw, 37 << 2) * 10f;
        message.MaxRPM = BitConverter.ToSingle(raw, 63 << 2) * 10f;
        message.IdleRPM = BitConverter.ToSingle(raw, 64 << 2) * 10f;
        message.G_lat = BitConverter.ToSingle(raw, 34 << 2);
        message.G_long = BitConverter.ToSingle(raw, 35 << 2);

        message.BrakeTempRearLeft = BitConverter.ToSingle(raw, 51 << 2);
        message.BrakeTempRearRight = BitConverter.ToSingle(raw, 52 << 2);
        message.BrakeTempFrontLeft = BitConverter.ToSingle(raw, 53 << 2);
        message.BrakeTempFrontRight = BitConverter.ToSingle(raw, 54 << 2);

        message.SuspensionRearLeft = BitConverter.ToSingle(raw, 17 << 2);
        message.SuspensionRearRight = BitConverter.ToSingle(raw, 18 << 2);
        message.SuspensionFrontLeft = BitConverter.ToSingle(raw, 19 << 2);
        message.SuspensionFrontRight = BitConverter.ToSingle(raw, 20 << 2);

        message.SuspensionSpeedRearLeft = BitConverter.ToSingle(raw, 21 << 2);
        message.SuspensionSpeedRearRight = BitConverter.ToSingle(raw, 22 << 2);
        message.SuspensionSpeedFrontLeft = BitConverter.ToSingle(raw, 23 << 2);
        message.SuspensionSpeedFrontRight = BitConverter.ToSingle(raw, 24 << 2);

        message.CurrentLap = BitConverter.ToSingle(raw, 36 << 2);
        message.LapsComplete = BitConverter.ToSingle(raw, 59 << 2);
        message.LastLapTime = BitConverter.ToSingle(raw, 62 << 2);
        message.TotalLaps = BitConverter.ToSingle(raw, 60 << 2);

        message.Sector = BitConverter.ToSingle(raw, 48 << 2);
        message.Sector1Time = BitConverter.ToSingle(raw, 49 << 2);
        message.Sector2Time = BitConverter.ToSingle(raw, 50 << 2);
        return message;
    }
}

