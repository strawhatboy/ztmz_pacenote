
using System;
using System.Diagnostics.CodeAnalysis;
using System.Text;

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
    public float HandBrake;
    public bool HandBrakeValid;

    public float Steering;
    public float Gear;
    public float MaxGears;
    public float RPM;
    public float MaxRPM;
    public float IdleRPM;
    public float ShiftLightsFraction;
    public float ShiftLightsRPMStart;
    public float ShiftLightsRPMEnd;
    public bool ShiftLightsRPMValid;
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

    public object GameSpecificData;

    public override bool Equals([NotNullWhen(true)] object obj)
    {
        return base.Equals(obj);
    }

    public override int GetHashCode()
    {
        return base.GetHashCode();
    }

    public override string ToString()
    {
        return toString(this);
    }

    private string toString(object o)
    {
        // loop through every field on the object and print it out
        var fields = o.GetType().GetFields();
        var stringBuilder = new StringBuilder();
        foreach (var field in fields)
        {
            stringBuilder.AppendLine($"{field.Name}: {field.GetValue(o)}");
        }
        
        return stringBuilder.ToString();
    }

    public string GameSpecificDataToString()
    {
        if (GameSpecificData == null)
        {
            return "No Game Specific Data";
        }
        return toString(GameSpecificData);
    }
}
