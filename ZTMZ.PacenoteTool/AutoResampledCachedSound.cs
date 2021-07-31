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
        private float[] _audioData = new float[] { };

        public int Amplification { get; set; } = 100;

        public float[] AudioData
        {
            get
            {
                if (this.Amplification != 100)
                {
                    return (from x in this._audioData select (x * this.Amplification / 100)).ToArray();
                }

                return this._audioData;
            }
            private set
            {
                this._audioData = value;
            }
        }

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);

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

                var wholeFile = new List<float>((int) (audioFileReader.Length / 4));
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