using System;

namespace ZTMZ.PacenoteTool.Base.Game;

/// <summary>
/// Game-reported signed FFB output, normalized to [-1, 1]. This is an output
/// command, not measured wheel torque. Zero is valid; null means unavailable.
/// </summary>
public sealed record ForceFeedbackTelemetrySnapshot(string Source, DateTimeOffset CapturedAtUtc)
{
    public float OutputFraction { get; init; }

    public static ForceFeedbackTelemetrySnapshot FromNormalizedOutput(string source, float output)
        => float.IsFinite(output) && output is >= -1 and <= 1
            ? new(source, DateTimeOffset.UtcNow) { OutputFraction = output }
            : null;
}
