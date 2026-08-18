using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace ZTMZ.PacenoteTool.Base;

public sealed record DecodedAudio(float[] Samples, WaveFormat WaveFormat);

public interface IAudioFileDecoder
{
    DecodedAudio Decode(string fileName, int targetSampleRate, int targetChannelCount);
}

public static class AudioDecodingServices
{
    private static IAudioFileDecoder decoder = DefaultAudioFileDecoder.Instance;

    public static IAudioFileDecoder Decoder
    {
        get => Volatile.Read(ref decoder);
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            Volatile.Write(ref decoder, value);
        }
    }
}

public static class AudioSampleProviderDecoder
{
    public static DecodedAudio ReadAll(
        ISampleProvider source,
        int targetSampleRate,
        int targetChannelCount,
        long estimatedSourceSampleCount = 0)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (targetSampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetSampleRate));
        }

        if (targetChannelCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetChannelCount));
        }

        var normalized = ConvertChannelCount(source, targetChannelCount);
        if (normalized.WaveFormat.SampleRate != targetSampleRate)
        {
            normalized = new WdlResamplingSampleProvider(normalized, targetSampleRate);
        }

        var outputFormat = WaveFormat.CreateIeeeFloatWaveFormat(
            targetSampleRate,
            targetChannelCount);
        var capacity = EstimateOutputCapacity(
            estimatedSourceSampleCount,
            source.WaveFormat,
            outputFormat);
        var samples = capacity > 0
            ? new List<float>(capacity)
            : [];
        var buffer = new float[targetSampleRate * targetChannelCount];
        int samplesRead;
        while ((samplesRead = normalized.Read(buffer)) > 0)
        {
            for (var index = 0; index < samplesRead; index++)
            {
                samples.Add(buffer[index]);
            }
        }

        return new DecodedAudio(samples.ToArray(), outputFormat);
    }

    private static ISampleProvider ConvertChannelCount(
        ISampleProvider source,
        int targetChannelCount)
    {
        if (source.WaveFormat.Channels == targetChannelCount)
        {
            return source;
        }

        if (source.WaveFormat.Channels == 1 && targetChannelCount == 2)
        {
            return new MonoToStereoSampleProvider(source);
        }

        if (source.WaveFormat.Channels == 2 && targetChannelCount == 1)
        {
            return new StereoToMonoSampleProvider(source);
        }

        throw new NotSupportedException(
            $"Audio channel conversion from {source.WaveFormat.Channels} to " +
            $"{targetChannelCount} channels is not supported.");
    }

    private static int EstimateOutputCapacity(
        long sourceSampleCount,
        WaveFormat sourceFormat,
        WaveFormat targetFormat)
    {
        if (sourceSampleCount <= 0)
        {
            return 0;
        }

        var estimate = sourceSampleCount *
                       (double)targetFormat.SampleRate / sourceFormat.SampleRate *
                       targetFormat.Channels / sourceFormat.Channels;
        return (int)Math.Min(Math.Ceiling(estimate), int.MaxValue);
    }
}

internal sealed class DefaultAudioFileDecoder : IAudioFileDecoder
{
    public static DefaultAudioFileDecoder Instance { get; } = new();

    private DefaultAudioFileDecoder()
    {
    }

    public DecodedAudio Decode(
        string fileName,
        int targetSampleRate,
        int targetChannelCount)
    {
        IDisposable owner;
        ISampleProvider source;
        long estimatedSampleCount;
        var extension = Path.GetExtension(fileName);

        if (extension.Equals(".ogg", StringComparison.OrdinalIgnoreCase))
        {
            var reader = new NVorbisSampleProvider(fileName);
            owner = reader;
            source = reader;
            estimatedSampleCount = reader.TotalSamples * reader.WaveFormat.Channels;
        }
        else if (IsMediaFoundationFormat(extension))
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException(
                    $"{extension} decoding requires a platform audio decoder.");
            }

            var reader = new MediaFoundationReader(fileName);
            owner = reader;
            source = reader.ToSampleProvider();
            estimatedSampleCount = reader.Length /
                                   Math.Max(1, reader.WaveFormat.BitsPerSample / 8);
        }
        else
        {
            var reader = new AudioFileReader(fileName);
            owner = reader;
            source = reader;
            estimatedSampleCount = reader.Length / sizeof(float);
        }

        using (owner)
        {
            return AudioSampleProviderDecoder.ReadAll(
                source,
                targetSampleRate,
                targetChannelCount,
                estimatedSampleCount);
        }
    }

    private static bool IsMediaFoundationFormat(string extension)
    {
        return extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".wma", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".aac", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".m4a", StringComparison.OrdinalIgnoreCase);
    }
}
