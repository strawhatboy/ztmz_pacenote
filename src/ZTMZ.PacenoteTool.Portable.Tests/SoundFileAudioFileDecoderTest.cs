using ZTMZ.PacenoteTool.Audio.PortAudio;
using ZTMZ.PacenoteTool.Base;
using Xunit;

namespace ZTMZ.PacenoteTool.Portable.Tests;

public class SoundFileAudioFileDecoderTest
{
    [Theory]
    [InlineData("voice.wma")]
    [InlineData("voice.aac")]
    [InlineData("voice.mp4")]
    [InlineData("voice.m4a")]
    public void Decode_RoutesMediaContainerFormatsToFallback(string fileName)
    {
        var expected = new DecodedAudio(
            [0.25f, -0.25f],
            NAudio.Wave.WaveFormat.CreateIeeeFloatWaveFormat(44100, 2));
        var fallback = new RecordingDecoder(expected);
        var decoder = new SoundFileAudioFileDecoder(fallback);

        var actual = decoder.Decode(fileName, 44100, 2);

        Assert.Same(expected, actual);
        Assert.Equal(fileName, fallback.LastFileName);
    }

    private sealed class RecordingDecoder(DecodedAudio result) : IAudioFileDecoder
    {
        public string? LastFileName { get; private set; }

        public DecodedAudio Decode(
            string fileName,
            int targetSampleRate,
            int targetChannelCount)
        {
            LastFileName = fileName;
            return result;
        }
    }
}
