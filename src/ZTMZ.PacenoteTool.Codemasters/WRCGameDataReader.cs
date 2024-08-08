// should listen several udp ports when available

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.Codemasters;

public class WRCGameDataReader : DirtGameDataReader
{
    // private GameData LastGameData;
    
    // private GameData CurrentGameData;

    private WRCDataStructure _lastPacket;

    private WRCDataStructure _currentPacket;
    public override string TrackName => 
        WRCHelper.Instance.GetItinerary(_game, _currentPacket.location_id, _currentPacket.route_id);


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
    private event Action _onCarReset;
    public override event Action onCarReset
    {
        add { _onCarReset += value; }
        remove { _onCarReset -= value; }
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
        _lastPacket = oldPacket;
        _currentPacket = packet;

        var newGameData = this.RawData2GameData(packet);
        _onNewGameData?.Invoke(LastGameData, newGameData);
        CurrentGameData = newGameData;

        if (!packet.Equals(oldPacket))
        {
            var spdDiff = LastGameData.Speed - CurrentGameData.Speed;
            if (Config.Instance.PlayCollisionSound && CurrentGameData.Speed != 0)
            {
                CollisionSeverity severity = CarEventDetector.DetectCollision(LastGameData, CurrentGameData);
                    
                if (severity != CollisionSeverity.None) 
                {
                    _onCarDamaged?.Invoke(new CarDamageEvent
                    {
                        DamageType = CarDamage.Collision,
                        Parameters = new Dictionary<string, object> { { CarDamageConstants.SEVERITY, severity } }
                    });
                }
            }
            
            if (CarEventDetector.IsCarReset(LastGameData, CurrentGameData))
            {
                _onCarReset?.Invoke();
            }

            if (CurrentGameData.CompletionRate <= 0.0f && CurrentGameData.LapTime <= 0.0f) {
                if (this.GameState != GameState.RaceBegin)
                {
                    this.GameState = GameState.RaceBegin;
                }
            } else if (CurrentGameData.CompletionRate >= 1.0f) {
                if (this.GameState != GameState.RaceEnd)
                {
                    this.GameState = GameState.RaceEnd;
                }
            } else {
                if (this.GameState == GameState.Unknown)
                {
                    this.GameState = GameState.AdHocRaceBegin;
                } else if (this.GameState != GameState.Racing)
                {
                    this.GameState = GameState.Racing;
                }
            }
            
            this.LastGameData = this.CurrentGameData;
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
        message.Speed = wrcData.vehicle_speed * 3.6f; // m/s -> km/h
        message.TrackLength = (float)wrcData.stage_length;
        message.CompletionRate = wrcData.stage_progress;
        message.LapDistance = message.TrackLength * message.CompletionRate;
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
        message.HandBrake = wrcData.vehicle_handbrake;
        message.HandBrakeValid = true;

        message.Steering = wrcData.vehicle_steering;
        message.Gear = wrcData.vehicle_gear_index;
        message.MaxGears = wrcData.vehicle_gear_maximum;
        message.RPM = wrcData.vehicle_engine_rpm_current;
        message.MaxRPM = wrcData.vehicle_engine_rpm_max;
        message.IdleRPM = wrcData.vehicle_engine_rpm_idle;

        message.ShiftLightsFraction = wrcData.shiftlights_fraction;
        message.ShiftLightsRPMStart = wrcData.shiftlights_rpm_start;
        message.ShiftLightsRPMEnd = wrcData.shiftlights_rpm_end;
        message.ShiftLightsRPMValid = wrcData.shiftlights_rpm_valid;
        
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

        

        // calculate G long and G lat according to the vehicle's direction and acceleration
        var forward = new Vector3(wrcData.vehicle_forward_direction_x, wrcData.vehicle_forward_direction_y, wrcData.vehicle_forward_direction_z);
        var acceleration = new Vector3(wrcData.vehicle_acceleration_x, wrcData.vehicle_acceleration_y, wrcData.vehicle_acceleration_z);
        var gLong = Vector3.Dot(acceleration, forward);
        var gLat = Vector3.Dot(acceleration, Vector3.Cross(forward, new Vector3(0, 1, 0)));
        message.G_long = gLong / 9.8f;
        message.G_lat = -gLat / 9.8f;

        message.GameSpecificData = wrcData;

        return message;
    }
}

