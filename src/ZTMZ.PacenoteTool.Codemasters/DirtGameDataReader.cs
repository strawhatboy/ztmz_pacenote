

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Reflection.Metadata.Ecma335;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.Codemasters;

public class DirtGameDataReader : UdpGameDataReader
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    public override GameState GameState
    {
        set
        {
            var lastGameState = this._gameState;
            if (lastGameState == value) return;
            this._gameState = value;
            if (value == GameState.AdHocRaceBegin || value == GameState.RaceBegin)
            {
                this._currentCarShiftPercentage = DRHelper.Instance.GetCarShiftPercentage(_game, _currentRawData.IdleRPM, _currentRawData.MaxRPM, _currentRawData.MaxGears);
            }
            if (value != GameState.RaceEnd) {
                this._onGameStateChanged?.Invoke(new GameStateChangeEvent { LastGameState = lastGameState, NewGameState = this._gameState });
            }
        }
        get => this._gameState;
    }

    
    // add parameters like "retired", "finish_time", "retired_reason"
    private void onGameStateRaceEnd(Dictionary<string, object> Parameters)
    {
        var lastGameState = this._gameState;
        this._gameState = GameState.RaceEnd;
        this._onGameStateChanged?.Invoke(new GameStateChangeEvent { LastGameState = lastGameState, NewGameState = GameState.RaceEnd, Parameters = Parameters });
    }

    public override GameData LastGameData { get => _lastGameData; set => _lastGameData = value; }
    public override GameData CurrentGameData { get => _currentGameData; set => _currentGameData = value; }
    public override string TrackName => _trackName ??= DRHelper.Instance.GetItinerary(_game,
        _currentRawData.TrackLength.ToString("f2", CultureInfo.InvariantCulture), _currentRawData.PosZ);

    public override string CarName => _nativeCarCode is not null && DRHelper.Instance.CarCodes_DR2.TryGetValue(_nativeCarCode, out var car)
        ? car[0] : DRHelper.Instance.GetCarName(_game, _currentRawData.IdleRPM, _currentRawData.MaxRPM, _currentRawData.MaxGears);
    public override string CarClass => _nativeCarCode is not null && DRHelper.Instance.CarCodes_DR2.TryGetValue(_nativeCarCode, out var car)
        ? car[1] : DRHelper.Instance.GetCarClass(_game, _currentRawData.IdleRPM, _currentRawData.MaxRPM, _currentRawData.MaxGears);

    public GameState _gameState;
    private GameData _lastGameData;

    private DirtRawData _lastRawData;

    private GameData _currentGameData;
    private DirtRawData _currentRawData;

    private float _currentCarShiftPercentage = 0.9f;
    private Dr2VehicleMemoryReader? _vehicleMemory;
    private string? _nativeCarCode;
    private readonly Dr2Countdown _countdown = new();
    private bool _preStart;
    private bool _hasPacket;
    private bool _observedInitialUdpWait;
    private string? _trackName;
    private readonly object _stateGate = new();
    private readonly Dr2RaceResultTracker _dr2RaceResult = new();
    private long _nextRaceStateRead;

    protected virtual Dr2RaceStateSnapshot? ReadDr2RaceState()
    {
        if (!OperatingSystem.IsWindows() || !isInitialized) return null;
        _vehicleMemory ??= new Dr2VehicleMemoryReader();
        return _vehicleMemory.ReadRaceState();
    }

    private void UpdateDr2RaceResult(bool force)
    {
        if (_game is not DirtRally2 || !_hasPacket) return;
        if (!force && Environment.TickCount64 < _nextRaceStateRead) return;
        _nextRaceStateRead = Environment.TickCount64 + 200;
        var result = _dr2RaceResult.Observe(ReadDr2RaceState(), _lastValidLapTime > 0);
        if (result.HasValue && GameState != GameState.RaceEnd)
        {
            Logger.Info("DR2 native race result: {0}.", result.Value);
            onGameStateRaceEnd(new Dictionary<string, object>
            {
                [GameStateRaceEndProperty.FINISH_STATE] = result.Value
            });
            _lastValidLapTime = 0;
            _preStart = false;
            _countdown.Reset();
        }
    }

    protected override void UpdateStateForUdpSilence()
    {
        lock (_stateGate)
        {
            // DR2 can stop sending UDP on a retirement/result screen. The base
            // timer keeps this read alive without treating silence as a result.
            UpdateDr2RaceResult(true);
            if (_dr2RaceResult.HasAbnormalResult) return;
            UpdateDr2StateForUdpSilence();
        }
    }

    private void UpdateDr2StateForUdpSilence()
    {
        // A listener already waiting before the first grid packet has observed
        // the start of the stream. No previous race/reset packet is required.
        // Do not treat an immediate attachment to an existing stream this way.
        if (_game is DirtRally2 && !_hasPacket) _observedInitialUdpWait = true;
        // DR2 stops UDP while waiting for the handbrake after its reset packet.
        // Transport availability is still reported by the base timer. Silence
        // here must not destroy an observed pre-start session.
        if (_game is DirtRally2 && _preStart) return;
        base.UpdateStateForUdpSilence();
    }

    public override void Uninitialize(IGame game)
    {
        base.Uninitialize(game);
        lock (_stateGate)
        {
            if (OperatingSystem.IsWindows()) _vehicleMemory?.Dispose();
            _vehicleMemory = null;
            _nativeCarCode = null;
            _countdown.Reset();
            _preStart = _hasPacket = false;
            _observedInitialUdpWait = false;
            _trackName = null;
            _lastValidLapTime = 0;
            _lastRawData = _currentRawData = default;
            _lastGameData = _currentGameData = default;
            _gameState = GameState.Unknown;
            _dr2RaceResult.Reset();
            _nextRaceStateRead = 0;
        }
    }

    private event Action<GameData, GameData> _onNewGameData;

    private float _lastValidLapTime = 0f;

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

    public override void onNewUdpMessage(byte[] lastMsg, byte[] newMsg)
    {
        lock (_stateGate) ProcessUdpMessage(lastMsg, newMsg);
    }

    private void ProcessUdpMessage(byte[] lastMsg, byte[] newMsg)
    {
        // Extra-data 3 contains 66 floats. Do not parse or publish truncated packets.
        if (newMsg is null || newMsg.Length < 264) return;
        base.onNewUdpMessage(lastMsg, newMsg);
        DirtRawData message = RawBytesData2RawData(newMsg);
        DirtRawData lastMessage = RawBytesData2RawData(lastMsg);

        if (!_hasPacket || message.TrackLength != _currentRawData.TrackLength ||
            (message.LapTime == 0 && _currentRawData.LapTime > 0)) _trackName = null;
        _currentRawData = message;

        // Select the current vehicle before converting even its first packet.
        _currentCarShiftPercentage = DRHelper.Instance.GetCarShiftPercentage(
            _game, message.IdleRPM, message.MaxRPM, message.MaxGears);

        var newGameData = RawData2GameData(message);

        _currentGameData = newGameData;
        // Do not let a cached native Racing sample allow a stale UDP finish
        // time to win over a retirement that just happened.
        UpdateDr2RaceResult(message.Time == 0 && _lastValidLapTime > 0);

        if (!message.Equals(lastMessage))
        {
            var spdDiff = _lastGameData.Speed - newGameData.Speed;
            if (lastMsg?.Length >= 264 && Config.Instance.PlayCollisionSound && newGameData.Speed != 0)
            {
                CollisionSeverity severity = CarEventDetector.DetectCollision(_lastGameData, newGameData);

                if (severity != CollisionSeverity.None) 
                {
                    _onCarDamaged?.Invoke(new CarDamageEvent
                    {
                        DamageType = CarDamage.Collision,
                        Parameters = new Dictionary<string, object> { { CarDamageConstants.SEVERITY, severity } }
                    });
                }
            }

            if (lastMsg?.Length >= 264 && CarEventDetector.IsCarReset(_lastGameData, newGameData))
            {
                _onCarReset?.Invoke();
            }

            if (_game is DirtRally2)
            {
                if (!_dr2RaceResult.HasAbnormalResult) UpdateDr2State(message, lastMessage);
            }
            else if (newGameData.LapTime > 0 && this.GameState != GameState.Racing)
            {
                if (this.GameState == GameState.Unknown)
                {
                    this.GameState = GameState.AdHocRaceBegin;
                } else {
                    this.GameState = GameState.Racing;
                }
                this._lastValidLapTime = newGameData.LapTime;
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
                    // this should be normal race end for DR2
                    if (_game is DirtRally2 && _lastValidLapTime > 0 && lastMessage.LastLapTime > 0) {
                        onGameStateRaceEnd(new Dictionary<string, object> { 
                            { GameStateRaceEndProperty.FINISH_TIME, lastMessage.LastLapTime },
                            { GameStateRaceEndProperty.FINISH_STATE, GameStateRaceEnd.Normal }
                        });
                        _lastValidLapTime = 0;
                    }
                }
                else if (this.GameState != GameState.RaceBegin)
                {
                    this.GameState = GameState.RaceBegin;
                }
            } 
            
            // for DR1
            if (message.TotalLaps == message.LapsComplete && this.GameState != GameState.RaceEnd && _game is DirtRally) {
                // this should be normal, this should be the right way to detect raceend
                onGameStateRaceEnd(new Dictionary<string, object> { 
                    { GameStateRaceEndProperty.FINISH_TIME, message.LastLapTime },
                    { GameStateRaceEndProperty.FINISH_STATE, GameStateRaceEnd.Normal }
                });
            }
        }
        else
        {
            if (this.GameState == GameState.Racing || this.GameState == GameState.AdHocRaceBegin || this.GameState == GameState.RaceBegin ||
                this.GameState == GameState.CountingDown)
            {
                this.GameState = GameState.Paused;
            }
        }

        // Session listeners must see the current snapshot and prepare the session
        // before its first telemetry notification reaches dashboards / replay.
        _onNewGameData?.Invoke(_lastGameData, newGameData);
        _lastGameData = newGameData;
        _lastRawData = message;
        _hasPacket = true;
    }

    private void UpdateDr2State(DirtRawData message, DirtRawData previous)
    {
        if (message.TrackLength <= 0)
        {
            // The menu/loading packet has no track. End the old pre-start
            // context as well, including when leaving before the start.
            _preStart = false;
            _countdown.Reset();
            _lastValidLapTime = 0;
            _dr2RaceResult.Reset();
            return;
        }
        if (message.LapTime > 0)
        {
            _lastValidLapTime = message.LapTime;
            if (_preStart)
            {
                // A pause on the grid must not turn the actual Go into a resume.
                if (GameState is not (GameState.RaceBegin or GameState.CountingDown)) SetCountdownState(null);
                _preStart = false;
                _countdown.Reset();
                GameState = GameState.Racing;
            }
            else if (!_hasPacket || GameState is GameState.Unknown or GameState.RaceEnd)
                GameState = GameState.AdHocRaceBegin;
            else GameState = GameState.Racing;
            return;
        }
        if (message.Time == 0)
        {
            var finish = Math.Max(message.LastLapTime, previous.LastLapTime);
            if (_lastValidLapTime > 0 && finish > 0 && GameState != GameState.RaceEnd)
                onGameStateRaceEnd(new Dictionary<string, object>
                {
                    [GameStateRaceEndProperty.FINISH_TIME] = finish,
                    [GameStateRaceEndProperty.FINISH_STATE] = GameStateRaceEnd.Normal
                });
            _lastValidLapTime = 0;
            _preStart = false;
            _countdown.Reset();
            return;
        }
        if (message.LapDistance > 0) return;
        if (!_preStart)
        {
            _lastValidLapTime = 0;
            _dr2RaceResult.Reset();
            _preStart = true;
            _countdown.Begin(message.Time,
                (!_hasPacket && _observedInitialUdpWait) ||
                (_hasPacket && (previous.LapTime > 0 || previous.Time == 0 || previous.TrackLength <= 0)));
            GameState = GameState.RaceBegin;
        }
        if (_countdown.Update(message.Time) is { } number) SetCountdownState(number);
    }

    private void SetCountdownState(int? number)
    {
        var last = _gameState;
        _gameState = GameState.CountingDown;
        _onGameStateChanged?.Invoke(new GameStateChangeEvent
        {
            LastGameState = last,
            NewGameState = GameState.CountingDown,
            Parameters = number.HasValue ? new Dictionary<string, object> { ["number"] = number.Value } : null
        });
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
        if (_game is DirtRally2)
        {
            // DR2 exports radians/second. Keep the legacy raw fingerprint used
            // by car_dict_dr2, but publish actual revolutions/minute to GameData.
            var correction = Dr2VehicleMemory.AngularVelocityToRPM / 10f;
            gameData.RPM *= correction;
            gameData.MaxRPM *= correction;
            gameData.IdleRPM *= correction;
        }
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
        gameData.HandBrakeValid = false;
        _nativeCarCode = null;

        gameData.ShiftLightsRPMValid = float.IsFinite(gameData.MaxRPM) && gameData.MaxRPM > 1000;
        gameData.ShiftLightsRPMEnd = gameData.MaxRPM * _currentCarShiftPercentage;
        if (_game is DirtRally2 && OperatingSystem.IsWindows())
        {
            _vehicleMemory ??= new Dr2VehicleMemoryReader();
            if (_vehicleMemory.Read(data.IdleRPM / 10f, data.MaxRPM / 10f) is { } vehicle)
            {
                gameData.HandBrake = vehicle.HandBrake;
                gameData.HandBrakeValid = true;
                gameData.ShiftLightsRPMEnd = vehicle.ShiftRPM;
                _nativeCarCode = vehicle.CarCode;
                gameData.CarDamage = vehicle.Damage ?? CarDamageSnapshot.Unavailable("DR2");
                gameData.Turbo = vehicle.Turbo;
            }
        }
        gameData.ShiftLightsRPMStart = Math.Max(0, gameData.ShiftLightsRPMEnd - 1000);
        gameData.ShiftLightsFraction = gameData.ShiftLightsRPMValid && float.IsFinite(gameData.RPM)
            ? Math.Clamp((gameData.RPM - gameData.ShiftLightsRPMStart) /
                (gameData.ShiftLightsRPMEnd - gameData.ShiftLightsRPMStart), 0f, 1f) : 0f;

        gameData.GameSpecificData = data;

        return gameData;
    }

    private DirtRawData RawBytesData2RawData(byte[] raw)
    {
        DirtRawData message = new DirtRawData();
        if (raw == null || raw.Length < 264)
        {
            return message;
        }
        message.TimeStamp = DateTime.UtcNow;
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

