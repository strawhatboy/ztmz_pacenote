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
}

public interface IGameLivePacenoteSource
{
    event Action<LivePacenoteEvent> onLivePacenote;
}
