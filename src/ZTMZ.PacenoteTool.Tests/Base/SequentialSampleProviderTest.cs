using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.Tests.Base;

public class SequentialSampleProviderTest
{
    [Fact]
    public void Read_CombinesPartialInputsWithoutDroppingSamples()
    {
        var format = WaveFormat.CreateIeeeFloatWaveFormat(44100, 1);
        var provider = new SequentialSampleProvider(format);
        provider.AddSequentialInput(new ChunkedSampleProvider(format, new[] { 1f, 2f, 3f }, 2));
        provider.AddSequentialInput(new ChunkedSampleProvider(format, new[] { 4f, 5f }, 1));
        var buffer = new float[5];

        var samplesRead = provider.Read(buffer);

        Assert.Equal(5, samplesRead);
        Assert.Equal(new[] { 1f, 2f, 3f, 4f, 5f }, buffer);
    }

    [Fact]
    public void ReadFully_KeepsTheQueueAvailableForFutureInputs()
    {
        var format = WaveFormat.CreateIeeeFloatWaveFormat(44100, 1);
        var provider = new SequentialSampleProvider(format) { ReadFully = true };
        var buffer = new float[4];

        Assert.Equal(4, provider.Read(buffer));
        Assert.Equal(new[] { 0f, 0f, 0f, 0f }, buffer);

        provider.AddSequentialInput(new ChunkedSampleProvider(format, new[] { 1f, 2f }, 1));
        Assert.Equal(4, provider.Read(buffer));
        Assert.Equal(new[] { 1f, 2f, 0f, 0f }, buffer);
    }

    [Fact]
    public void Read_EmitsAudioFromTheSequentialMixerPipeline()
    {
        var format = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
        var soundData = new float[format.SampleRate * format.Channels];
        for (var i = 0; i < soundData.Length; i += format.Channels)
        {
            var sample = MathF.Sin(2 * MathF.PI * 440 * (i / format.Channels) / format.SampleRate);
            soundData[i] = sample;
            soundData[i + 1] = sample;
        }

        var sequential = new SequentialSampleProvider(format) { ReadFully = true };
        sequential.AddSequentialInput(
            new InterComEffectSampleProvider(
                new VariSpeedSampleProvider(
                    new AutoResampledCachedSoundSampleProvider(new AutoResampledCachedSound(soundData)),
                    500,
                    playbackRate: 1f,
                    useTempo: true),
                level: 0));
        var mixer = new MixingSampleProvider(format) { ReadFully = true };
        mixer.AddMixerInput(sequential);

        var buffer = new float[4096];
        var maxSample = 0f;
        for (var i = 0; i < 30; i++)
        {
            mixer.Read(buffer);
            maxSample = Math.Max(maxSample, buffer.Max(MathF.Abs));
        }

        Assert.True(maxSample > 0.01f);
    }

    [Fact]
    public void Read_TempoProviderPreservesTheInitialSourceSegment()
    {
        var format = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
        var soundData = new float[format.SampleRate * format.Channels * 2];
        var openingFrames = format.SampleRate / 4;
        for (var frame = 0; frame < soundData.Length / format.Channels; frame++)
        {
            var sample = frame < openingFrames ? 0.75f : -0.75f;
            soundData[frame * format.Channels] = sample;
            soundData[frame * format.Channels + 1] = sample;
        }

        var provider = new VariSpeedSampleProvider(
            new AutoResampledCachedSoundSampleProvider(new AutoResampledCachedSound(soundData)),
            500,
            playbackRate: 1f,
            useTempo: true);
        var buffer = new float[4096];
        var firstAudibleSample = 0f;

        for (var i = 0; i < 100 && firstAudibleSample == 0f; i++)
        {
            var samplesRead = provider.Read(buffer);
            for (var sample = 0; sample < samplesRead; sample++)
            {
                if (MathF.Abs(buffer[sample]) > 0.1f)
                {
                    firstAudibleSample = buffer[sample];
                    break;
                }
            }
        }

        Assert.True(firstAudibleSample > 0.1f);
    }

    [Fact]
    public void Read_KeepAliveProviderNeverReturnsDigitalSilence()
    {
        var provider = new AudioKeepAliveSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(44100, 2));
        var buffer = new float[4096];

        Assert.Equal(buffer.Length, provider.Read(buffer));
        Assert.Contains(buffer, sample => MathF.Abs(sample) > 0f);
    }

    private sealed class ChunkedSampleProvider : ISampleProvider
    {
        private readonly float[] samples;
        private readonly int maxSamplesPerRead;
        private int position;

        public ChunkedSampleProvider(WaveFormat waveFormat, float[] samples, int maxSamplesPerRead)
        {
            WaveFormat = waveFormat;
            this.samples = samples;
            this.maxSamplesPerRead = maxSamplesPerRead;
        }

        public WaveFormat WaveFormat { get; }

        public int Read(Span<float> buffer)
        {
            var samplesToRead = Math.Min(Math.Min(maxSamplesPerRead, buffer.Length), samples.Length - position);
            samples.AsSpan(position, samplesToRead).CopyTo(buffer);
            position += samplesToRead;
            return samplesToRead;
        }
    }
}
