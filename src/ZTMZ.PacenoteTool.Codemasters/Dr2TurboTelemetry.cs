using System;
using System.IO;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.Codemasters;

/// <summary>Optional turbo reading for the executable verified by Dr2VehicleMemoryReader.</summary>
public static class Dr2TurboTelemetry
{
    public static TurboTelemetrySnapshot? Read(Func<ulong, int, byte[]> read, ulong physics)
    {
        byte[] Bytes(ulong address, int count)
        {
            var bytes = read(address, count);
            if (bytes.Length != count) throw new IOException("Incomplete DR2 turbo reading.");
            return bytes;
        }

        if (physics < 0x10000 || physics > 0x7fffffffffff - 0x253c) return null;
        var parameters = BitConverter.ToUInt64(Bytes(physics + 0x1378, 8));
        if (parameters < 0x10000 || parameters > 0x7fffffffffff - 0x348) return null;

        // Native turbo-presence getter 0x7336c0 decodes this parameter and tests > 0.
        var encoded = BitConverter.ToUInt32(Bytes(parameters + 0x344, 4));
        var maximum = BitConverter.Int32BitsToSingle(unchecked((int)(encoded * 0x16889185u)));
        if (!float.IsFinite(maximum) || maximum <= 0) return null;

        // Native HUD getter 0x7313f0. Update 0x74e750 smooths absolute pressure
        // minus 1.0 into this field: already gauge pressure, not the audio ratio.
        var pressure = BitConverter.ToSingle(Bytes(physics + 0x2538, 4));
        if (!float.IsFinite(pressure) || pressure <= -1 || pressure > Math.Max(0, maximum - 1) + 0.01f)
            return null; // -1 is the native initialization/reset sentinel.
        if (BitConverter.ToUInt64(Bytes(physics + 0x1378, 8)) != parameters) return null;

        return new TurboTelemetrySnapshot("DR2", DateTimeOffset.UtcNow)
        {
            PressureBar = pressure,
            PressureReference = TurboPressureReference.Gauge
        };
    }
}
