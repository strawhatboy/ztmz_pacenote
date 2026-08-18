using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;

namespace ZTMZ.PacenoteTool.Base;

public interface IAudioOutputDeviceEnumerator
{
    IReadOnlyList<AudioOutputDeviceInfo> EnumerateActiveRenderDevices();
}

public interface IAudioOutputDeviceChangeDetector
{
    bool RequiresPlaybackStopBeforeRefresh { get; }

    ValueTask<string> GetDeviceChangeFingerprintAsync(
        CancellationToken cancellationToken = default);
}

[SupportedOSPlatform("windows")]
public sealed class WasapiAudioOutputDeviceEnumerator :
    IAudioOutputDeviceEnumerator,
    IAudioOutputDeviceChangeDetector
{
    public bool RequiresPlaybackStopBeforeRefresh => false;

    public IReadOnlyList<AudioOutputDeviceInfo> EnumerateActiveRenderDevices()
    {
        return AudioOutputDeviceEnumerator.EnumerateActiveRenderDevices();
    }

    public ValueTask<string> GetDeviceChangeFingerprintAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(
            AudioOutputDeviceFingerprint.Create(EnumerateActiveRenderDevices()));
    }
}

public static class AudioOutputDeviceFingerprint
{
    public static string Create(IEnumerable<AudioOutputDeviceInfo> devices) =>
        string.Join(
            "\n",
            devices
                .OrderBy(device => device.Id, StringComparer.OrdinalIgnoreCase)
                .Select(device => $"{device.Id}\t{device.Name}\t{device.IsDefault}"));
}

[SupportedOSPlatform("windows")]
public static class AudioOutputDeviceEnumerator
{
    public static IReadOnlyList<AudioOutputDeviceInfo> EnumerateActiveRenderDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        using var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

        var defaultDeviceId = enumerator.TryGetDefaultAudioEndpoint(
            DataFlow.Render,
            Role.Multimedia,
            out var defaultDevice)
            ? defaultDevice.ID
            : string.Empty;
        defaultDevice?.Dispose();

        var result = new List<AudioOutputDeviceInfo>(devices.Count);
        foreach (var device in devices)
        {
            result.Add(new AudioOutputDeviceInfo(
                device.ID,
                device.FriendlyName,
                string.Equals(device.ID, defaultDeviceId, StringComparison.OrdinalIgnoreCase)));
        }

        return result
            .OrderByDescending(device => device.IsDefault)
            .ThenBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
