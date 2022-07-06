
using System;
using System.Diagnostics.CodeAnalysis;

namespace ZTMZ.PacenoteTool.Base.Game;

public struct GameData
{
    
    public float Time;
    public float LapTime;
    public float LapDistance;
    public float CompletionRate; // 0-1, 0.5 means finished 50%
    public float Speed;
    public float TrackLength;

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
    public float CarPos;
    public float PosX;
    public float PosY;
    public float PosZ;
    // public int TrackNumber { set; get; }
    public DateTime TimeStamp;

    public override bool Equals([NotNullWhen(true)] object obj)
    {
        return base.Equals(obj);
    }

    public override int GetHashCode()
    {
        return base.GetHashCode();
    }
}
