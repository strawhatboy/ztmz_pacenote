using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.Audio.PortAudio;

public sealed class PortAudioAudioPlaybackEngineFactory : IAudioPlaybackEngineFactory
{
    public IAudioPlaybackEngine Create(AudioPlaybackEngineOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var backend = new PortAudioAudioOutputBackend(
            options.DeviceEndpointId,
            options.DesiredLatencyMilliseconds,
            options.SampleRate,
            options.ChannelCount);

        try
        {
            return new ZTMZAudioPlaybackEngine(
                backend,
                options.DeviceIndex,
                options.IsSequential,
                options.DesiredLatencyMilliseconds,
                options.SampleRate,
                options.ChannelCount);
        }
        catch
        {
            backend.Dispose();
            throw;
        }
    }
}
