using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ZTMZ.PacenoteTool
{
    public class AutoResampledCachedSoundSampleProvider : ISampleProvider
    {
        private readonly AutoResampledCachedSound cachedSound;
        private long position;

        public AutoResampledCachedSoundSampleProvider(AutoResampledCachedSound cachedSound)
        {
            this.cachedSound = cachedSound;
        }

        public int Read(float[] buffer, int offset, int count)
        {
            var availableSamples = cachedSound.AudioData.Length - position;
            var samplesToCopy = Math.Min(availableSamples, count);
            for (int i = 0; i < samplesToCopy; i++)
            {
                buffer[offset + i] = cachedSound.AudioData[position + i];
            }
            //Array.Copy(cachedSound.AudioData, position, buffer, offset, samplesToCopy);
            position += samplesToCopy;
            return (int)samplesToCopy;
        }

        public WaveFormat WaveFormat => cachedSound.WaveFormat;
    }
}
