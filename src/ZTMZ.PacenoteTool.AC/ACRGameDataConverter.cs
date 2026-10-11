using System;
using System.Globalization;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.AC;

internal static class ACRGameDataConverter
{
    public static GameData Convert(ACRGameRawData rawData, ACRVehicleTelemetry? vehicle = null)
    {
        var time = ReadLapTime(rawData.GraphicsData);
        var length = NonNegative(rawData.StaticInfo.TrackLengthMeters);
        var distance = NonNegative(rawData.GraphicsData.TotalDistanceTraveled);
        var position = PlayerPosition(rawData.GraphicsData);
        return new GameData
        {
            TimeStamp = DateTime.Now,
            CarDamage = vehicle?.Damage ?? CarDamageSnapshot.Unavailable("ACR"),
            Time = time,
            LapTime = time,
            LapDistance = distance,
            CompletionRate = length > 0
                ? Math.Clamp(distance / length, 0, 1)
                : 0,
            TrackLength = length,
            PosX = Finite(position.XCoord), PosY = Finite(position.YCoord), PosZ = Finite(position.ZCoord),
            CarPos = rawData.GraphicsData.CurrentPosition,
            Speed = NonNegative(rawData.PhysicsData.SpeedKilometersPerHour),
            SpeedFrontLeft = WheelSpeed(rawData, vehicle, 0),
            SpeedFrontRight = WheelSpeed(rawData, vehicle, 1),
            SpeedRearLeft = WheelSpeed(rawData, vehicle, 2),
            SpeedRearRight = WheelSpeed(rawData, vehicle, 3),
            Clutch = Pedal(1 - rawData.PhysicsData.ClutchPosition),
            Brake = Pedal(rawData.PhysicsData.BrakeInput),
            Throttle = Pedal(rawData.PhysicsData.ThrottleInput),
            HandBrake = vehicle is null ? 0 : Pedal(vehicle.HandBrake),
            HandBrakeValid = vehicle is not null,
            Steering = Math.Clamp(Finite(rawData.PhysicsData.SteeringAngle), -1, 1),
            Gear = rawData.PhysicsData.CurrentGear - 1,
            MaxGears = vehicle?.MaxGears ?? 0,
            RPM = rawData.PhysicsData.EngineRPM,
            MaxRPM = rawData.PhysicsData.MaximumRPM > 0 ? rawData.PhysicsData.MaximumRPM : Math.Max(0, rawData.StaticInfo.EngineRPMMax),
            IdleRPM = vehicle?.IdleRPM ?? 0,
            ShiftLightsFraction = vehicle is { ShiftStart: > 0 } && vehicle.ShiftEnd > vehicle.ShiftStart
                ? Pedal((rawData.PhysicsData.EngineRPM - vehicle.ShiftStart) / (vehicle.ShiftEnd - vehicle.ShiftStart)) : 0,
            ShiftLightsRPMStart = vehicle?.ShiftStart ?? 0,
            ShiftLightsRPMEnd = vehicle?.ShiftEnd ?? 0,
            ShiftLightsRPMValid = vehicle is { ShiftStart: > 0 } && vehicle.ShiftEnd > vehicle.ShiftStart,
            G_long = Element(rawData.PhysicsData.AccelerationG, 2),
            G_lat = Element(rawData.PhysicsData.AccelerationG, 0),
            BrakeTempFrontLeft = Celsius(Element(rawData.PhysicsData.BrakeTemperatures, 0)),
            BrakeTempFrontRight = Celsius(Element(rawData.PhysicsData.BrakeTemperatures, 1)),
            BrakeTempRearLeft = Celsius(Element(rawData.PhysicsData.BrakeTemperatures, 2)),
            BrakeTempRearRight = Celsius(Element(rawData.PhysicsData.BrakeTemperatures, 3)),
            SuspensionFrontLeft = Element(rawData.PhysicsData.SuspensionTravelDistance, 0),
            SuspensionFrontRight = Element(rawData.PhysicsData.SuspensionTravelDistance, 1),
            SuspensionRearLeft = Element(rawData.PhysicsData.SuspensionTravelDistance, 2),
            SuspensionRearRight = Element(rawData.PhysicsData.SuspensionTravelDistance, 3)
        };
    }

    internal static float ReadLapTime(ACRGraphicsData graphics)
    {
        if (graphics.CurrentTimeMillis > 0) return graphics.CurrentTimeMillis / 1000f;
        // ACR can populate the formatted time while leaving iCurrentTime zero.
        var parts = graphics.CurrentLapTime?.Trim().Split(':');
        if (parts is null || parts.Length is < 2 or > 3) return 0;
        double seconds = 0;
        for (var i = 0; i < parts.Length; i++)
        {
            if (!double.TryParse(parts[i], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) ||
                !double.IsFinite(value) || value < 0 || (i > 0 && value >= 60) ||
                (i < parts.Length - 1 && value != Math.Truncate(value))) return 0;
            seconds = seconds * 60 + value;
        }
        return seconds < 86400 ? (float)seconds : 0;
    }

    private static ACRVector3 PlayerPosition(ACRGraphicsData graphics)
    {
        var positions = graphics.OpponentCarPositions;
        if (positions is null || positions.Length == 0) return default;
        // ACR single-player writes slot 0 but leaves activeCars and carID zero.
        if (graphics.ActiveCarsCount <= 1) return positions[0];
        var ids = graphics.OpponentCarIdentifiers;
        for (var i = 0; i < Math.Min(graphics.ActiveCarsCount, positions.Length); i++)
            if (ids is not null && i < ids.Length && ids[i] == graphics.PlayerCarIdentifier) return positions[i];
        return default;
    }

    private static float WheelSpeed(ACRGameRawData raw, ACRVehicleTelemetry? vehicle, int wheel)
    {
        var angular = raw.PhysicsData.WheelAngularVelocity;
        var radii = raw.StaticInfo.TyreRadiusValues;
        if (Element(radii, wheel) <= 0) radii = vehicle?.WheelRadii;
        if (angular is null || radii is null || wheel >= angular.Length || wheel >= radii.Length ||
            !float.IsFinite(radii[wheel]) || radii[wheel] <= 0) return 0;
        return Finite(angular[wheel] * radii[wheel] * 3.6f);
    }

    private static float Finite(float value) => float.IsFinite(value) ? value : 0;
    private static float NonNegative(float value) => Math.Max(0, Finite(value));
    private static float Pedal(float value) => Math.Clamp(Finite(value), 0, 1);
    private static float Element(float[]? values, int index) => values is not null && index < values.Length ? Finite(values[index]) : 0;
    // ACR's physics writer copies native Kelvin temperatures without conversion.
    private static float Celsius(float kelvin) => kelvin > 0 ? kelvin - 273.15f : 0;
}
