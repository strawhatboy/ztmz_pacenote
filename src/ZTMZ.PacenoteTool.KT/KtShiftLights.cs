using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.KT;

internal static class KtShiftLights
{
    // KT exposes one native warning point. Use the same 1000-RPM dashboard
    // lead-in as RBR/DR2 so getRPMLevel can render blank/green/yellow/red/flash.
    // The lead-in is ZTMZ's display convention, not native LED telemetry.
    internal static GameData ApplyWarning(GameData data, float warningRpm)
    {
        data.ShiftLightsRPMValid = false;
        data.ShiftLightsRPMStart = data.ShiftLightsRPMEnd = data.ShiftLightsFraction = 0;
        if (!float.IsFinite(warningRpm) || warningRpm is < 100 or > 30000 ||
            !float.IsFinite(data.RPM) || data.RPM < 0) return data;
        data.ShiftLightsRPMEnd = warningRpm;
        data.ShiftLightsRPMStart = Math.Max(0, warningRpm - 1000);
        data.ShiftLightsFraction = Math.Clamp((data.RPM - data.ShiftLightsRPMStart) /
            (data.ShiftLightsRPMEnd - data.ShiftLightsRPMStart), 0, 1);
        data.ShiftLightsRPMValid = true;
        return data;
    }
}
