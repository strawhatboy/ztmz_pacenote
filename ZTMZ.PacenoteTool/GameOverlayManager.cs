using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Drawing;

namespace ZTMZ.PacenoteTool
{
    public class GameOverlayManager
    {
        public static string GAME_PROCESS = "Notepad";

        public void InitializeOverlay(System.Diagnostics.Process process)
        {
            
        }

        public void StartLoop()
        {
            BackgroundWorker bgw = new BackgroundWorker();
            bgw.DoWork += (sender, args) =>
            {
                while (true)
                {
                    // 1. find process   
                    var processes = System.Diagnostics.Process.GetProcessesByName(GAME_PROCESS);
                    if (processes.Length > 0)
                    {
                        var process = processes.First();
                        this.InitializeOverlay(process);
                    }

                    Thread.Sleep(5000);
                }
            };
            bgw.RunWorkerAsync();
        }
    }
    
}