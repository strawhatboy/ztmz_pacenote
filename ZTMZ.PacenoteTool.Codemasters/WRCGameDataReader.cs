// should listen several udp ports when available

using System;
using System.Collections.Generic;
using System.Globalization;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.Codemasters;

public class WRCGameDataReader : DirtGameDataReader
{
    private GameData _lastGameData;
    
    private GameData _currentGameData;
    public override string TrackName => 
        WRCHelper.Instance.GetItinerary(_game, _currentGameData.TrackLength.ToString("f2", CultureInfo.InvariantCulture), _currentGameData.PosZ );


    private event Action<CarDamageEvent> _onCarDamaged;
    public override event Action<CarDamageEvent> onCarDamaged
    {
        add { _onCarDamaged += value; }
        remove { _onCarDamaged -= value; }
    }
    private event Action<GameData, GameData> _onNewGameData;
    public override event Action<GameData, GameData> onNewGameData
    {
        add 
        {
            _onNewGameData += value;
        }
        remove { _onNewGameData -= value; }
    }

    public override void onNewUdpMessage(byte[] lastMsg, byte[] newMsg)
    {
        // this is stupid
        this._timerCount = 0;

        if (newMsg.Length == 0)
        {
            return;
        }

        var oldPacket = lastMsg.CastToStruct<WRCDataStructure>();
        var packet = newMsg.CastToStruct<WRCDataStructure>();

        var newGameData = this.RawData2GameData(packet);
        _onNewGameData?.Invoke(_lastGameData, newGameData);
        _currentGameData = newGameData;

        if (!packet.Equals(oldPacket))
        {
            var spdDiff = _lastGameData.Speed - _currentGameData.Speed;
            if (Config.Instance.PlayCollisionSound && _currentGameData.Speed != 0)
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

            if (_currentGameData.LapTime > 0 && this.GameState != GameState.Racing)
            {
                if (this.GameState == GameState.Unknown)
                {
                    this.GameState = GameState.AdHocRaceBegin;
                } else {
                    this.GameState = GameState.Racing;
                }
            }
            else if (_currentGameData.LapTime == 0 && _currentGameData.LapDistance <= 0)
            {
                // CountDown not works in Daily Event, only works for TimeTrial
                //if (message.Time != this.LastMessage.Time)
                //{
                //    if (this.GameState != GameState.CountDown)
                //        this.GameState = GameState.CountDown;
                //}
                //else 
                if (_currentGameData.Time == 0 && this.GameState != GameState.RaceEnd)
                {
                    this.GameState = GameState.RaceEnd;
                }
                else if (this.GameState != GameState.RaceBegin)
                {
                    this.GameState = GameState.RaceBegin;
                }
            }
            
            this._lastGameData = this._currentGameData;
        }
        else
        {
            if (this.GameState == GameState.Racing || this.GameState == GameState.RaceBegin ||
                this.GameState == GameState.CountingDown)
            {
                this.GameState = GameState.Paused;
            }
        }
    }

    private GameData RawData2GameData(WRCDataStructure wrcData) {
        var message = new GameData();
        message.TimeStamp = DateTime.Now;
        message.Time = wrcData.game_total_time;
        message.LapTime = wrcData.stage_current_time;
        message.LapDistance = (float)wrcData.stage_current_distance;
        message.Speed = wrcData.vehicle_speed * 3.6f; // m/s -> km/h
        message.TrackLength = (float)wrcData.stage_length;
        message.CompletionRate = message.LapDistance / message.TrackLength;
        message.PosX = wrcData.vehicle_position_x;
        message.PosY = wrcData.vehicle_position_y;
        message.PosZ = wrcData.vehicle_position_z;
        // message.CarPos = BitConverter.ToSingle(rawData, 39 << 2);
        message.SpeedFrontLeft = wrcData.vehicle_cp_forward_speed_fl * 3.6f; // m/s -> km/h
        message.SpeedFrontRight = wrcData.vehicle_cp_forward_speed_fr * 3.6f; // m/s -> km/h
        message.SpeedRearLeft = wrcData.vehicle_cp_forward_speed_bl * 3.6f; // m/s -> km/h
        message.SpeedRearRight = wrcData.vehicle_cp_forward_speed_br * 3.6f; // m/s -> km/h

        message.Clutch = wrcData.vehicle_clutch;
        message.Brake = wrcData.vehicle_brake;
        message.Throttle = wrcData.vehicle_throttle;

        message.Steering = wrcData.vehicle_steering;
        message.Gear = wrcData.vehicle_gear_index;
        message.MaxGears = wrcData.vehicle_gear_maximum;
        message.RPM = wrcData.vehicle_engine_rpm_current;
        message.MaxRPM = wrcData.vehicle_engine_rpm_max;
        message.IdleRPM = wrcData.vehicle_engine_rpm_idle;
        message.G_lat = -wrcData.vehicle_acceleration_x / 10f;
        message.G_long = -wrcData.vehicle_acceleration_y / 10f;
        
        message.BrakeTempRearLeft = wrcData.vehicle_brake_temperature_bl;
        message.BrakeTempRearRight = wrcData.vehicle_brake_temperature_br;
        message.BrakeTempFrontLeft = wrcData.vehicle_brake_temperature_fl;
        message.BrakeTempFrontRight = wrcData.vehicle_brake_temperature_fr;
        
        message.SuspensionRearLeft = wrcData.vehicle_hub_position_bl;
        message.SuspensionRearRight = wrcData.vehicle_hub_position_br;
        message.SuspensionFrontLeft = wrcData.vehicle_hub_position_fl;
        message.SuspensionFrontRight = wrcData.vehicle_hub_position_fr;
        
        message.SuspensionSpeedRearLeft = wrcData.vehicle_hub_velocity_bl;
        message.SuspensionSpeedRearRight = wrcData.vehicle_hub_velocity_br;
        message.SuspensionSpeedFrontLeft = wrcData.vehicle_hub_velocity_fl;
        message.SuspensionSpeedFrontRight = wrcData.vehicle_hub_velocity_fr;

        return message;
    }
}

