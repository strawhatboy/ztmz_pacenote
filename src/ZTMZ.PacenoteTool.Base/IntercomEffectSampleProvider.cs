using NAudio.Dsp;
using NAudio.Wave;

public class InterComEffectSampleProvider : ISampleProvider
{
    private readonly ISampleProvider sourceProvider;
    private readonly BiQuadFilter lowShelfFilter;
    private readonly BiQuadFilter highShelfFilter;

    private static readonly float SHELF_SLOPE = 0.707f;

    private static readonly float LOW_FREQUENCY_MIN = 250f;
    private static readonly float LOW_FREQUENCY_MAX = 1250f;
    private static readonly float HIGH_FREQUENCY_MIN = 1250f;
    private static readonly float HIGH_FREQUENCY_MAX = 20000f;

    private int level;
    
    public InterComEffectSampleProvider(ISampleProvider sourceProvider, int level)
    {
        this.level = level;
        this.sourceProvider = sourceProvider;
        // low shelf frequency ranges from 200 to 400, level ranges from 0 to 100
        var lowShelfFrequency = LOW_FREQUENCY_MAX - (LOW_FREQUENCY_MAX - LOW_FREQUENCY_MIN) * (100-level) / 100;
        lowShelfFilter = BiQuadFilter.HighPassFilter(sourceProvider.WaveFormat.SampleRate, lowShelfFrequency, 1.0f);
        var highShelfFrequency = HIGH_FREQUENCY_MIN + (HIGH_FREQUENCY_MAX - HIGH_FREQUENCY_MIN) * (100-level) / 100;
        highShelfFilter = BiQuadFilter.LowPassFilter(sourceProvider.WaveFormat.SampleRate, highShelfFrequency, 1.0f);
        this.WaveFormat = sourceProvider.WaveFormat;
    }
    
    public WaveFormat WaveFormat { get; }
    
    public int Read(float[] buffer, int offset, int count)
    {
        // Read from the source
        int samplesRead = sourceProvider.Read(buffer, offset, count);
        
        // Apply the BiQuadFilter to each sample
        for (int i = 0; i < samplesRead; i++)
        {
            buffer[offset + i] = lowShelfFilter.Transform(buffer[offset + i]);
            buffer[offset + i] = highShelfFilter.Transform(buffer[offset + i]);

            // need to amplify the sound by level, when intercom level become larger, the sound will be louder
            buffer[offset + i] *= (level*15+100) / 100f;
        }
        
        return samplesRead;
    }
}
