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

        public int Read(float[] buffer, int offset, int count)
        {
            if (playbackRate == 0) // play silence
            {
                for (int n = 0; n < count; n++)
                {
                    buffer[offset++] = 0;
                }

                return count;
            }

            if (repositionRequested)
            {
                currentSoundTouchProcessor.Clear();
                repositionRequested = false;
            }

            int samplesRead = 0;
            bool reachedEndOfSource = false;
            while (samplesRead < count)
            {
                if (currentSoundTouchProcessor.AvailableSamples == 0)
                {
                    var readFromSource = sourceProvider.Read(sourceReadBuffer, 0, sourceReadBuffer.Length);
                    if (readFromSource > 0)
                    {
                        currentSoundTouchProcessor.PutSamples(sourceReadBuffer, readFromSource / channelCount);
                    }
                    else
                    {
                        reachedEndOfSource = true;
                        // we've reached the end, tell SoundTouch we're done
                        currentSoundTouchProcessor.Flush();
                        break; // samplesRead instead of 0 to keep the integrity
                    }
                }

                var desiredSampleFrames = (count - samplesRead) / channelCount;

                var received = currentSoundTouchProcessor.ReceiveSamples(soundTouchReadBuffer, desiredSampleFrames) *
                               channelCount;
                // use loop instead of Array.Copy due to WaveBuffer
                for (int n = 0; n < received; n++)
                {
                    buffer[offset + samplesRead++] = soundTouchReadBuffer[n];
                }

                if (received == 0 && reachedEndOfSource) break;
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
