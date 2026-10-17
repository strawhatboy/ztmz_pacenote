using System;

namespace ZTMZ.PacenoteTool.Base.Game;

/// <summary>
/// Optional hybrid operation telemetry. A null snapshot means unavailable or a
/// non-hybrid car; null individual readings mean the source cannot report them.
/// Battery charge and boost-cycle progress are different quantities.
/// </summary>
public sealed record HybridTelemetrySnapshot(string Source, DateTimeOffset CapturedAtUtc)
{
    /// <summary>Stored energy divided by nominal battery capacity, from 0 to 1.</summary>
    public float? BatteryChargeFraction { get; init; }
    public float? BatteryEnergyKWh { get; init; }
    public float? BatteryCapacityKWh { get; init; }
    /// <summary>Recharge progress when inactive, remaining boost budget when active (0–1).</summary>
    public float? BoostProgressFraction { get; init; }
    public bool? BoostReady { get; init; }
    public bool? BoostActive { get; init; }
    /// <summary>The game's regeneration phase, not a measurement of electrical power.</summary>
    public bool? RegenerationActive { get; init; }
    /// <summary>Selected boost map, numbered from 1 as displayed in the game.</summary>
    public int? BoostMap { get; init; }
}
