using System;
using System.Collections.Generic;
using System.Runtime.Versioning;

namespace ZTMZ.PacenoteTool.Base;

public interface IAudioPlaybackEngine : IDisposable
{
    bool IsMuted { get; }

    float PlaybackRate { get; set; }

    AudioPlaybackStatus Status { get; }

    void SetMuted(bool muted);

    void PlaySound(
        AutoResampledCachedSound sound,
        bool isSequential = true,
        bool isSystem = false);
}

public sealed record AudioPlaybackEngineOptions(
    string DeviceEndpointId,
    int DeviceIndex,
    bool IsSequential,
    int DesiredLatencyMilliseconds,
    int SampleRate = 44100,
    int ChannelCount = 2);

public interface IAudioPlaybackEngineFactory
{
    IAudioPlaybackEngine Create(AudioPlaybackEngineOptions options);
}

public static class AudioPlatformServices
{
    public static IAudioOutputDeviceEnumerator CreateDefaultDeviceEnumerator()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WasapiAudioOutputDeviceEnumerator();
        }

        return EmptyAudioOutputDeviceEnumerator.Instance;
    }

    public static IAudioPlaybackEngineFactory CreateDefaultPlaybackEngineFactory()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WasapiAudioPlaybackEngineFactory();
        }

        return UnavailableAudioPlaybackEngineFactory.Instance;
    }
}

public sealed class EmptyAudioOutputDeviceEnumerator : IAudioOutputDeviceEnumerator
{
    public static EmptyAudioOutputDeviceEnumerator Instance { get; } = new();

    private EmptyAudioOutputDeviceEnumerator()
    {
    }

    public IReadOnlyList<AudioOutputDeviceInfo> EnumerateActiveRenderDevices()
    {
        return Array.Empty<AudioOutputDeviceInfo>();
    }
}

public sealed class UnavailableAudioPlaybackEngineFactory : IAudioPlaybackEngineFactory
{
    public static UnavailableAudioPlaybackEngineFactory Instance { get; } = new();

    private UnavailableAudioPlaybackEngineFactory()
    {
    }

    public IAudioPlaybackEngine Create(AudioPlaybackEngineOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        throw new PlatformNotSupportedException(
            "No audio playback backend is available for this platform.");
    }
}

[SupportedOSPlatform("windows")]
public sealed class WasapiAudioPlaybackEngineFactory : IAudioPlaybackEngineFactory
{
    public IAudioPlaybackEngine Create(AudioPlaybackEngineOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new ZTMZAudioPlaybackEngine(
            options.DeviceEndpointId,
            options.DeviceIndex,
            options.IsSequential,
            options.DesiredLatencyMilliseconds,
            options.SampleRate,
            options.ChannelCount);
    }
}
