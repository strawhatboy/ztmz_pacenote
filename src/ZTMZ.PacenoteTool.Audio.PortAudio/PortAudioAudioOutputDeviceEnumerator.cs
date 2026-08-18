using System.Diagnostics;
using System.Text.RegularExpressions;
using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.Audio.PortAudio;

public sealed class PortAudioAudioOutputDeviceEnumerator :
    IAudioOutputDeviceEnumerator,
    IAudioOutputDeviceChangeDetector
{
    private const int PlaybackChannelCount = 2;

    public bool RequiresPlaybackStopBeforeRefresh => true;

    public IReadOnlyList<AudioOutputDeviceInfo> EnumerateActiveRenderDevices()
    {
        using var runtime = PortAudioRuntime.Acquire();
        var candidates = new List<(int Index, global::PortAudioSharp.DeviceInfo Info)>();

        for (var index = 0; index < global::PortAudioSharp.PortAudio.DeviceCount; index++)
        {
            var device = global::PortAudioSharp.PortAudio.GetDeviceInfo(index);
            if (device.maxOutputChannels < PlaybackChannelCount)
            {
                continue;
            }

            candidates.Add((index, device));
        }

        var hostApis = PortAudioNative.GetHostApis()
            .OrderBy(GetHostApiPriority)
            .ThenBy(hostApi => hostApi.Index)
            .ToArray();
        foreach (var hostApi in hostApis)
        {
            var hostCandidates = candidates
                .Where(candidate => candidate.Info.hostApi == hostApi.Index)
                .Where(candidate =>
                    !OperatingSystem.IsLinux() ||
                    IsLinuxUserFacingDeviceName(candidate.Info.name))
                .Where(candidate => PortAudioNative.IsOutputFormatSupported(
                    candidate.Index,
                    candidate.Info,
                    candidate.Info.defaultSampleRate,
                    PlaybackChannelCount))
                .ToArray();
            if (hostCandidates.Length == 0)
            {
                continue;
            }

            var defaultDevice = OperatingSystem.IsLinux()
                ? hostCandidates
                    .OrderBy(candidate => GetLinuxDevicePriority(candidate.Info.name))
                    .ThenBy(candidate => candidate.Info.name, StringComparer.OrdinalIgnoreCase)
                    .First()
                    .Index
                : hostCandidates.Any(candidate =>
                    candidate.Index == hostApi.DefaultOutputDevice)
                    ? hostApi.DefaultOutputDevice
                    : hostCandidates[0].Index;
            return hostCandidates
                .Select(candidate => new AudioOutputDeviceInfo(
                    CreateStableDeviceId(hostApi, candidate.Info.name),
                    candidate.Info.name,
                    candidate.Index == defaultDevice))
                .OrderByDescending(device => device.IsDefault)
                .ThenBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        return [];
    }

    public async ValueTask<string> GetDeviceChangeFingerprintAsync(
        CancellationToken cancellationToken = default)
    {
        if (OperatingSystem.IsWindows())
        {
            return AudioOutputDeviceFingerprint.Create(
                new WasapiAudioOutputDeviceEnumerator().EnumerateActiveRenderDevices());
        }

        if (OperatingSystem.IsLinux())
        {
            var pipeWireFingerprint = await TryGetPipeWireFingerprintAsync(
                cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(pipeWireFingerprint))
            {
                return pipeWireFingerprint;
            }

            const string soundDeviceDirectory = "/dev/snd";
            return Directory.Exists(soundDeviceDirectory)
                ? string.Join(
                    "\n",
                    Directory.EnumerateFileSystemEntries(soundDeviceDirectory)
                        .Select(Path.GetFileName)
                        .Order(StringComparer.Ordinal))
                : string.Empty;
        }

        return AudioOutputDeviceFingerprint.Create(EnumerateActiveRenderDevices());
    }

    internal static string CreateStableDeviceId(
        PortAudioHostApiInfo hostApi,
        string deviceName) =>
        $"portaudio:{hostApi.Type}:{Uri.EscapeDataString(deviceName)}";

    internal static bool IsLinuxUserFacingDeviceName(string name)
    {
        var normalized = name.Trim().ToLowerInvariant();
        return normalized is "default" or "pipewire" or "pulse" or "sysdefault" ||
               normalized.Contains("(hw:", StringComparison.Ordinal);
    }

    internal static int GetLinuxDevicePriority(string name)
    {
        return name.Trim().ToLowerInvariant() switch
        {
            "pipewire" => 0,
            "pulse" => 1,
            "default" => 2,
            "sysdefault" => 3,
            _ => 10
        };
    }

    private static async Task<string?> TryGetPipeWireFingerprintAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "wpctl",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.StartInfo.ArgumentList.Add("status");
            process.StartInfo.ArgumentList.Add("--name");
            if (!process.Start())
            {
                return null;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                return null;
            }

            var output = await outputTask.ConfigureAwait(false);
            return Regex.Replace(
                    output,
                    @"\s+\[(?:vol:[^\]]+|MUTED)\]",
                    string.Empty,
                    RegexOptions.IgnoreCase)
                .Trim();
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException ||
            !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private static int GetHostApiPriority(PortAudioHostApiInfo hostApi)
    {
        if (OperatingSystem.IsWindows())
        {
            return hostApi.Name switch
            {
                "Windows WASAPI" => 0,
                "Windows DirectSound" => 1,
                "MME" => 2,
                _ => 100
            };
        }

        if (OperatingSystem.IsLinux())
        {
            return hostApi.Name switch
            {
                "PipeWire" => 0,
                "ALSA" => 1,
                "JACK Audio Connection Kit" => 2,
                "OSS" => 3,
                _ => 100
            };
        }

        if (OperatingSystem.IsMacOS())
        {
            return hostApi.Name == "Core Audio" ? 0 : 100;
        }

        return hostApi.Index;
    }
}
