using System;
using System.Collections.Generic;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.WebDashboard;

public class GameContext
{
    public string TrackName { set; get; } = string.Empty;
    public string AudioPackage { set; get; } = string.Empty;
    public string ScriptAuthor { set; get; } = string.Empty;
    public string PacenoteType { set; get; } = string.Empty;
    public PacenotePlaybackSnapshot CurrentPacenote { set; get; } = PacenotePlaybackSnapshot.Empty;
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
