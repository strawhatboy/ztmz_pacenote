using System;
using System.Collections.Generic;
using System.Linq;

namespace ZTMZ.PacenoteTool.Base;

/// <summary>
/// Immutable description of the most recently dispatched pacenote call.
/// </summary>
public sealed record PacenotePlaybackSnapshot(
    float? Distance,
    IReadOnlyList<PacenotePlaybackToken> Tokens,
    DateTimeOffset DispatchedAtUtc,
    string Source,
    string ScriptPath,
    string StageId)
{
    public string GroupId { get; init; } = string.Empty;

    public static readonly PacenotePlaybackSnapshot Empty = new(
        null,
        Array.Empty<PacenotePlaybackToken>(),
        DateTimeOffset.MinValue,
        string.Empty,
        string.Empty,
        string.Empty);

    public static PacenotePlaybackSnapshot FromRecord(
        PacenoteRecord record,
        string source,
        string scriptPath = "",
        string stageId = "")
    {
        return new PacenotePlaybackSnapshot(
            record.Distance,
            record.Pacenotes
                .Select(note => new PacenotePlaybackToken(
                    note.Note,
                    note.Modifiers.ToArray()))
                .ToArray(),
            DateTimeOffset.UtcNow,
            source,
            scriptPath ?? string.Empty,
            stageId ?? string.Empty);
    }
}

public sealed record PacenotePlaybackToken(
    string Token,
    IReadOnlyList<string> Modifiers);
