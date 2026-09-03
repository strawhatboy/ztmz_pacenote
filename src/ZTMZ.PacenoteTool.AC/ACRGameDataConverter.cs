using System;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.AC;

internal static class ACRGameDataConverter
{
    public static GameData Convert(ACRGameRawData rawData)
    {
        return new GameData
        {
            Time = rawData.GraphicsData.CurrentTimeMillis / 1000.0f,
            LapTime = rawData.GraphicsData.CurrentTimeMillis / 1000.0f,
            LapDistance = rawData.GraphicsData.TotalDistanceTraveled,
            CompletionRate = rawData.StaticInfo.TrackLengthMeters > 0
                ? Math.Clamp(rawData.GraphicsData.TotalDistanceTraveled / rawData.StaticInfo.TrackLengthMeters, 0, 1)
                : 0,
            TrackLength = rawData.StaticInfo.TrackLengthMeters,
            Speed = rawData.PhysicsData.SpeedKilometersPerHour,
            SpeedFrontLeft = rawData.PhysicsData.WheelAngularVelocity?[0] ?? 0,
            SpeedFrontRight = rawData.PhysicsData.WheelAngularVelocity?[1] ?? 0,
            SpeedRearLeft = rawData.PhysicsData.WheelAngularVelocity?[2] ?? 0,
            SpeedRearRight = rawData.PhysicsData.WheelAngularVelocity?[3] ?? 0,
            Clutch = 1 - rawData.PhysicsData.ClutchPosition,
            Brake = rawData.PhysicsData.BrakeInput,
            Throttle = rawData.PhysicsData.ThrottleInput,
            HandBrake = 0,
            HandBrakeValid = false,
            Steering = rawData.PhysicsData.SteeringAngle,
            Gear = rawData.PhysicsData.CurrentGear - 1,
            MaxGears = 6,
            RPM = rawData.PhysicsData.EngineRPM,
            MaxRPM = 8000,
            IdleRPM = 1000,
            ShiftLightsFraction = 0,
            ShiftLightsRPMStart = 0,
            ShiftLightsRPMEnd = 0,
            ShiftLightsRPMValid = false,
            G_long = rawData.PhysicsData.AccelerationG?[2] ?? 0,
            G_lat = rawData.PhysicsData.AccelerationG?[0] ?? 0,
            BrakeTempFrontLeft = rawData.PhysicsData.BrakeTemperatures?[0] ?? 0,
            BrakeTempFrontRight = rawData.PhysicsData.BrakeTemperatures?[1] ?? 0,
            BrakeTempRearLeft = rawData.PhysicsData.BrakeTemperatures?[2] ?? 0,
            BrakeTempRearRight = rawData.PhysicsData.BrakeTemperatures?[3] ?? 0,
            SuspensionFrontLeft = rawData.PhysicsData.SuspensionTravelDistance?[0] ?? 0,
            SuspensionFrontRight = rawData.PhysicsData.SuspensionTravelDistance?[1] ?? 0,
            SuspensionRearLeft = rawData.PhysicsData.SuspensionTravelDistance?[2] ?? 0,
            SuspensionRearRight = rawData.PhysicsData.SuspensionTravelDistance?[3] ?? 0
        };
    }
}
