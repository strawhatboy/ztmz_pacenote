using System;
using ZTMZ.PacenoteTool.Base;
namespace ZTMZ.PacenoteTool.Codemasters;

public struct DirtRawData
{
    public float Time;
    public float LapTime;
    public float LapDistance;
    public float CompletionRate; // 0-1, 0.5 means finished 50%
    public float Speed;
    public float TrackLength;
    public float PosX;
    public float PosY;
    public float PosZ;

    public float SpeedX;
    public float SpeedY;
    public float SpeedZ;

    public float RollX;
    public float RollY;
    public float RollZ;

    public float PitchX;
    public float PitchY;
    public float PitchZ;

    // Wheel Pressure
    public float SpeedRearLeft;
    public float SpeedRearRight;
    public float SpeedFrontLeft;

    public float SpeedFrontRight;

    // pedals (0-1)
    public float Clutch;
    public float Brake;
    public float Throttle;

    public float Steering;
    public float Gear;
    public float MaxGears;
    public float RPM;
    public float MaxRPM;
    public float IdleRPM;
    public float G_long;
    public float G_lat;

    // brake tmp
    public float BrakeTempRearLeft;
    public float BrakeTempRearRight;
    public float BrakeTempFrontLeft;
    public float BrakeTempFrontRight;

    // suspension
    public float SuspensionRearLeft;
    public float SuspensionRearRight;
    public float SuspensionFrontLeft;
    public float SuspensionFrontRight;

    public float SuspensionSpeedRearLeft;
    public float SuspensionSpeedRearRight;
    public float SuspensionSpeedFrontLeft;
    public float SuspensionSpeedFrontRight;

    public float CurrentLap;
    public float CarPos;
    public float Sector;
    public float Sector1Time;
    public float Sector2Time;
    public float LapsComplete;
    public float TotalLaps;
    public float LastLapTime;


    // public int TrackNumber { set; get; }
    public DateTime TimeStamp;

    public override string ToString()
    {
        return StringHelper.InstanceToStringWithFields(this);
    }

    public override bool Equals(object? obj)
    {
        if (obj == null)
        {
            return false;
        }

        var target = (DirtRawData)obj;
        return this.Time == target.Time &&
               this.LapTime == target.LapTime &&
               this.LapDistance == target.LapDistance &&
               this.CompletionRate == target.CompletionRate &&
               this.Speed == target.Speed &&
               // this.TrackNumber == target.TrackNumber &&
               this.TrackLength == target.TrackLength &&
               this.PosZ == target.PosZ;
    }

    public override int GetHashCode()
    {
        return base.GetHashCode();
    }
}
