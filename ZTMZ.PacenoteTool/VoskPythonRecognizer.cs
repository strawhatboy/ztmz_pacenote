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
            this.PythonProcess = new Process();
            var startInfo = new ProcessStartInfo(
                "Python38/python.exe",
                string.Format("-i speech_recognizer.py --framerate={0}", framerate)
            );
            startInfo.UseShellExecute = false;
            startInfo.RedirectStandardInput = true;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.CreateNoWindow = true;
            startInfo.WindowStyle = ProcessWindowStyle.Hidden;
            this.PythonProcess.StartInfo = startInfo;
            //BackgroundWorker bgw = new BackgroundWorker();
            this.PythonProcess.OutputDataReceived += (sender, args) =>
            {
                this.Recognized?.Invoke(args.Data);
            };
            this.PythonProcess.Start();
            this.PythonProcess.BeginOutputReadLine();
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
