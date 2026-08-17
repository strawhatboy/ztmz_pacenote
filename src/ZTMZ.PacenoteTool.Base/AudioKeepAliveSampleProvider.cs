using System;
using NAudio.Wave;

namespace ZTMZ.PacenoteTool.Base;

/// <summary>
/// Prevents drivers that power down after digital silence from losing the next audio buffer.
/// </summary>
public sealed class AudioKeepAliveSampleProvider : ISampleProvider
{
    private const float Gain = 0.0005f;
    private const double Frequency = 19.0;
    private readonly double phaseIncrement;
    private double phase;

    public AudioKeepAliveSampleProvider(WaveFormat waveFormat)
    {
        WaveFormat = waveFormat;
        phaseIncrement = 2 * Math.PI * Frequency / waveFormat.SampleRate;
    }

    public WaveFormat WaveFormat { get; }

    public int Read(Span<float> buffer)
    {
        for (var index = 0; index < buffer.Length; index += WaveFormat.Channels)
        {
            var sample = (float)(Math.Sin(phase) * Gain);
            for (var channel = 0; channel < WaveFormat.Channels && index + channel < buffer.Length; channel++)
            {
                buffer[index + channel] = sample;
            }

            phase += phaseIncrement;
            if (phase >= 2 * Math.PI)
            {
                phase -= 2 * Math.PI;
            }
        }

        return buffer.Length;
    }
}
