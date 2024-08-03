using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SoundTouch;

namespace ZTMZ.PacenoteTool.Base
{
    public class AutoResampledCachedSoundSampleProvider : ISampleProvider
    {
        private readonly AutoResampledCachedSound cachedSound;
        private int samplesRead = 0;

        public AutoResampledCachedSoundSampleProvider(AutoResampledCachedSound cachedSound)
        {
            this.cachedSound = cachedSound;
        }

        public int Read(float[] buffer, int offset, int count)
        {
            var availableSamples = Math.Max(0, cachedSound.AudioData.Length - samplesRead);
            var samplesToCopy = Math.Min(availableSamples, count);

            for (int i = 0; i < samplesToCopy; i++)
            {
                buffer[offset + i] = cachedSound[samplesRead + i];
            }

            // this is very necessary to avoid the sound from being cut off
            // #IAH9PW finally fixed.
            if (samplesToCopy < count && samplesToCopy > 0)
            {
                for (int i = samplesToCopy; i < count; i++)
                {
                    buffer[offset + i] = 0;
                }
            }

            samplesRead += samplesToCopy;

            if (samplesToCopy <= 0)
            {
                // no more samples to read, we return 0 to avoid infinite loop
                return 0;
            }

            return count;
        }


        public WaveFormat WaveFormat => cachedSound.WaveFormat;



    }
}
