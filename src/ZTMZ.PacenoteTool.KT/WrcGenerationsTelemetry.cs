using System.Buffers.Binary;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.KT;

/// <summary>
/// WRCG legacy UDP layout: 70 little-endian floats, not the EA WRC protocol.
/// Field reference: PHARTGAMES/SpaceMonkey, WRCGenAPI/WRCGenData.cs.
/// Unit conversions require live-game verification before provider release.
/// </summary>
public static class WrcGenerationsTelemetry
{
    public const int PacketSize = 280;

    public static bool TryDecode(ReadOnlySpan<byte> packet, out GameData data)
    {
        data = default;
        if (packet.Length != PacketSize) return false;
        Span<float> fields = stackalloc float[70];
        for (var i = 0; i < fields.Length; i++)
        {
            fields[i] = BinaryPrimitives.ReadSingleLittleEndian(packet.Slice(i * 4, 4));
            if (!float.IsFinite(fields[i])) return false;
        }
        var gear = fields[33];
        if (gear != MathF.Truncate(gear) || gear < -1 || gear > 20) return false;
        var speed = fields[7] * 3.6f;
        var rpm = fields[37] * 10f;
        var maxRpm = fields[63] * 10f;
        var idleRpm = fields[64] * 10f;
        if (!float.IsFinite(speed) || !float.IsFinite(rpm) || !float.IsFinite(maxRpm) || !float.IsFinite(idleRpm)) return false;
        data = new GameData
        {
            TimeStamp = DateTime.Now,
            Time = fields[0], LapTime = fields[1], LapDistance = fields[2],
            PosX = fields[4], PosY = fields[5], PosZ = fields[6], Speed = speed,
            SuspensionRearLeft = fields[17], SuspensionRearRight = fields[18],
            SuspensionFrontLeft = fields[19], SuspensionFrontRight = fields[20],
            SuspensionSpeedRearLeft = fields[21], SuspensionSpeedRearRight = fields[22],
            SuspensionSpeedFrontLeft = fields[23], SuspensionSpeedFrontRight = fields[24],
            SpeedRearLeft = fields[25], SpeedRearRight = fields[26],
            SpeedFrontLeft = fields[27], SpeedFrontRight = fields[28],
            Throttle = Math.Clamp(fields[29], 0, 1), Steering = Math.Clamp(fields[30], -1, 1),
            Brake = Math.Clamp(fields[31], 0, 1), Clutch = Math.Clamp(fields[32], 0, 1),
            Gear = gear, G_lat = fields[34], G_long = fields[35], RPM = rpm,
            MaxRPM = maxRpm, IdleRPM = idleRpm, MaxGears = fields[65],
            // The reference marks track_size as unused. Do not invent completion,
            // car/stage identity, handbrake or shift-light thresholds from it.
            HandBrakeValid = false, ShiftLightsRPMValid = false
        };
        return true;
    }
}
