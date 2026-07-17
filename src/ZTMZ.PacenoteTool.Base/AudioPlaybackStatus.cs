using System;

namespace ZTMZ.PacenoteTool.Base;

public sealed record AudioPlaybackStatus(
    bool IsActive,
    int DeviceId,
    int DesiredLatencyMilliseconds,
    long QueuedSoundCount,
    DateTimeOffset? LastSoundQueuedUtc,
    DateTimeOffset? LastErrorUtc,
    string LastError)
{
    public static AudioPlaybackStatus NotAvailable { get; } = new(
        false,
        -1,
        0,
        0,
        null,
        null,
        string.Empty);
}
