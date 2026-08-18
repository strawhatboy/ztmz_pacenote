using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.Audio.PortAudio;

public sealed class PortAudioAudioOutputDeviceEnumerator : IAudioOutputDeviceEnumerator
{
    public IReadOnlyList<AudioOutputDeviceInfo> EnumerateActiveRenderDevices()
    {
        using var runtime = PortAudioRuntime.Acquire();
        var defaultDevice = global::PortAudioSharp.PortAudio.DefaultOutputDevice;
        var devices = new List<AudioOutputDeviceInfo>();

        for (var index = 0; index < global::PortAudioSharp.PortAudio.DeviceCount; index++)
        {
            var device = global::PortAudioSharp.PortAudio.GetDeviceInfo(index);
            if (device.maxOutputChannels <= 0)
            {
                continue;
            }

            devices.Add(new AudioOutputDeviceInfo(
                index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                device.name,
                index == defaultDevice));
        }

        return devices
            .OrderByDescending(device => device.IsDefault)
            .ThenBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
