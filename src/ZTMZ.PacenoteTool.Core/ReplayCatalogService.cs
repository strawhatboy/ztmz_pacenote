using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.Core;

/// <summary>
/// Exposes replay catalog operations without coupling platform hosts to the
/// process-wide replay manager singleton.
/// </summary>
public sealed class ReplayCatalogService
{
    private readonly ReplayManager _replayManager;

    public ReplayCatalogService()
        : this(ReplayManager.Instance)
    {
    }

    internal ReplayCatalogService(ReplayManager replayManager)
    {
        _replayManager = replayManager;
    }

    public Task<List<Replay>> GetReplaysAsync(IGame game)
    {
        ArgumentNullException.ThrowIfNull(game);
        return _replayManager.getReplays(game);
    }

    public Task<Replay> GetReplayAsync(int replayId)
    {
        return _replayManager.getReplay(replayId);
    }

    public Task<List<ReplayDetailsPerTime>> GetDetailsPerTimeAsync(int replayId)
    {
        return _replayManager.getReplayDetailsPerTime(replayId);
    }

    public Task<List<ReplayDetailsPerCheckpoint>> GetDetailsPerCheckpointAsync(int replayId)
    {
        return _replayManager.getReplayDetailsPerCheckpoint(replayId);
    }

    public Task<Replay> GetBestReplayAsync(
        IGame game,
        string track,
        string carClass,
        string carName)
    {
        ArgumentNullException.ThrowIfNull(game);
        return _replayManager.GetBestReplay(game, track, carClass, carName);
    }

    public Task SetLockedAsync(int replayId, bool locked)
    {
        return _replayManager.SetReplayLockedAsync(replayId, locked);
    }

    public async Task ExportAsync(IGame game, int replayId, string destinationDirectory)
    {
        var replay = await RequireReplayAsync(replayId);
        await _replayManager.ExportReplay(game, replay, destinationDirectory);
    }

    public async Task ExportAudioAsync(IGame game, int replayId, string destinationDirectory)
    {
        var replay = await RequireReplayAsync(replayId);
        await _replayManager.ExportReplayWithAudio(game, replay, destinationDirectory);
    }

    public Task FlushPendingSavesAsync()
    {
        return _replayManager.FlushPendingSavesAsync();
    }

    private async Task<Replay> RequireReplayAsync(int replayId)
    {
        return await GetReplayAsync(replayId)
            ?? throw new InvalidOperationException($"Replay {replayId} no longer exists.");
    }
}
