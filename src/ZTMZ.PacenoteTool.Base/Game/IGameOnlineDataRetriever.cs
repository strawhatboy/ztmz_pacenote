// Retrieve online data for the game
// * Leaderboard
// * RivalStats

using System.Collections.Generic;
using System.Threading.Tasks;

namespace ZTMZ.PacenoteTool.Base.Game;

public class LeaderboardItem {
    public string driverName { get; set; }
    public string carName { get; set; }
    public string trackName { get; set; }
    public string carClass { get; set; }
    public float time { get; set; } // in seconds, convert from string
    public int position { get; set; }
}

public interface IGameOnlineDataRetriever
{
    Task<List<LeaderboardItem>> GetLeaderboardAsync();
    Task<List<ReplayDetailsPerTime>> GetRivalStatsAsync(string driverName);
}
