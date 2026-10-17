using System;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.RBR;

internal static class RBRPowertrainTelemetry
{
    // The delegate must return true only for a complete successful read.
    internal static (EngineTelemetrySnapshot? Engine, TurboTelemetrySnapshot? Turbo) Read(
        Func<int, byte[], bool> read, int raceDataAddress, DateTimeOffset capturedAt)
    {
        if ((uint)raceDataAddress < 0x10000 || (uint)raceDataAddress > uint.MaxValue - 28)
            return (null, null);
        var buffer = new byte[8];
        if (!read(unchecked(raceDataAddress + 20), buffer)) return (null, null);
        var coolant = BitConverter.ToSingle(buffer, 0);
        var pressureBar = BitConverter.ToSingle(buffer, 4) / 100000f;
        return (
            float.IsFinite(coolant) && coolant >= -273.15f
                ? new EngineTelemetrySnapshot("rbr", capturedAt) { CoolantTemperatureCelsius = coolant } : null,
            float.IsFinite(pressureBar)
                ? new TurboTelemetrySnapshot("rbr", capturedAt)
                {
                    PressureBar = pressureBar,
                    // Preserve the existing Pa -> bar conversion. Atmospheric
                    // reference has not been verified; do not subtract 1 bar.
                    PressureReference = TurboPressureReference.Unknown
                } : null);
    }
}
