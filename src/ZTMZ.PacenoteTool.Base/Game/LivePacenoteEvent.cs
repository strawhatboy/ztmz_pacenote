using System;

namespace ZTMZ.PacenoteTool.Base.Game;

/// <summary>
/// A pacenote call emitted by a game at the point its built-in co-driver calls it.
/// </summary>
public sealed class LivePacenoteEvent
{
    public string Id { get; init; } = string.Empty;
    public string StageId { get; init; } = string.Empty;
    public PacenoteRecord Record { get; init; } = new();
    public string SourceResourceName { get; init; } = string.Empty;
    public string SourceToken { get; init; } = string.Empty;
    public string SourcePlacement { get; init; } = string.Empty;
    public string SourceVariant { get; init; } = string.Empty;
    public string SystemSoundToken { get; init; } = string.Empty;
    public bool SystemSoundSequential { get; init; }
}

public interface IGameLivePacenoteSource
{
    event Action<LivePacenoteEvent> onLivePacenote;
}
