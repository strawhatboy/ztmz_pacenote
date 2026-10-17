using System;

namespace ZTMZ.PacenoteTool.Base.Game;

/// <summary>Optional induction pressure in bar. Zero does not establish absence of a turbo.
/// Pressure with an unknown reference must not be presented as gauge boost pressure.</summary>
public sealed record TurboTelemetrySnapshot(string Source, DateTimeOffset CapturedAtUtc)
{
    public float? PressureBar { get; init; }
    public string PressureReference { get; init; } = TurboPressureReference.Unknown;
}

/// <summary>Stable strings shared by CLR Lua and JSON consumers.</summary>
public static class TurboPressureReference
{
    public const string Unknown = "unknown";
    public const string Gauge = "gauge";
    public const string Absolute = "absolute";
}
