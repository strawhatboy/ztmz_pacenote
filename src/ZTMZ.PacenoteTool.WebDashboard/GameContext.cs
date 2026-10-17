using System;
using System.Collections.Generic;
using System.Threading;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.WebDashboard;

public class GameContext
{
    public string TrackName { set; get; } = string.Empty;
    public string AudioPackage { set; get; } = string.Empty;
    public string ScriptAuthor { set; get; } = string.Empty;
    public string PacenoteType { set; get; } = string.Empty;
    private readonly object _pacenoteGate = new();
    private PacenotePlaybackSnapshot[] _recentPacenotes = [];
    private const int MaximumRecentPacenotes = 32;

    // Each renderer consumes the same immutable window independently. Keeping
    // only the latest call loses earlier dispatches between dashboard frames.
    public IReadOnlyList<PacenotePlaybackSnapshot> RecentPacenotes => Volatile.Read(ref _recentPacenotes);

    public PacenotePlaybackSnapshot CurrentPacenote
    {
        get
        {
            var recent = Volatile.Read(ref _recentPacenotes);
            return recent.Length == 0 ? PacenotePlaybackSnapshot.Empty : recent[^1];
        }
        set
        {
            lock (_pacenoteGate)
            {
                if (value == null || value.Tokens.Count == 0)
                {
                    Volatile.Write(ref _recentPacenotes, []);
                    return;
                }
                var previous = _recentPacenotes;
                if (previous.Length > 0 && previous[^1].DispatchId == value.DispatchId)
                    return;
                var retained = Math.Min(previous.Length, MaximumRecentPacenotes - 1);
                var next = new PacenotePlaybackSnapshot[retained + 1];
                Array.Copy(previous, previous.Length - retained, next, 0, retained);
                next[^1] = value;
                Volatile.Write(ref _recentPacenotes, next);
            }
        }
    }
    public string CarName { set; get; } = string.Empty;
    public string CarClass { set; get; } = string.Empty;
    public bool LocalReplayValid { set; get; }
    public List<ReplayDetailsPerCheckpoint> LocalReplayDetailsPerCheckpoints { set; get; } = [];
    public List<ReplayDetailsPerTime> LocalReplayDetailsPerTimes { set; get; } = [];
    public Dictionary<float, float> LocalReplayDetailsPerTimesDict { set; get; } = [];
    public Replay LocalReplay { set; get; } = new();
    public float LocalReplayFinishTime { set; get; }
    public ReplayComparisonSnapshot LocalReplayComparison { set; get; } = ReplayComparisonSnapshot.Unavailable;
    public Func<float, float> GetDelta { set; get; } = _ => 0;
    public Func<float, float, float> GetDeltaByDistanceAndTime { set; get; } = (_, _) => 0;
    public Func<int, float> GetDeltaByCheckpoint { set; get; } = _ => 0;
    public Func<float, float> GetTimeByDistance { set; get; } = _ => 0;
    public Func<float, float> GetBestTimeByDistance { set; get; } = _ => 0;
    public Func<float, float> GetBestDistanceByTime { set; get; } = _ => 0;
    public int LastGameState { set; get; }
    public int GameState { set; get; }
    public Dictionary<string, object> GameStateChangedParameters { set; get; } = [];
}
