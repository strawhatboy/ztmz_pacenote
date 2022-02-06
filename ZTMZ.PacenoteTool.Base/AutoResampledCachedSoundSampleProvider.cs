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

            var availableSamples = cachedSound.AudioData.Length - samplesRead;
            var samplesToCopy = Math.Min(availableSamples, count);

            
            // // if (_processor.AvailableSamples == 0)
            // // {
            // if (samplesToCopy > 0)
            // {
            //     _processor.PutSamples(cachedSound.AudioData[samplesRead..], samplesToCopy);
            // }
            // // }
            //
            //
            // var soundTouchReadBuffer = new float[samplesToCopy];
            // var desiredSampleFrames = samplesToCopy/this.WaveFormat.Channels;
            // var received = _processor.ReceiveSamples(soundTouchReadBuffer, desiredSampleFrames) *
            //                this.WaveFormat.Channels;

            for (int i = 0; i < samplesToCopy; i++)
            {
                buffer[offset + i] = cachedSound.AudioData[samplesRead + i];
            }
            // Array.Copy(cachedSound.AudioData, position, buffer, offset, samplesToCopy);
            samplesRead += samplesToCopy;
            return (int)samplesToCopy;
        }


        public WaveFormat WaveFormat => cachedSound.WaveFormat;



    }
}
