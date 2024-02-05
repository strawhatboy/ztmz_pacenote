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

namespace ZTMZ.PacenoteTool.Base.UI
{
    public class VRGameOverlayManager
    {
        private NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
        private BackgroundWorker _bgw;
        private bool _isRunning;
        private bool _isNeedReload = false;
        private DeviceManager _deviceManager = null;
        private Direct3D11CaptureSource _captureSource = null;
        private VROverlayWindow _vrOverlayWindow = null;

        public void initliazeOverlay()
        {
            _deviceManager = new DeviceManager(OpenVR.System);
            _captureSource = new Direct3D11CaptureSource(_deviceManager, OpenVR.System);

            this.initliazeOverlayWindow();
        }

        private void initliazeOverlayWindow()
        {
            List<IntPtr> windows = new List<IntPtr>();
            windows.AddRange(Win32Stuff.FindWindows());
            foreach (var wnd in windows)
            {
                string windowName = Win32Stuff.GetWindowText(wnd);
                if (!string.IsNullOrWhiteSpace(windowName) && windowName == Config.Instance.VrOverlayWindowName)
                {
                    _vrOverlayWindow = new VROverlayWindow(windowName, wnd, enabled: true, isDisplay: false, wasEnabled: true);
                    UpdateOverlayWindow();
                    break;
                }
            }
        }

        private void ResetOverlayWindow()
        {
            _vrOverlayWindow.enabled = false;
            _vrOverlayWindow.Dispose();
            _vrOverlayWindow = null;
            this.initliazeOverlayWindow();
        }

        public void UpdateOverlayWindow()
        {
            if (_vrOverlayWindow != null)
            {
                if (this._vrOverlayWindow.Name != Config.Instance.VrOverlayWindowName)
                {
                    _isNeedReload = true;
                }

                _vrOverlayWindow.positionX = Config.Instance.VrOverlayPositionX * 0.01f;
                _vrOverlayWindow.positionY = Config.Instance.VrOverlayPositionY * 0.01f;
                _vrOverlayWindow.positionZ = Config.Instance.VrOverlayPositionZ * 0.01f;
                _vrOverlayWindow.rotationX = Config.Instance.VrOverlayRotationX * 0.01f;
                _vrOverlayWindow.rotationY = Config.Instance.VrOverlayRotationY * 0.01f;
                _vrOverlayWindow.rotationZ = Config.Instance.VrOverlayRotationZ * 0.01f;
                _vrOverlayWindow.scale = Config.Instance.VrOverlayScale * 0.01f;
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
            if (!this.isSteamVrRunning())
            {
                _logger.Info("SteamVR is not running, please check and restart application.");
                return;
            }

            _isRunning = true;
            _bgw = new BackgroundWorker();

            this.initliazeOverlay();
            _bgw.DoWork += (sender, e) =>
            {
                uint vrEventSize = (uint)SharpDX.Utilities.SizeOf<VREvent_t>();
                while (_isRunning)
                {
                    var vrEvent = new VREvent_t();
                    while (OpenVR.System != null && OpenVR.System.PollNextEvent(ref vrEvent, vrEventSize))
                    {
                        switch ((EVREventType)vrEvent.eventType)
                        {
                            case EVREventType.VREvent_Quit:
                                {
                                    this.handleVRQuit();
                                    break;
                                }
                            default:
                                break;
                        }
                    }

                    if (_isNeedReload)
                    {
                        _isNeedReload = false;
                        ResetOverlayWindow();
                    }

                    if (OpenVR.System != null)
                    {
                        TrackedDevices.UpdatePoses();
                        TrackedDevices.GetHeadPose(out SharpDX.Matrix hmdMatrix, out _, out _);

                        //FIXME: just support overlay one window.
                        VROverlayWindow[] currentItem = new VROverlayWindow[1];
                        if (_vrOverlayWindow != null)
                        {
                            currentItem[0] = _vrOverlayWindow;
                        }

                        var windowBatch = currentItem.Where(wnd => wnd.enabled).ToList();
                        _captureSource.Capture(windowBatch);
                        foreach (var wnd in windowBatch)
                        {
                            wnd.hmdMatrix = hmdMatrix;
                            wnd.Draw();
                        }
                    }
                    Thread.Sleep(10);
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
