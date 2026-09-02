#nullable enable

using System;
using System.Collections.Generic;

namespace ZTMZ.PacenoteTool.Base;

public static class WebDashboardCanvasResolution
{
    public const int DefaultWidth = 1280;
    public const int DefaultHeight = 720;
    public const string Default = "1280x720";

    public static IReadOnlyList<string> Supported { get; } = new[]
    {
        "640x360",
        "960x540",
        Default,
        "1920x1080"
    };

    public static string Normalize(string? value)
    {
        foreach (var supported in Supported)
        {
            if (string.Equals(value, supported, StringComparison.OrdinalIgnoreCase))
            {
                return supported;
            }
        }

        return Default;
    }

    public static (int Width, int Height) Parse(string? value)
    {
        var normalized = Normalize(value);
        var separator = normalized.IndexOf('x');
        return (
            int.Parse(normalized[..separator]),
            int.Parse(normalized[(separator + 1)..]));
    }
}
