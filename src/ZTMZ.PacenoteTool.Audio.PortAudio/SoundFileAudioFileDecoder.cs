using NAudio.SoundFile;
using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.Audio.PortAudio;

public sealed class SoundFileAudioFileDecoder : IAudioFileDecoder
{
    private static readonly HashSet<string> FfmpegOnlyExtensions = new(
        StringComparer.OrdinalIgnoreCase)
    {
        ".wma",
        ".aac",
        ".mp4",
        ".m4a"
    };
    private readonly IAudioFileDecoder fallbackDecoder;

    public SoundFileAudioFileDecoder()
        : this(new FfmpegAudioFileDecoder())
    {
    }

    public SoundFileAudioFileDecoder(IAudioFileDecoder fallbackDecoder)
    {
        this.fallbackDecoder = fallbackDecoder ??
            throw new ArgumentNullException(nameof(fallbackDecoder));
    }

    public DecodedAudio Decode(
        string fileName,
        int targetSampleRate,
        int targetChannelCount)
    {
        if (FfmpegOnlyExtensions.Contains(Path.GetExtension(fileName)))
        {
            return fallbackDecoder.Decode(
                fileName,
                targetSampleRate,
                targetChannelCount);
        }

        try
        {
            using var reader = new SoundFileReader(fileName);
            var estimatedSampleCount = reader.Length / sizeof(float);
            return AudioSampleProviderDecoder.ReadAll(
                reader,
                targetSampleRate,
                targetChannelCount,
                estimatedSampleCount);
        }
        catch (Exception primaryException)
        {
            try
            {
                return fallbackDecoder.Decode(
                    fileName,
                    targetSampleRate,
                    targetChannelCount);
            }
            catch (Exception fallbackException)
            {
                throw new InvalidDataException(
                    $"Neither libsndfile nor FFmpeg could decode '{fileName}'.",
                    new AggregateException(primaryException, fallbackException));
            }
        }
    }
}
