using System;
using System.Globalization;

namespace ZTMZ.PacenoteTool.Base;

/// <summary>Playback gain relative to the source audio, not an absolute sound-pressure level.</summary>
public static class PlaybackGain
{
    public const double MinimumDb = -60;
    public const double MaximumDb = 24;

    public static double NormalizeDb(double decibels) =>
        double.IsFinite(decibels) ? Math.Clamp(decibels, MinimumDb, MaximumDb) : 0;

    public static float ToLinear(double decibels) => (float)Math.Pow(10, NormalizeDb(decibels) / 20);

    // The old 0..100 UI stored -1000..1000 and passed an integer to the audio engine.
    public static float LegacyToLinear(double amplification)
    {
        if (!double.IsFinite(amplification)) return 1;
        var value = (float)Math.Truncate(amplification);
        return Math.Max(0, value > 0 ? (1000 + value * 10) / 1000 : (1000 + value) / 1000);
    }

    public static double FromLegacy(double amplification)
    {
        var gain = LegacyToLinear(amplification);
        // Silence migrates to the separate mute setting, with unity gain on unmute.
        return gain == 0 ? 0 : NormalizeDb(20 * Math.Log10(gain));
    }

    public static string Format(double decibels) =>
        NormalizeDb(decibels).ToString("+0.0;-0.0;0.0", CultureInfo.CurrentCulture) + " dB";
}
