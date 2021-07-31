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
        private float[] _amplifiedAudioData = new float[] { };

        private int _amplification = 0;
        public int Amplification
        {
            get => this._amplification; set
            {
                this._amplification = value;
                if (value > 0)
                {
                    this._amplifiedAudioData = (from x in this._audioData select (x * (1000 + value * 10) / 1000)).ToArray();
                } else if (value < 0)
                {
                    this._amplifiedAudioData = (from x in this._audioData select (x * (1000 + value) / 1000)).ToArray();
                } else
                {
                    this._amplifiedAudioData = this._audioData;
                }
            }
        }

        public float[] AudioData
        {
            get
            {
                if (this.Amplification != 0)
                {
                    return this._amplifiedAudioData;
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