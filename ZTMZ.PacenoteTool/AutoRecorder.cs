using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.Compression;
using OnlyR.Core.EventArgs;
using OnlyR.Core.Samples;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Vosk;
using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool
{
    public class AutoRecorder
    {
        public static string MODEL_PATH = "speech_model";
        public Model Model { get; private set; }

        public int Distance { set; get; }
        public Queue<Tuple<int, string>> Pieces { get; } = new Queue<Tuple<int, string>>();
        public Queue<Tuple<int, string>> PreprocessPieces { get; } = new Queue<Tuple<int, string>>();

        public event Action<Tuple<int, string>> PieceRecored;
        public event Action<Tuple<int, string>> PieceRecognized;
        public event Action Initialized;

        private object preprocessed_lock = new object();

        private HackedWasapiLoopbackCapture _capture = new HackedWasapiLoopbackCapture();

        private VoskPythonRecognizer recognizer = new VoskPythonRecognizer();
        public bool IsRecognizing { set; get; }

        public int Patience { set; get; } = 5;

        private int _dampedLevel;
        private const int RequiredReportingIntervalMs = 40;
        private const int VuSpeed = 5;
        private SampleAggregator? _sampleAggregator;
        private WaveFileWriter _writer = null;


        int patience = 5;
        bool isTalking = false;
        string outputFilePath = Path.GetTempFileName();
        public void Initialize()
        {
            // 1. check if model exists
            if (!Directory.Exists(MODEL_PATH))
            {
                throw new Exception("语音识别模型不存在，请使用开发版本或下载并解压模型到speech_model目录下");
            }


            // 2. listen to loopback sound
            this.PieceRecored += AutoRecorder_PieceRecored;
            this.Model = new Model(MODEL_PATH);

            Directory.CreateDirectory("tmp");
            //BackgroundWorker bgw = new BackgroundWorker();
            //bgw.DoWork += (e, args) => 
            //bgw.RunWorkerAsync();
            this.InitSoundCapture();
            // 3. recognize
            IsRecognizing = true;
            this.InitRecognizer();
            this.Initialized?.Invoke();
        }

        public void Uninitialize()
        {
            this.StopSoundCapture();
            this.StopRecognizer();
        }

        private void AutoRecorder_PieceRecored(Tuple<int, string> obj)
        {
            BackgroundWorker bgw = new BackgroundWorker();
            bgw.DoWork += (e, args) =>
            {
                var newFile = ConvertToPCM(obj.Item2);
                var fileName = "tmp/" + Path.GetFileName(newFile) + ".wav";
                StereoToMono(newFile, fileName);
                File.Delete(newFile);
                lock(preprocessed_lock)
                {
                    this.PreprocessPieces.Enqueue(new Tuple<int, string>(obj.Item1, fileName));
                }
            };
            bgw.RunWorkerAsync();
        }

        private void InitSoundCapture()
        {
            _capture = new HackedWasapiLoopbackCapture();

            InitAggregator(_capture.WaveFormat.SampleRate);
            _capture.DataAvailable += this.CaptureDataAvailable;
            _capture.RecordingStopped += this.CaptureRecordingStopped;
            try
            {
                _capture.StartRecording();
            } catch (COMException e)
            {
                _capture.Dispose();
                _capture = new HackedWasapiLoopbackCapture();
                _capture.WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(Config.Instance.LoopbackCaptureSampleRate, Config.Instance.LoopbackCaptureChannels); 
                InitAggregator(_capture.WaveFormat.SampleRate);
                _capture.DataAvailable += this.CaptureDataAvailable;
                _capture.RecordingStopped += this.CaptureRecordingStopped;
                _capture.StartRecording();
            }
            //while (_capture.CaptureState != NAudio.CoreAudioApi.CaptureState.Stopped)
            //{
            //    Thread.Sleep(500);
            //}
        }

        private void CaptureDataAvailable(object? s, WaveInEventArgs a)
        {
            var isFloatingPointAudio = _capture?.WaveFormat.BitsPerSample == 32;

            AddToSampleAggregator(a.Buffer, a.BytesRecorded, isFloatingPointAudio);

            if (_writer != null)
                _writer.Write(a.Buffer, 0, a.BytesRecorded);
        }

        private void CaptureRecordingStopped(object? sender, StoppedEventArgs e)
        {
            _writer.Dispose();
            _writer = null;
            _capture.Dispose();
        }

        public void InitRecognizer()
        {
            recognizer.Start();
            recognizer.Recognized += s =>
            {
                var parts = s.Split('>');
                if (parts.Length == 2)
                {
                    var dis = int.Parse(parts.First());
                    this.PieceRecognized?.Invoke(new Tuple<int, string>(dis, s));
                }
            };
            BackgroundWorker bgw = new BackgroundWorker();
            bgw.DoWork += (o, args) =>
            {
                while (this.IsRecognizing)
                {
                    Thread.Sleep(1000);
                    lock (preprocessed_lock)
                    {
                        if (this.PreprocessPieces.Count > 0)
                        {
                            var piece = this.PreprocessPieces.Dequeue();
                            recognizer.Recognize(piece.Item1, piece.Item2);
                            // clean the file.
                            // File.Delete(piece.Item2);
                        }
                    }
                }
            };
            bgw.RunWorkerAsync();
        }

        public void StopSoundCapture()
        {
            _capture?.StopRecording();
        }

        public void StopRecognizer()
        {
            BackgroundWorker bgw = new BackgroundWorker();
            bgw.DoWork += (o, args) =>
            {
                while (this.IsRecognizing)
                {
                    Thread.Sleep(1000);
                    lock (preprocessed_lock)
                    {
                        if (this.PreprocessPieces.Count == 0)
                        {
                            break;
                        }
                    }
                }
                this.IsRecognizing = false;
                Thread.Sleep(10000);
                recognizer.Stop();
            };
            bgw.RunWorkerAsync();
        }

        public string ConvertToPCM(string filePath)
        {
            var outPath = Path.GetTempFileName();
            using (var reader = new MediaFoundationReader(filePath))
            {
                using (var conversionStream = CreatePcmStream(reader))
                {
                    WaveFileWriter.CreateWaveFile(outPath, conversionStream);
                }
            }
            return outPath;
        }
        public static WaveStream CreatePcmStream(WaveStream sourceStream)
        {
            if (sourceStream.WaveFormat.Encoding == WaveFormatEncoding.Pcm)
            {
                return sourceStream;
            }
            WaveFormat pcmFormat = AcmStream.SuggestPcmFormat(sourceStream.WaveFormat);
            return new WaveFormatConversionStream(pcmFormat, sourceStream);
        }
        public static void StereoToMono(string sourceFile, string outputFile)
        {
            using (var waveFileReader = new WaveFileReader(sourceFile))
            {
                var outFormat = new WaveFormat(waveFileReader.WaveFormat.SampleRate, 1);
                using (var resampler = new MediaFoundationResampler(waveFileReader, outFormat))
                {
                    WaveFileWriter.CreateWaveFile(outputFile, resampler);
                }
            }
        }

        private void InitAggregator(int sampleRate)
        {
            // the aggregator collects audio sample metrics 
            // and publishes the results at suitable intervals.
            // Used by the OnlyR volume meter
            if (_sampleAggregator != null)
            {
                _sampleAggregator.ReportEvent -= AggregatorReportHandler;
            }

            _sampleAggregator = new SampleAggregator(sampleRate, RequiredReportingIntervalMs);
            _sampleAggregator.ReportEvent += AggregatorReportHandler;
        }

        private int GetDampedVolumeLevel(float volLevel)
        {
            // provide some "damping" of the volume meter.
            if (volLevel > _dampedLevel)
            {
                _dampedLevel = (int)(volLevel + VuSpeed);
            }

            _dampedLevel -= VuSpeed;
            if (_dampedLevel < 0)
            {
                _dampedLevel = 0;
            }

            return _dampedLevel;
        }

        private void AddToSampleAggregator(byte[] buffer, int bytesRecorded, bool isFloatingPointAudio)
        {
            var buff = new WaveBuffer(buffer);

            if (isFloatingPointAudio)
            {
                for (var index = 0; index < bytesRecorded / 4; ++index)
                {
                    var sample = buff.FloatBuffer[index];
                    _sampleAggregator?.Add(sample);
                }
            }
            else
            {
                for (var index = 0; index < bytesRecorded / 2; ++index)
                {
                    var sample = buff.ShortBuffer[index];
                    _sampleAggregator?.Add(sample / 32768F);
                }
            }
        }
        private void AggregatorReportHandler(object? sender, SamplesReportEventArgs e)
        {
            var value = Math.Max(e.MaxSample, Math.Abs(e.MinSample)) * 100;

            var damped = GetDampedVolumeLevel(value);
            if (damped != 0)
            {
                Debug.Write(string.Format(".{0}", damped));
            }
            if (damped > 50)
            {
                if (!isTalking)
                {
                    isTalking = true;
                    outputFilePath = Path.GetTempFileName();
                    _writer = new WaveFileWriter(outputFilePath, _capture.WaveFormat);
                    Pieces.Enqueue(new Tuple<int, string>(Distance, outputFilePath));
                }
                patience = Patience;
            } else
            {
                if (isTalking)
                {
                    if (patience == 0)
                    {
                        isTalking = false;
                        // output file
                        _writer.Flush();
                        _writer.Dispose();
                        _writer = null;
                        patience--;
                        this.PieceRecored?.Invoke(Pieces.Dequeue());
                    }
                    else if (patience > 0)
                    {
                        patience--;
                    }
                }
            }
            //OnProgressEvent(new RecordingProgressEventArgs { VolumeLevelAsPercentage = damped });
        }
    }
}
