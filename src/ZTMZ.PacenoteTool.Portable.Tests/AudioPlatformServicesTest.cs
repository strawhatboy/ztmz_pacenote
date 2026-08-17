using ZTMZ.PacenoteTool.Base;
using Xunit;

namespace ZTMZ.PacenoteTool.Portable.Tests;

public class AudioPlatformServicesTest
{
    [Fact]
    public void EmptyDeviceEnumerator_ReturnsNoDevices()
    {
        var devices = EmptyAudioOutputDeviceEnumerator.Instance
            .EnumerateActiveRenderDevices();

        Assert.Empty(devices);
    }

    [Fact]
    public void UnavailablePlaybackFactory_ReportsMissingBackend()
    {
        var options = new AudioPlaybackEngineOptions(
            string.Empty,
            0,
            true,
            50);

        var exception = Assert.Throws<PlatformNotSupportedException>(() =>
            UnavailableAudioPlaybackEngineFactory.Instance.Create(options));

        Assert.Contains("No audio playback backend", exception.Message);
    }
}
