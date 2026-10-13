using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.BeamNG;

/// <summary>
/// Public fields are deliberate: the existing debug dashboard reflects game-specific fields.
/// </summary>
public sealed class BeamNGRallySnapshot
{
    public int ProtocolVersion;
    public string BridgeStatus = string.Empty;
    public string BridgeError = string.Empty;
    public string StageId = string.Empty;
    public string StageName = string.Empty;
    public bool StageActive;
    public bool StageCountingDown;
    public int StageCountdownNumber;
    public bool StageFinished;
    public string StageResult = string.Empty;
    public float StageTimeSeconds;
    public float DistanceMeters;
    public float TotalDistanceMeters;
    public float Completion;
    public int ExportedPacenoteCount;
    public string CurrentPacenoteId = string.Empty;
    public string CurrentPacenoteName = string.Empty;
    public string CurrentPacenoteText = string.Empty;
    public string CurrentPacenoteVisual = string.Empty;
    public float CurrentPacenoteDistanceBeforeMeters;
    public float CurrentPacenoteDistanceAfterMeters;
    public float CurrentPacenoteDistanceToMeters;
    public string VehicleName = string.Empty;
    public string VehicleClass = string.Empty;
    public int VehicleId;
    public float MaxRPM;
    public float IdleRPM;
    public bool HasDetailedVehicleTelemetry;
    public float Steering;
    public float HandBrake;
    public float MaxGears;
    public float GLong;
    public float GLat;
    public BeamNGRallyDamageSummary Damage = new();
    public CarDamageSnapshot CarDamage = CarDamageSnapshot.Unavailable("beamng");
    public float FuelFraction;
    public float EngineTemperatureCelsius;
    public float OilTemperatureCelsius;
    public float TurboBar;

    public void ClearStageData()
    {
        StageId = string.Empty;
        StageName = string.Empty;
        StageActive = false;
        StageCountingDown = false;
        StageCountdownNumber = 0;
        StageFinished = false;
        StageResult = string.Empty;
        StageTimeSeconds = 0;
        DistanceMeters = 0;
        TotalDistanceMeters = 0;
        Completion = 0;
        ExportedPacenoteCount = 0;
        CurrentPacenoteId = string.Empty;
        CurrentPacenoteName = string.Empty;
        CurrentPacenoteText = string.Empty;
        CurrentPacenoteVisual = string.Empty;
        CurrentPacenoteDistanceBeforeMeters = 0;
        CurrentPacenoteDistanceAfterMeters = 0;
        CurrentPacenoteDistanceToMeters = 0;
    }

    public BeamNGRallySnapshot Clone()
    {
        var clone = (BeamNGRallySnapshot)MemberwiseClone();
        clone.Damage = Damage.Clone();
        return clone;
    }
}

/// <summary>
/// Compact, vehicle-independent damage state for dashboards and diagnostics.
/// </summary>
public sealed class BeamNGRallyDamageSummary
{
    public int VehicleId;
    public int ActiveDamageCount;
    public int DamagedWheelCount;
    public float BodyDamageMaximum;
    public float BodyDamageFrontLeft;
    public float BodyDamageFrontRight;
    public float BodyDamageRearLeft;
    public float BodyDamageRearRight;
    public bool HasEngineDamage;
    public bool HasRadiatorDamage;
    public bool HasPowertrainDamage;
    public bool HasGearboxDamage;
    public bool HasEnergyStorageDamage;
    public bool HasDrivingDynamicsDamage;
    public string ActiveDamage = string.Empty;
    public string LastChanges = string.Empty;

    public BeamNGRallyDamageSummary Clone()
    {
        return (BeamNGRallyDamageSummary)MemberwiseClone();
    }
}
