using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.Compression;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Vosk;

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

        private HackedWasapiLoopbackCapture _capture = new HackedWasapiLoopbackCapture();
        public bool IsRecognizing { set; get; }

        public int Patience { set; get; } = 5;
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

        private void AutoRecorder_PieceRecored(Tuple<int, string> obj)
        {
            BackgroundWorker bgw = new BackgroundWorker();
            bgw.DoWork += (e, args) =>
            {
                var newFile = ConvertToPCM(obj.Item2);
                var fileName = "tmp/" + Path.GetFileName(newFile) + ".wav";
                StereoToMono(newFile, fileName);
                File.Delete(newFile);
                this.PreprocessPieces.Enqueue(new Tuple<int, string>(obj.Item1, fileName));
            };
            bgw.RunWorkerAsync();
        }

        private void InitSoundCapture()
        {
            //_capture = new WasapiLoopbackCapture(WasapiLoopbackCapture.GetDefaultLoopbackCaptureDevice());
            _capture = new HackedWasapiLoopbackCapture();
            _capture.WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
            var outputFilePath = Path.GetTempFileName();
            // WaveFileWriter writer = new WaveFileWriter(outputFilePath, _capture.WaveFormat);
            WaveFileWriter writer = null;
            int patience = Patience;
            bool isTalking = false;
            _capture.DataAvailable += (s, a) =>
            {
                //var pcmBytes = ToPCM16(a.Buffer, a.BytesRecorded, capture.WaveFormat);
                //if (Math.Abs(a.Buffer[0]- pre) > 10)
                //{
                //    pre = a.Buffer[0];
                //    Console.Write(string.Format(".{0}", pre.ToString()));
                //}
                if (a.Buffer[0] != 0 && a.Buffer[0] != 255 && a.Buffer[0] != 254 && a.Buffer[0] != 63)
                {
                    Debug.Write(string.Format(".{0}", a.Buffer[0].ToString()));
                    if (!isTalking)
                    {
                        isTalking = true;
                        outputFilePath = Path.GetTempFileName();
                        writer = new WaveFileWriter(outputFilePath, _capture.WaveFormat);
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
                            writer.Flush();
                            writer.Dispose();
                            writer = null;
                            patience--;
                            this.PieceRecored?.Invoke(Pieces.Dequeue());
                        }
                        else if (patience > 0)
                        {
                            patience--;
                        }
                    }
                }
                if (writer != null)
                    writer.Write(a.Buffer, 0, a.BytesRecorded);
            };
            _capture.RecordingStopped += (s, a) =>
            {
                writer.Dispose();
                writer = null;
                _capture.Dispose();
            };
            //_capture.WaveFormat.
            _capture.StartRecording();
            //while (_capture.CaptureState != NAudio.CoreAudioApi.CaptureState.Stopped)
            //{
            //    Thread.Sleep(500);
            //}
        }

        public void InitRecognizer()
        {
            BackgroundWorker bgw = new BackgroundWorker();
            bgw.DoWork += (o, args) =>
            {
                while (this.IsRecognizing)
                {
                    Thread.Sleep(1000);
                    if (this.PreprocessPieces.Count > 0)
                    {
                        var piece = this.PreprocessPieces.Dequeue();
                        VoskRecognizer rec = new VoskRecognizer(Model, 16000.0f);
                        //rec.SetMaxAlternatives(0);
                        //rec.SetWords(true);
                        using (Stream source = File.OpenRead(piece.Item2))
                        {
                            byte[] buffer = new byte[4096];
                            int bytesRead;
                            while ((bytesRead = source.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                if (rec.AcceptWaveform(buffer, bytesRead))
                                {
                                    Debug.WriteLine(rec.Result());
                                }
                                else
                                {
                                    Debug.WriteLine(rec.PartialResult());
                                }
                            }
                        }
                        Debug.WriteLine(rec.FinalResult());
                        this.PieceRecognized?.Invoke(new Tuple<int, string>(piece.Item1, rec.FinalResult()));

                        // clean the file.
                        // File.Delete(piece.Item2);
                    }
                }
            };
            bgw.RunWorkerAsync();
        }

        public void StopSoundCapture()
        {
            _capture?.StopRecording();
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
    }
}
