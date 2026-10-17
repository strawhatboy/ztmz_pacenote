using System;

namespace ZTMZ.PacenoteTool.Base.Game;

/// <summary>Optional engine readings. Null fields mean unavailable, not zero or healthy.
/// Warnings come from the source; they are never inferred from generic temperature thresholds.</summary>
public sealed record EngineTelemetrySnapshot(string Source, DateTimeOffset CapturedAtUtc)
{
    public float? CoolantTemperatureCelsius { get; init; }
    public float? OilTemperatureCelsius { get; init; }
    public bool? OilPressureWarning { get; init; }
    public bool? CoolantTemperatureWarning { get; init; }
    public bool? EngineWarning { get; init; }
}
