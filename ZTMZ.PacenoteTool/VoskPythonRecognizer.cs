using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ZTMZ.PacenoteTool
{
    class VoskPythonRecognizer
    {
        public Process PythonProcess { set; get; }

        public event Action<string> Recognized;

        public void Start(int framerate=48000)
        {
            this.PythonProcess = Process.Start(new ProcessStartInfo("python3.8/python.exe", string.Format("speech_recognizer.py --framerate={0}", framerate)));
            //BackgroundWorker bgw = new BackgroundWorker();
            this.PythonProcess.OutputDataReceived += (sender, args) =>
            {
                this.Recognized?.Invoke(args.Data);
            };
        }

        public void Stop()
        {
            this.PythonProcess.Kill();
        }

        public void Recognize(int distance, string filepath)
        {
            this.PythonProcess.StandardInput.WriteLine(string.Format("{0}:{1}", distance, filepath));
        }
    }
}
