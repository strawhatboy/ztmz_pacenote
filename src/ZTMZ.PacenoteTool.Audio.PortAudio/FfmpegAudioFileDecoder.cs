using System.Diagnostics;
using NAudio.Wave;
using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.Audio.PortAudio;

public sealed class FfmpegAudioFileDecoder : IAudioFileDecoder
{
    private const string FfmpegPathEnvironmentVariable = "ZTMZ_FFMPEG_PATH";

    public DecodedAudio Decode(
        string fileName,
        int targetSampleRate,
        int targetChannelCount)
    {
        if (targetSampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetSampleRate));
        }

        if (targetChannelCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetChannelCount));
        }

        var executable = Environment.GetEnvironmentVariable(
            FfmpegPathEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(executable))
        {
            executable = "ffmpeg";
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("-nostdin");
        startInfo.ArgumentList.Add("-hide_banner");
        startInfo.ArgumentList.Add("-loglevel");
        startInfo.ArgumentList.Add("error");
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(fileName);
        startInfo.ArgumentList.Add("-vn");
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add("f32le");
        startInfo.ArgumentList.Add("-acodec");
        startInfo.ArgumentList.Add("pcm_f32le");
        startInfo.ArgumentList.Add("-ar");
        startInfo.ArgumentList.Add(targetSampleRate.ToString(
            System.Globalization.CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("-ac");
        startInfo.ArgumentList.Add(targetChannelCount.ToString(
            System.Globalization.CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("pipe:1");

        using var process = Process.Start(startInfo) ??
            throw new InvalidOperationException("Failed to start FFmpeg.");
        var errorTask = process.StandardError.ReadToEndAsync();
        using var pcm = new MemoryStream();
        process.StandardOutput.BaseStream.CopyTo(pcm);
        process.WaitForExit();
        var error = errorTask.GetAwaiter().GetResult().Trim();

        if (process.ExitCode != 0)
        {
            throw new InvalidDataException(
                string.IsNullOrWhiteSpace(error)
                    ? $"FFmpeg exited with code {process.ExitCode}."
                    : $"FFmpeg exited with code {process.ExitCode}: {error}");
        }

        if (pcm.Length % sizeof(float) != 0)
        {
            throw new InvalidDataException(
                $"FFmpeg returned {pcm.Length} bytes, which is not valid float PCM.");
        }

        var samples = new float[checked((int)(pcm.Length / sizeof(float)))];
        if (samples.Length > 0)
        {
            Buffer.BlockCopy(pcm.GetBuffer(), 0, samples, 0, checked((int)pcm.Length));
        }

        return new DecodedAudio(
            samples,
            WaveFormat.CreateIeeeFloatWaveFormat(
                targetSampleRate,
                targetChannelCount));
    }
}
