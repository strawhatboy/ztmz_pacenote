using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool
{
    class VoskPythonRecognizer
    {
        public Process PythonProcess { set; get; }

        public event Action<string> Recognized;

        public void Start(int framerate = 48000, bool autoclean = false, string modelpath = "speech_model")
        {
            this.PythonProcess = new Process();
            var startInfo = new ProcessStartInfo(
                string.Format("{0}/python.exe", Config.Instance.PythonPath),
                string.Format("-i speech_recognizer.py --framerate={0} --modelpath={1} {2}", framerate, modelpath, autoclean ? "--autoclean" : "")
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
