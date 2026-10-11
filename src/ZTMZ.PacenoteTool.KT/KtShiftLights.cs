using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.KT;

internal static class KtShiftLights
{
    // These HUDs expose one warning point, not ACR's progressive LED range.
    // Equal endpoints deliberately mean off below the threshold, flashing above.
    internal static GameData ApplyWarning(GameData data, float warningRpm)
    {
        data.ShiftLightsRPMValid = false;
        data.ShiftLightsRPMStart = data.ShiftLightsRPMEnd = data.ShiftLightsFraction = 0;
        if (!float.IsFinite(warningRpm) || warningRpm is < 100 or > 30000 ||
            !float.IsFinite(data.RPM) || data.RPM < 0) return data;
        data.ShiftLightsRPMStart = data.ShiftLightsRPMEnd = warningRpm;
        data.ShiftLightsFraction = data.RPM >= warningRpm ? 1 : 0;
        data.ShiftLightsRPMValid = true;
        return data;
    }
}
