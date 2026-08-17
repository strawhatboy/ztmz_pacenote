using System;
using System.Runtime.Versioning;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace ZTMZ.PacenoteTool.Base;

[SupportedOSPlatform("windows")]
public sealed class WasapiAudioOutputBackend : IAudioOutputBackend
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    private readonly MMDeviceEnumerator deviceEnumerator;
    private readonly MMDevice device;
    private readonly WasapiPlayer player;
    private bool isDisposed;

    public WasapiAudioOutputBackend(string deviceId, int desiredLatencyMilliseconds)
    {
        deviceEnumerator = new MMDeviceEnumerator();
        device = string.IsNullOrWhiteSpace(deviceId)
            ? deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
            : deviceEnumerator.GetDevice(deviceId);

        player = new WasapiPlayerBuilder()
            .WithDevice(device)
            .WithSharedMode()
            .WithEventSync()
            .WithLatency(desiredLatencyMilliseconds)
            .WithCategory(AudioStreamCategory.Media)
            .WithMmcssThreadPriority("Audio")
            .Build();

        DeviceId = device.ID;
        DeviceName = device.FriendlyName;
        LatencyMilliseconds = desiredLatencyMilliseconds;

        Logger.Info(
            "Created WASAPI media output for {0} ({1}) with {2} ms desired latency.",
            DeviceName,
            DeviceId,
            LatencyMilliseconds);
    }

    public string DeviceId { get; }

    public string DeviceName { get; }

    public int LatencyMilliseconds { get; }

    public void Start(ISampleProvider source)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        player.Init(source);
        TryDisableWindowsDucking();
        player.Play();
    }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        player.Dispose();
        device.Dispose();
        deviceEnumerator.Dispose();
    }

    private void TryDisableWindowsDucking()
    {
        try
        {
            using var sessionManager = device.AudioSessionManager;
            using var sessionControl = sessionManager.AudioSessionControl;
            sessionControl.SetDuckingPreference(true);
        }
        catch (Exception exception)
        {
            // The Media category remains the primary protection. Older drivers may not expose
            // session ducking control, so failure here must not prevent pacenote playback.
            Logger.Warn(exception, "Could not opt the audio session out of Windows ducking.");
        }
    }
}
