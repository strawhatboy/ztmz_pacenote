using System;
using NAudio.Dsp;
using NAudio.Wave;
using SoundTouch;

namespace ZTMZ.PacenoteTool.Base
{

    public class VariSpeedSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider sourceProvider;
        private readonly float[] sourceReadBuffer;
        private readonly float[] soundTouchReadBuffer;
        private readonly int channelCount;
        private float playbackRate = 1.0f;
        private SoundTouchProcessor currentSoundTouchProcessor;
        private bool repositionRequested;
        private bool sourceEnded;

        public VariSpeedSampleProvider(ISampleProvider sourceProvider, int readDurationMilliseconds,
            float playbackRate = 1.0f, bool useTempo = true)
        {
            currentSoundTouchProcessor = new SoundTouchProcessor();
            // explore what the default values are before we change them:
            //Debug.WriteLine(String.Format("SoundTouch Version {0}", soundTouch.VersionString));
            //Debug.WriteLine("Use QuickSeek: {0}", soundTouch.GetUseQuickSeek());
            //Debug.WriteLine("Use AntiAliasing: {0}", soundTouch.GetUseAntiAliasing());

            this.sourceProvider = sourceProvider;
            currentSoundTouchProcessor.SampleRate = WaveFormat.SampleRate;
            channelCount = WaveFormat.Channels;
            currentSoundTouchProcessor.Channels = channelCount;
            sourceReadBuffer =
                new float[(WaveFormat.SampleRate * channelCount * (long)readDurationMilliseconds) / 1000];
            soundTouchReadBuffer = new float[sourceReadBuffer.Length * 10]; // support down to 0.1 speed
            this.UseTempo = useTempo;
            this.PlaybackRate = playbackRate;
        }

        public int Read(Span<float> buffer)
        {
            if (playbackRate == 0) // play silence
            {
                buffer.Clear();
                return buffer.Length;
            }

            if (repositionRequested)
            {
                currentSoundTouchProcessor.Clear();
                repositionRequested = false;
                sourceEnded = false;
            }

            int samplesRead = 0;
            while (samplesRead < buffer.Length)
            {
                var desiredSampleFrames = (buffer.Length - samplesRead) / channelCount;
                if (desiredSampleFrames == 0)
                {
                    break;
                }

                var received = currentSoundTouchProcessor.ReceiveSamples(soundTouchReadBuffer, desiredSampleFrames) *
                               channelCount;
                if (received > 0)
                {
                    soundTouchReadBuffer.AsSpan(0, received).CopyTo(buffer[samplesRead..]);
                    samplesRead += received;
                    continue;
                }

                if (sourceEnded)
                {
                    break;
                }

                var readFromSource = sourceProvider.Read(sourceReadBuffer);
                if (readFromSource > 0)
                {
                    currentSoundTouchProcessor.PutSamples(sourceReadBuffer, readFromSource / channelCount);
                }
                else
                {
                    sourceEnded = true;
                    currentSoundTouchProcessor.Flush();
                }
            }

            return samplesRead;
        }

        public WaveFormat WaveFormat => sourceProvider.WaveFormat;

        public float PlaybackRate
        {
            get { return playbackRate; }
            set
            {
                if (playbackRate != value)
                {
                    UpdatePlaybackRate(value);
                    playbackRate = value;
                }
            }
        }

        private void UpdatePlaybackRate(float value)
        {
            if (value != 0)
            {
                if (UseTempo)
                {
                    currentSoundTouchProcessor.Tempo = value;
                }
                else
                {
                    currentSoundTouchProcessor.Rate = value;
                }
            }
        }


        public bool UseTempo { set; get; } = true;



        public void Reposition()
        {
            repositionRequested = true;
        }

    }
}
