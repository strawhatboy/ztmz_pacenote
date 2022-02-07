using NAudio.Extras;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ZTMZ.PacenoteTool.Base
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
                // if (value > 0)
                // {
                //     this._amplifiedAudioData = (from x in this._audioData select (x * (1000 + value * 10) / 1000)).ToArray();
                // } else if (value < 0)
                // {
                //     this._amplifiedAudioData = (from x in this._audioData select (x * (1000 + value) / 1000)).ToArray();
                // } else
                // {
                //     this._amplifiedAudioData = this._audioData;
                // }
            }
        }

        public float[] AudioData
        {
            get
            {
                return this._audioData;
            }
            private set
            {
                this._audioData = value;
            }
        }

        public bool IsEmpty { get => this.AudioData.Length == 0; }

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
        
        private float? _mean;
        public float Mean
        {
            get
            {
                if (!_mean.HasValue)
                {
                    _mean = (from p in this.AudioData select Math.Abs(p)).Average();
                }
                return _mean.Value;
            }
        }

        private float? _max;
        public float Max
        {
            get
            {
                if (!_max.HasValue)
                {
                    _max = (from p in this.AudioData select Math.Abs(p)).Max();
                }
                return _max.Value;
            }
        }

        /// <summary>
        /// Tension: 0f-1f, 1f: high tense
        /// </summary>
        public float Tension { set; get; } = 0f;
        
        /// <summary>
        /// Duration: In seconds
        /// </summary>
        public float Duration => this.AudioData.Length / this.WaveFormat.Channels / this.WaveFormat.SampleRate; 

        public void CutHeadAndTail(double cutRatio)
        {
            var max = this.Max;
            List<float> newData = new List<float>(this._audioData);
            // head
            var headIndex = 0;
            for (int i = 0; i < this._audioData.Length; i++)
            {
                if (Math.Abs(this._audioData[i]) >= max * cutRatio)
                {
                    headIndex = i;
                    break;
                }
            }

            var tailIndex = this._audioData.Length - 1;
            for (int i = tailIndex; i >= 0; i--)
            {
                if (Math.Abs(this._audioData[i]) >= max * cutRatio)
                {
                    tailIndex = i;
                    break;
                }
            }

            if (headIndex < tailIndex)
            {
                newData = newData.GetRange(headIndex, tailIndex - headIndex + 1);
                this._audioData = newData.ToArray();
                return;
            }

            this._audioData = new float[] { };
        }

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
        
        public float this[int index]
        {
            get
            {
                float res;
                if (this._amplification > 0)
                {
                    res = this.AudioData[index] * (1000 + this._amplification * 10) / 1000;
                } else if (this._amplification < 0)
                {
                    res = this.AudioData[index] * (1000 + this._amplification) / 1000;
                }
                else
                {
                    res = this.AudioData[index];
                }

                res *= (1 + 
                        Tension * MathF.Sin(
                            (float)index / 
                            this.WaveFormat.SampleRate / 
                            this.WaveFormat.Channels * 
                            Config.Instance.DynamicVolumePerturbationFrequency * 2 * MathF.PI
                            )
                        );
                
                return res;
            }
        }
    }
}