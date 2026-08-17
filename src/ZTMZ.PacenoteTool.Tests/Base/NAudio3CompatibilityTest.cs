using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.Tests.Base;

public class NAudio3CompatibilityTest
{
    [Fact]
    public void NVorbisSampleProvider_ImplementsCurrentSampleProviderContract()
    {
        var readerType = typeof(NVorbisSampleProvider);

        Assert.Contains(typeof(ISampleProvider), readerType.GetInterfaces());
        _ = readerType.GetInterfaceMap(typeof(ISampleProvider));
    }

    [Fact]
    public void CachedSoundDuration_PreservesFractionalSeconds()
    {
        var sound = new AutoResampledCachedSound(new float[44100]);

        Assert.Equal(0.5f, sound.Duration);
    }

    [Fact]
    public void CachedSound_DecodesMp3WithMediaFoundation()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"ztmz-mp3-test-{Guid.NewGuid():N}.mp3");
        try
        {
            var source = new SignalGenerator(44100, 1)
            {
                Frequency = 440,
                Gain = 0.2,
                Type = SignalGeneratorType.Sin
            }.Take(TimeSpan.FromMilliseconds(150)).ToWaveProvider16();
            MediaFoundationEncoder.EncodeToMp3(source, tempPath, 128000);

            var sound = new AutoResampledCachedSound(tempPath);

            Assert.False(sound.IsEmpty);
            Assert.Equal(44100, sound.WaveFormat.SampleRate);
            Assert.Equal(2, sound.WaveFormat.Channels);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }
}
