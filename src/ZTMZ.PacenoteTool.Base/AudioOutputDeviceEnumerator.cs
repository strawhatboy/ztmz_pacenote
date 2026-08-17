using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Linq;
using NAudio.CoreAudioApi;

namespace ZTMZ.PacenoteTool.Base;

public interface IAudioOutputDeviceEnumerator
{
    IReadOnlyList<AudioOutputDeviceInfo> EnumerateActiveRenderDevices();
}

[SupportedOSPlatform("windows")]
public sealed class WasapiAudioOutputDeviceEnumerator : IAudioOutputDeviceEnumerator
{
    public IReadOnlyList<AudioOutputDeviceInfo> EnumerateActiveRenderDevices()
    {
        return AudioOutputDeviceEnumerator.EnumerateActiveRenderDevices();
    }
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
