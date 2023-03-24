using SharpDX;
using SharpDX.DirectInput;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ZTMZ.PacenoteTool.Base;
using VRGameOverlay.VROverlayWindow;

namespace ZTMZ.PacenoteTool
{
    public class VRGameOverlayManager
    {
        private BackgroundWorker _bgw;
        private bool _isRunning;
        private DeviceManager _deviceManager = null;
        private Direct3D11CaptureSource _captureSource = null;
        private VROverlayWindow _vrOverlayWindow = null;

        public void initliazeOverlay()
        {
            _deviceManager = new DeviceManager(OpenVR.System);
            _captureSource = new Direct3D11CaptureSource(_deviceManager, OpenVR.System);
            
            List<IntPtr> windows = new List<IntPtr>();
            windows.AddRange(Win32Stuff.FindWindows());
            foreach (var wnd in windows)
            {
                string windowName = Win32Stuff.GetWindowText(wnd);
                if (!string.IsNullOrWhiteSpace(windowName) && windowName == Config.Instance.VrOverlayWindowName)
                {
                    _vrOverlayWindow = new VROverlayWindow(windowName, wnd, enabled: false, isDisplay: false);
                    break;
                }
            }
        }

        private bool isSteamVrRunning()
        {
           return Win32Stuff.FindWindowsWithText("SteamVR").FirstOrDefault() != IntPtr.Zero;
        }

        private void waitForSteamVR()
        {
           while (!this.isSteamVrRunning())
           {
               Thread.Sleep(1000);
           }
        }

        public void StartLoop()
        {
            _isRunning = true;
            _bgw = new BackgroundWorker();
            this.initliazeOverlay();
            _bgw.DoWork += (sender, args) =>
            {
                while (_isRunning)
                {
                    TrackedDevices.UpdatePoses();
                    TrackedDevices.GetHeadPose(out SharpDX.Matrix hmdMatrix, out _, out _);

                    VROverlayWindow[] currentItem = null;
                    if (_vrOverlayWindow != null)
                    {
                        currentItem.Append<VROverlayWindow>(_vrOverlayWindow);
                    }
                    
                    var windowBatch = currentItem.Where(wnd => wnd.enabled).ToList();
                    _captureSource.Capture(windowBatch);
                    foreach (var wnd in windowBatch)
                    {
                        wnd.hmdMatrix = hmdMatrix;
                        wnd.Draw();
                    }
                }
            };
            _bgw.RunWorkerAsync();
        }

        public void StopLoop()
        {
            _bgw?.Dispose();
            _bgw = null;
            _isRunning = false;
        }

        private void handleVRQuit()
        {
            OpenVR.System?.AcknowledgeQuit_Exiting();

            _captureSource?.Dispose();
            _captureSource = null;

            _deviceManager?.Dispose();
            _deviceManager = null;

            SteamVR.SafeDispose();
        }
    }
}
