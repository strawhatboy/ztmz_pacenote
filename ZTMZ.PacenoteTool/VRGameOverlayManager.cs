using SharpDX.DirectInput;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VRGameOverlay.VROverlayWindow;

namespace ZTMZ.PacenoteTool
{
    public class VRGameOverlayManager
    {
        private BackgroundWorker _bgw;
        private bool _isRunning;
        private DeviceManager _deviceManager = null;
        private Direct3D11CaptureSource _captureSource = null;

        public void initliazeOverlay()
        {
            _deviceManager = new DeviceManager(OpenVR.System);
            _captureSource = new Direct3D11CaptureSource(_deviceManager, OpenVR.System);
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
    }
}
