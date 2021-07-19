using NAudio.Extras;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ZTMZ.PacenoteTool
{
    public class AutoResampledCachedSound
    {
        public float[] AudioData { get; private set; } = new float[] { };
        public WaveFormat WaveFormat { get; }
        public AutoResampledCachedSound()
        {
        }

        public AutoResampledCachedSound(string audioFileName)
        {
            using (var audioFileReader = new AudioFileReader(audioFileName))
            {
                var resampledAudio = new WaveToSampleProvider(new MediaFoundationResampler(
                    new SampleToWaveProvider(audioFileReader),
                    WaveFormat.CreateIeeeFloatWaveFormat(44100, 2)));
                this.WaveFormat = resampledAudio.WaveFormat;

                var wholeFile = new List<float>((int)(audioFileReader.Length / 4));
                var buffer = new float[resampledAudio.WaveFormat.SampleRate * resampledAudio.WaveFormat.Channels];
                int samplesRead;
                while ((samplesRead = resampledAudio.Read(buffer, 0, buffer.Length)) > 0)
                {
                    wholeFile.AddRange(buffer.Take(samplesRead));
                }
                this.AudioData = wholeFile.ToArray();
            }

        }

        public void Append(AutoResampledCachedSound sound)
        {
            this.AudioData = this.AudioData.Concat(sound.AudioData).ToArray();
        }
    }
}
