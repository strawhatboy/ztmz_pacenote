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
    public bool StageFinished;
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
        StageFinished = false;
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
        return (BeamNGRallySnapshot)MemberwiseClone();
    }
}
