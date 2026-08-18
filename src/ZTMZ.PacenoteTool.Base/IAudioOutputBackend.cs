using System;
using NAudio.Wave;

namespace ZTMZ.PacenoteTool.Base;

public interface IAudioOutputBackend : IDisposable
{
    string DeviceId { get; }

    string DeviceName { get; }

    int LatencyMilliseconds { get; }

    DateTimeOffset? LastErrorUtc { get; }

    string LastError { get; }

    void Start(ISampleProvider source);
}
