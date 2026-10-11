using System.Buffers.Binary;
using System.Numerics;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.KT;

internal static class Wrc7VehicleTelemetry
{
    internal static GameData DecodeMotion(ReadOnlySpan<byte> body, ReadOnlySpan<byte> localPose, GameData data)
    {
        if (body.Length < 40 || localPose.Length < 28) return data;
        var velocity = Vector(body, 28);
        if (Finite(velocity) && velocity.LengthSquared() < 200 * 200)
            data.Speed = velocity.Length() * 3.6f;
        var rotation = new Quaternion(Single(body, 0), Single(body, 4), Single(body, 8), Single(body, 12));
        var localRotation = new Quaternion(Single(localPose, 0), Single(localPose, 4), Single(localPose, 8), Single(localPose, 12));
        var position = Vector(body, 16);
        var offset = Vector(localPose, 16);
        if (Math.Abs(rotation.LengthSquared() - 1) < .01f && Math.Abs(localRotation.LengthSquared() - 1) < .01f &&
            Finite(position) && Finite(offset))
        {
            var actorRotation = Quaternion.Normalize(rotation * Quaternion.Inverse(localRotation));
            position -= Vector3.Transform(offset, actorRotation);
            data.PosX = position.X; data.PosY = position.Y; data.PosZ = position.Z;
        }
        return data;
    }

    internal static GameData DecodeWheelSpeeds(ReadOnlySpan<byte> wheelData, ReadOnlySpan<byte> angularSpeeds, GameData data)
    {
        if (wheelData.Length < 0xc0 || angularSpeeds.Length < 16) return data;
        Span<float> speeds = stackalloc float[4];
        for (var i = 0; i < 4; i++)
        {
            var radius = Single(wheelData, i * 0x30);
            var radians = Single(angularSpeeds, i * 4);
            if (!float.IsFinite(radius) || radius is < .1f or > 1f || !float.IsFinite(radians) || Math.Abs(radians) > 2000) return data;
            speeds[i] = radians * radius * 3.6f;
        }
        data.SpeedFrontLeft = speeds[0]; data.SpeedFrontRight = speeds[1];
        data.SpeedRearLeft = speeds[2]; data.SpeedRearRight = speeds[3];
        return data;
    }

    private static float Single(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadSingleLittleEndian(bytes[offset..]);
    private static Vector3 Vector(ReadOnlySpan<byte> bytes, int offset) => new(Single(bytes, offset), Single(bytes, offset + 4), Single(bytes, offset + 8));
    private static bool Finite(Vector3 vector) => float.IsFinite(vector.X) && float.IsFinite(vector.Y) && float.IsFinite(vector.Z);

    internal static GameData DecodeProgress(ReadOnlySpan<byte> lengthBytes, ReadOnlySpan<byte> distanceBytes, GameData data)
    {
        if (lengthBytes.Length < 4 || distanceBytes.Length < 4) return data;
        var length = BinaryPrimitives.ReadSingleLittleEndian(lengthBytes);
        var distance = BinaryPrimitives.ReadSingleLittleEndian(distanceBytes);
        if (!float.IsFinite(length) || length is < 10 or > 100000 ||
            !float.IsFinite(distance) || distance < -100 || distance > length + 100) return data;
        data.TrackLength = length;
        // RaceStats +0x790 is distance remaining, decreasing towards the finish.
        data.LapDistance = Math.Clamp(length - distance, 0, length);
        data.CompletionRate = data.LapDistance / length;
        return data;
    }

    internal static GameData DecodeControls(ReadOnlySpan<byte> controls, int gearCount, GameData data)
    {
        // WRC7 calls get/setAnalogInput at 0x946640/0x973c00 on drive+0xd0.
        // 0=acceleration, 1=brake, 2=handbrake. WRC7 writes signed steering
        // into channel 4 and clears channel 3 (0x82f1b2), unlike raw pad input.
        if (controls.Length < 20) return data;
        var throttle = BinaryPrimitives.ReadSingleLittleEndian(controls);
        var brake = BinaryPrimitives.ReadSingleLittleEndian(controls[4..]);
        var handbrake = BinaryPrimitives.ReadSingleLittleEndian(controls[8..]);
        var left = BinaryPrimitives.ReadSingleLittleEndian(controls[12..]);
        var right = BinaryPrimitives.ReadSingleLittleEndian(controls[16..]);
        if (Unit(throttle)) data.Throttle = throttle;
        if (Unit(brake)) data.Brake = brake;
        if (Unit(handbrake)) { data.HandBrake = handbrake; data.HandBrakeValid = true; }
        // Channel 5 is the clutch disengagement control, including auto-clutch:
        // gear changes assert/release it at 0x95d270/0x95d300.
        if (controls.Length >= 24)
        {
            var clutch = BinaryPrimitives.ReadSingleLittleEndian(controls[20..]);
            if (Unit(clutch)) data.Clutch = clutch;
        }
        if (float.IsFinite(left) && float.IsFinite(right) && Math.Abs(left) <= 1 && Math.Abs(right) <= 1)
            // WRC7 negates the driver steering input before writing channel 4
            // (0x82f1cb). Undo that for the shared telemetry convention.
            data.Steering = Math.Clamp(left - right, -1, 1);
        // Count includes PhysX reverse and neutral entries.
        if (gearCount is >= 3 and <= 14) data.MaxGears = gearCount - 2;
        return data;
    }

    private static bool Unit(float value) => float.IsFinite(value) && value is >= 0 and <= 1;

    internal static GameData Decode(ReadOnlySpan<byte> engine, ReadOnlySpan<byte> limits, GameData data)
    {
        if (engine.Length < 0xb0 || limits.Length < 8) return data;
        var gear = BinaryPrimitives.ReadInt32LittleEndian(engine);
        var radians = BinaryPrimitives.ReadSingleLittleEndian(engine[8..]);
        var maximum = BinaryPrimitives.ReadSingleLittleEndian(limits);
        var idle = BinaryPrimitives.ReadSingleLittleEndian(limits[4..]);
        if (gear is < 0 or > 12 || !float.IsFinite(radians) || radians is < 0 or > 3000 ||
            !float.IsFinite(maximum) || maximum is < 100 or > 3000 ||
            !float.IsFinite(idle) || idle < 0 || idle > maximum) return data;
        const float radiansToRpm = 60f / (2f * MathF.PI);
        data.Gear = gear - 1; // PhysX: reverse=0, neutral=1, first=2.
        data.RPM = radians * radiansToRpm;
        data.MaxRPM = maximum * radiansToRpm;
        data.IdleRPM = idle * radiansToRpm;
        // HUD 0x1313b7 uses omegaMax - 1 rad/s as its flashing threshold.
        // This is the game's warning point, not an inferred optimal shift RPM.
        return KtShiftLights.ApplyWarning(data, (maximum - 1f) * radiansToRpm);
    }
}
