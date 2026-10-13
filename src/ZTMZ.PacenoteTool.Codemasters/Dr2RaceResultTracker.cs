using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.Codemasters;

/// <summary>Only a native result following observed racing in the same player
/// record can end the current attempt. No inference from damage or UDP silence.</summary>
public sealed class Dr2RaceResultTracker
{
    private ulong record;
    private bool observedRacing;
    public bool HasAbnormalResult { get; private set; }

    public GameStateRaceEnd? Observe(Dr2RaceStateSnapshot? snapshot, bool stageStarted)
    {
        if (snapshot is not { } current) return null;
        if (record != current.RecordAddress)
        {
            Reset();
            record = current.RecordAddress;
        }
        if (current.State == Dr2RaceState.Racing)
        {
            observedRacing |= stageStarted;
            HasAbnormalResult = false;
            return null;
        }
        if (HasAbnormalResult || !observedRacing) return null;
        var result = current.State switch
        {
            Dr2RaceState.TerminalDamage => (GameStateRaceEnd?)GameStateRaceEnd.Crashed,
            Dr2RaceState.Retired => GameStateRaceEnd.Retired,
            Dr2RaceState.Disqualified => GameStateRaceEnd.Disqualified,
            Dr2RaceState.TimedOut => GameStateRaceEnd.TimeOut,
            _ => null
        };
        // None, finished and other unhandled states cannot leave an old racing
        // observation armed for a later menu/result screen.
        observedRacing = false;
        if (result.HasValue) HasAbnormalResult = true;
        return result;
    }

    public void Reset()
    {
        record = 0;
        observedRacing = false;
        HasAbnormalResult = false;
    }
}
