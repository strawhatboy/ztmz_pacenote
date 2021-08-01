using NAudio.Utils;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ZTMZ.PacenoteTool
{
    public class SequentialSampleProvider : ISampleProvider
    {
        private readonly Queue<ISampleProvider> sources;
        private const int MaxInputs = 1024; // protect ourselves against doing something silly
        private float[] sourceBuffer;
        public WaveFormat WaveFormat { get; private set; }
        public bool ReadFully { get; set; }

        public SequentialSampleProvider(WaveFormat waveFormat)
        {
            if (waveFormat.Encoding != WaveFormatEncoding.IeeeFloat)
            {
                throw new ArgumentException("Sequential Sample wave format must be IEEE float");
            }
            sources = new Queue<ISampleProvider>();
            WaveFormat = waveFormat;
        }
        public void AddSequentialInput(ISampleProvider sequentialInput)
        {
            // we'll just call the lock around add since we are protecting against an AddSequentialInput at
            // the same time as a Read, rather than two AddSequentialInput calls at the same time
            lock (sources)
            {
                if (sources.Count >= MaxInputs)
                {
                    throw new InvalidOperationException("Too many sequential inputs");
                }
                sources.Enqueue(sequentialInput);
            }
            if (WaveFormat == null)
            {
                WaveFormat = sequentialInput.WaveFormat;
            }
            else
            {
                if (WaveFormat.SampleRate != sequentialInput.WaveFormat.SampleRate ||
                    WaveFormat.Channels != sequentialInput.WaveFormat.Channels)
                {
                    throw new ArgumentException("All sequential inputs must have the same WaveFormat");
                }
            }
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int outputSamples = 0;
            sourceBuffer = BufferHelpers.Ensure(sourceBuffer, count);
            lock (sources)
            {
                while (outputSamples < count && sources.Count > 0)
                {
                    var source = sources.Peek();
                    int samplesRead = source.Read(sourceBuffer, 0, count);
                    int outIndex = offset;
                    for (int n = 0; n < samplesRead; n++)
                    {
                        buffer[outIndex++] = sourceBuffer[n];
                    }
                    outputSamples = Math.Max(samplesRead, outputSamples);
                    if (samplesRead < count)
                    {
                        sources.Dequeue();
                    }
                }
            }
            // optionally ensure we return a full buffer
            if (ReadFully && outputSamples < count)
            {
                int outputIndex = offset + outputSamples;
                while (outputIndex < offset + count)
                {
                    buffer[outputIndex++] = 0;
                }
                outputSamples = count;
            }
            return outputSamples;
        }
    }
}
