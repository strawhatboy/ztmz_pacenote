using NAudio.Wave;
using ZTMZ.PacenoteTool.Base;
using Xunit;

namespace ZTMZ.PacenoteTool.Portable.Tests;

public class AudioPlaybackEngineTest
{
    [Fact]
    public void PortableEngine_StartsInjectedBackendAndQueuesSound()
    {
        using var backend = new RecordingAudioOutputBackend();
        using var engine = new ZTMZAudioPlaybackEngine(backend);

        engine.PlaySound(new AutoResampledCachedSound(new float[128]));

        Assert.NotNull(backend.Source);
        Assert.Equal(1, engine.Status.QueuedSoundCount);
    }

    [Fact]
    public void PortableEngine_ReportsErrorsRaisedByInjectedBackend()
    {
        using var backend = new RecordingAudioOutputBackend
        {
            LastErrorUtc = DateTimeOffset.UtcNow,
            LastError = "PortAudio callback failed"
        };
        using var engine = new ZTMZAudioPlaybackEngine(backend);

        Assert.Equal("PortAudio callback failed", engine.Status.LastError);
        Assert.Equal(backend.LastErrorUtc, engine.Status.LastErrorUtc);
    }

    private sealed class RecordingAudioOutputBackend : IAudioOutputBackend
    {
        public string DeviceId => "test";

        public string DeviceName => "Test output";

        public int LatencyMilliseconds => 0;

        public DateTimeOffset? LastErrorUtc { get; init; }

        public string LastError { get; init; } = string.Empty;

        public ISampleProvider? Source { get; private set; }

        public void Start(ISampleProvider source)
        {
            Source = source;
        }

        public void Dispose()
        {
        }
    }
}
