using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using ZTMZ.PacenoteTool.Base;
using Xunit;

namespace ZTMZ.PacenoteTool.Portable.Tests;

public class AudioSampleProviderDecoderTest
{
    [Fact]
    public void ReadAll_ResamplesMonoInputToTargetStereoFormat()
    {
        var source = new SignalGenerator(22050, 1)
        {
            Frequency = 440,
            Gain = 0.2,
            Type = SignalGeneratorType.Sin
        }.Take(TimeSpan.FromMilliseconds(100));

        var decoded = AudioSampleProviderDecoder.ReadAll(source, 44100, 2);

        Assert.Equal(44100, decoded.WaveFormat.SampleRate);
        Assert.Equal(2, decoded.WaveFormat.Channels);
        Assert.InRange(decoded.Samples.Length, 8700, 8900);
    }
}
