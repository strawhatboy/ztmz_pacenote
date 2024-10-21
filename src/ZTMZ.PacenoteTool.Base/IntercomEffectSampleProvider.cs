using System;
using NAudio.Dsp;
using NAudio.MediaFoundation;
using NAudio.Wave;

public class InterComEffectSampleProvider : ISampleProvider
{
    private static NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();
    private readonly ISampleProvider sourceProvider;
    private readonly BiQuadFilter lowPassFilter;
    private readonly BiQuadFilter highPassFilter;

    private static readonly float Q = 0.707f;

    private static readonly float LOW_FREQUENCY_MIN = 20f;
    private static readonly float LOW_FREQUENCY_MAX = 400f;
    private static readonly float HIGH_FREQUENCY_MIN = 3000f;
    private static readonly float HIGH_FREQUENCY_MAX = 20000f;

    private static readonly float DISTORTION_GAIN_MAX = 50.0f;
    private static readonly float DISTORTION_GAIN_MIN = 1.0f;
    private static readonly float DISTORTION_THRESHOLD_MAX = 1.0f;
    private static readonly float DISTORTION_THRESHOLD_MIN = 0.01f;

    private float distortionGain;
    private float distortionThreshold;

    private int level;
    
    public InterComEffectSampleProvider(ISampleProvider sourceProvider, int level)
    {
        this.level = level;
        this.sourceProvider = sourceProvider;
        // low shelf frequency ranges from 200 to 400, level ranges from 0 to 100
        var lowPassFrequency = LOW_FREQUENCY_MAX - (LOW_FREQUENCY_MAX - LOW_FREQUENCY_MIN) * (100-level) / 100f;
        highPassFilter = BiQuadFilter.HighPassFilter(sourceProvider.WaveFormat.SampleRate, lowPassFrequency, Q);
        var highPassFrequency = HIGH_FREQUENCY_MIN + (HIGH_FREQUENCY_MAX - HIGH_FREQUENCY_MIN) * (100-level) / 100f;
        lowPassFilter = BiQuadFilter.LowPassFilter(sourceProvider.WaveFormat.SampleRate, highPassFrequency, Q);
        this.WaveFormat = sourceProvider.WaveFormat;
        // distortion gain ranges from 1.0 to 2.0, distortion threshold ranges from 1.0 to 0.5
        
        distortionThreshold = DISTORTION_THRESHOLD_MIN + (DISTORTION_THRESHOLD_MAX - DISTORTION_THRESHOLD_MIN) * Math.Abs(MathF.Pow(level / 100f - 1, 3));
        distortionGain = DISTORTION_GAIN_MIN + (DISTORTION_GAIN_MAX - DISTORTION_GAIN_MIN) * Math.Abs(MathF.Pow(level / 100f, 3));

        logger.Trace($"InterComEffectSampleProvider: level={level}, lowPassFrequency={lowPassFrequency}, highPassFrequency={highPassFrequency}, distortionGain={distortionGain}, distortionThreshold={distortionThreshold}");
    }
    
    public WaveFormat WaveFormat { get; }
    
    public int Read(float[] buffer, int offset, int count)
    {
        // Read from the source
        int samplesRead = sourceProvider.Read(buffer, offset, count);
        
        // Apply the BiQuadFilter to each sample
        for (int i = 0; i < samplesRead; i++)
        {
            // filter the sound by low pass filter and high pass filter
            buffer[offset + i] = lowPassFilter.Transform(buffer[offset + i]);
            buffer[offset + i] = highPassFilter.Transform(buffer[offset + i]);

            // amplify and distort the sound
            buffer[offset + i] = buffer[offset + i] * 2;
            if (buffer[offset + i] > distortionThreshold)
            {
                buffer[offset + i] = distortionThreshold;
            }
            else if (buffer[offset + i] < -distortionThreshold)
            {
                buffer[offset + i] = -distortionThreshold;
            }
            buffer[offset + i] = buffer[offset + i] * distortionGain;
        }
        
        return samplesRead;
    }
}
