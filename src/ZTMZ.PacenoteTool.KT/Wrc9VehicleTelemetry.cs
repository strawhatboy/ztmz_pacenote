using System.Buffers.Binary;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.KT;

/// <summary>Telemetry channels verified against the supported WRC9 build and live captures.</summary>
internal static class Wrc9VehicleTelemetry
{
    internal static float Single(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadSingleLittleEndian(bytes[offset..]);
    private static bool Range(float value, float min, float max) => float.IsFinite(value) && value >= min && value <= max;

    internal static GameData DecodeDrive(ReadOnlySpan<byte> drive, GameData data)
    {
        if (drive.Length < 0x538) return data;
        // Debug HUD 0xf9d835 labels these six analog channels. Live left/right
        // holds produce +1/-1 on channel 4; ZTMZ uses negative for left.
        var controls = drive[0xe0..];
        var throttle = Single(controls, 0); var brake = Single(controls, 4);
        var handbrake = Single(controls, 8); var steering = Single(controls, 16);
        var clutch = Single(controls, 20);
        if (Range(throttle, 0, 1)) data.Throttle = throttle;
        if (Range(brake, 0, 1)) data.Brake = brake;
        if (Range(handbrake, 0, 1)) { data.HandBrake = handbrake; data.HandBrakeValid = true; }
        if (Range(steering, -1, 1)) data.Steering = -steering;
        if (Range(clutch, 0, 1)) data.Clutch = clutch; // Includes the game's automatic clutch.
        var gears = BinaryPrimitives.ReadInt32LittleEndian(drive[0x534..]);
        var gear = BinaryPrimitives.ReadInt32LittleEndian(drive[0x128..]);
        // Gear table at +0x4b0 includes reverse and neutral, followed by forward ratios.
        if (gears is >= 3 and <= 14)
        {
            data.MaxGears = gears - 2;
            if (gear >= 0 && gear < gears) data.Gear = gear - 1;
        }
        var omega = Single(drive, 0x134); var max = Single(drive, 0x448); var idle = Single(drive, 0x44c);
        const float rpm = 60f / (2 * MathF.PI);
        if (Range(omega, 0, 3000)) data.RPM = omega * rpm;
        if (Range(max, 100, 3000)) data.MaxRPM = max * rpm;
        if (Range(idle, 0, max)) data.IdleRPM = idle * rpm;
        // 0x102a7b5 differentiates world velocity; 0x102a8f8 rotates acceleration
        // into the vehicle frame. The vehicle moves along local -Y, with Z up.
        var lateral = Single(drive, 0x29c); var longitudinal = -Single(drive, 0x2a0);
        if (Range(lateral, -2000, 2000)) data.G_lat = lateral / 9.80665f;
        if (Range(longitudinal, -2000, 2000)) data.G_long = longitudinal / 9.80665f;
        return data;
    }

    internal static GameData DecodeWheels(ReadOnlySpan<byte> vehicle, ReadOnlySpan<byte> drive,
        ReadOnlySpan<byte> queries, ReadOnlySpan<byte> configuration, ReadOnlySpan<byte> dynamics, GameData data)
    {
        if (vehicle.Length < 0x1208 || drive.Length < 0x210 || queries.Length < 0x280 ||
            configuration.Length < 0xc0 || dynamics.Length < 0x20) return data;
        data = Wrc7VehicleTelemetry.DecodeWheelSpeeds(configuration, dynamics[0x10..], data);
        Span<float> travel = stackalloc float[4]; Span<float> speed = stackalloc float[4]; Span<float> temperature = stackalloc float[4];
        for (var i = 0; i < 4; i++)
        {
            // Same calculations as HUD 0xf9fcab/0xf9fdd2/0xf9fe77. Query stride
            // 0xa0, temperature stride 0x24; order FL, FR, RL, RR.
            var jounce = Single(queries, i * 0xa0 + 0x5c) - Single(drive, 0x1e0 + i * 4);
            var rate = Single(drive, 0x200 + i * 4);
            var temperatureBase = 0x118c + i * 0x24;
            if (vehicle[temperatureBase + 12] > 1) return data;
            var temp = Single(vehicle, temperatureBase - (vehicle[temperatureBase + 12] == 1 ? 20 : 0));
            if (!Range(jounce, -2, 2) || !Range(rate, -200, 200) || !Range(temp, -100, 2000)) return data;
            travel[i] = jounce; speed[i] = rate; temperature[i] = temp;
        }
        data.SuspensionFrontLeft = travel[0]; data.SuspensionFrontRight = travel[1];
        data.SuspensionRearLeft = travel[2]; data.SuspensionRearRight = travel[3];
        data.SuspensionSpeedFrontLeft = speed[0]; data.SuspensionSpeedFrontRight = speed[1];
        data.SuspensionSpeedRearLeft = speed[2]; data.SuspensionSpeedRearRight = speed[3];
        data.BrakeTempFrontLeft = temperature[0]; data.BrakeTempFrontRight = temperature[1];
        data.BrakeTempRearLeft = temperature[2]; data.BrakeTempRearRight = temperature[3];
        return data;
    }

    internal static GameData DecodeProgress(float length, float distance, GameData data)
    {
        if (!Range(length, 10, 100000) || !Range(distance, -100, length + 100)) return data;
        data.TrackLength = length; data.LapDistance = Math.Clamp(distance, 0, length);
        data.CompletionRate = data.LapDistance / length;
        return data;
    }
}
