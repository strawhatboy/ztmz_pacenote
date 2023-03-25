using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Newtonsoft.Json;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.Mathematics.Interop;

/// <summary>
/// This class stores settings for each Target Application
/// </summary>
/// 
namespace VRGameOverlay.VROverlayWindow
{
    public enum ClickAPI
    {
        None = 0,
        SendInput = 1,
        SendMessage = 2,
        SendNotifyMessage = 3,
    }
    public enum CaptureMode
    {
        GdiDirect = 0,
        GdiIndirect = 1,
        ReplicationApi = 2,
    }

    public enum MouseInteractionMode
    {
        DirectInteraction = 0, // Keep Window on top, Move Cursor
        WindowTop = 1, // Keep Window on top only, Send Mouse Clicks Only (No Move)
        SendClicksOnly = 2, // Only Send Mouse Clicks
        Disabled = 3
    }
    public enum TrackingSpace
    {
        Seated = 0,
        Standing,
        FollowHead
    }

    [Serializable]
    public class VROverlayWindow
    {
        public string Name { get; set; }
        public string Text { get; set; }
        [JsonIgnore]
        public IntPtr hWnd { get; set; }
        public bool enabled { get; set; }
        public bool wasEnabled { get; set; }
        public float positionX { get; set; }
        public float positionY { get; set; }
        public float positionZ { get; set; }
        public float rotationX { get; set; }
        public float rotationY { get; set; }
        public float rotationZ { get; set; }
        public float scale { get; set; }
        public float gazeScale { get; set; }
        public float transparency { get; set; }
        public float gazeTransparency { get; set; }
        public float curvature { get; set; }
        public bool gazeEnabled { get; set; }
        public bool forceTopMost { get; set; }
        [DefaultValue(TrackingSpace.Seated)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public TrackingSpace trackingSpace { get; set; }
        public bool isDisplay { get; set; }
        [DefaultValue(-1)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public int toggleVKeyCode { get; set; }
        [DefaultValue(-1)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public int modifierVKeyCode { get; set; }
        [JsonIgnore]
        public Texture2D copiedScreenTexture { get; set; }
        [JsonIgnore]
        public Rectangle rectAbs;
        [JsonIgnore]
        public Rectangle rectScreen;
        [JsonIgnore]
        public ulong vrOverlayHandle;
        [JsonIgnore]
        public bool shouldDraw { get; set; }
        [JsonIgnore]
        public float aspect { get; set; }
        [JsonIgnore]
        public Matrix hmdMatrix { get; set; }


        [JsonIgnore]
        public bool IsSelected { get; set; }

        public bool Chromakey { get; set; }
        public string ChromakeyColor { get; set; } = "#000000";
        public float ChromakeyTolerance { get; set; }

        private bool wasGazing = false;
        private long lastKeyHandleTickCount = 0;

        public VROverlayWindow()
        {
            positionZ = -1;
        }

        //[JsonConstructor]
        public VROverlayWindow(string Text, IntPtr hWnd, string Name = "", bool enabled = false, bool wasEnabled = false, float positionX = 0, float positionY = 0, float positionZ = -1,
            float rotationX = 0, float rotationY = 0, float rotationZ = 0, float scale = 1, float transparency = 1, float curvature = 0, TrackingSpace trackingUniverse = TrackingSpace.Seated, bool isDisplay = false, int toggleVKeyCode = -1, int modifierVKeyCode = -1, bool gazeEnabled = false, float gazeScale = 1f, float gazeTransparency = 1f, bool forceTopMost = false)
        {
            this.Text = Text;
            if (string.IsNullOrWhiteSpace(Name))
                this.Name = Text;
            else
                this.Name = Name;
            this.enabled = enabled;
            this.hWnd = hWnd;
            this.positionX = positionX;
            this.positionY = positionY;
            this.positionZ = positionZ; // place initial overlay 1 meter infront of the user
            this.rotationX = rotationX;
            this.rotationY = rotationY;
            this.rotationY = rotationZ;
            this.scale = scale;
            this.gazeScale = gazeScale;
            this.transparency = transparency;
            this.gazeTransparency = gazeTransparency;
            trackingSpace = trackingUniverse;
            this.curvature = curvature;
            this.isDisplay = isDisplay;
            this.toggleVKeyCode = toggleVKeyCode;
            this.modifierVKeyCode = modifierVKeyCode;
            this.gazeEnabled = gazeEnabled;
            this.wasEnabled = wasEnabled;
            rectAbs = new Rectangle();
            rectScreen = new Rectangle();
            this.forceTopMost = forceTopMost;
            shouldDraw = false;
            if (enabled)
            {
                CreateOverlay();
                SetOverlayCurvature();
                SetOverlayTransparency();
                SetOverlayEnabled(true);
            }
        }

        /// <summary>
        /// Update the dimensions of the window
        /// If the dimiensions have changed then the current texture will be dispsed and a new Texture will be created 
        /// </summary>
        /// <param name="rect"></param>
        /// <param name="getTexture">factory method to create a texture</param>
        /// <returns></returns>
        internal bool TryUpdateSize(Rectangle rect, Func<Texture2D> getTexture)
        {
            bool result = true;
            if ((rect.Width != rectAbs.Width || rect.Height != rectAbs.Height) && rect.Width > 0 && rect.Height > 0)
            {
                rectAbs = rect;
                try
                {
                    copiedScreenTexture?.Dispose();
                    copiedScreenTexture = null;
                    copiedScreenTexture = getTexture();
                }
                catch (SharpDXException e)
                {
                    Console.WriteLine("CaptureScreen.Capture: Screen capturing failed = " + e.Message);
                    result = false;
                }
            }
            rectAbs = rect;
            aspect = Math.Abs(rectAbs.Height / (float)rectAbs.Width);
            return result;
        }

        public VROverlayWindow(IntPtr hWnd, VROverlayWindow other, ulong vrOverlayCursorHandle = 0)
        {
            Text = other.Text;
            Name = other.Name;
            enabled = other.enabled;
            this.hWnd = hWnd;
            positionX = other.positionX;
            positionY = other.positionY;
            positionZ = other.positionZ; // place initial overlay 1 meter infront of the user
            rotationX = other.rotationX;
            rotationY = other.rotationY;
            rotationZ = other.rotationZ;
            scale = other.scale;
            gazeScale = other.gazeScale;
            transparency = other.transparency;
            gazeTransparency = other.gazeTransparency;
            trackingSpace = other.trackingSpace;
            curvature = other.curvature;
            isDisplay = other.isDisplay;
            toggleVKeyCode = other.toggleVKeyCode;
            modifierVKeyCode = other.modifierVKeyCode;
            gazeEnabled = other.gazeEnabled;
            wasEnabled = other.wasEnabled;
            rectAbs = new Rectangle();
            rectScreen = new Rectangle();
            forceTopMost = other.forceTopMost;
            lastKeyHandleTickCount = other.lastKeyHandleTickCount;
            Chromakey = other.Chromakey;
            ChromakeyColor = other.ChromakeyColor;
            ChromakeyTolerance = other.ChromakeyTolerance;
            shouldDraw = false;
            if (enabled)
            {
                CreateOverlay();
                SetOverlayCurvature();
                SetOverlayTransparency();
                SetOverlayEnabled(true);
            }
        }

        public void Copy(VROverlayWindow currWnd)
        {
            enabled = currWnd.enabled;
            wasEnabled = currWnd.wasEnabled;
            positionX = currWnd.positionX;
            positionY = currWnd.positionY;
            positionZ = currWnd.positionZ;
            rotationX = currWnd.rotationX;
            rotationY = currWnd.rotationY;
            rotationZ = currWnd.rotationZ;
            scale = currWnd.scale;
            gazeScale = currWnd.gazeScale;
            transparency = currWnd.transparency;
            gazeTransparency = currWnd.gazeTransparency;
            trackingSpace = currWnd.trackingSpace;
            curvature = currWnd.curvature;
            gazeEnabled = currWnd.gazeEnabled;
            forceTopMost = currWnd.forceTopMost;
            isDisplay = currWnd.isDisplay;
            toggleVKeyCode = currWnd.toggleVKeyCode;
            modifierVKeyCode = currWnd.modifierVKeyCode;
            Chromakey = currWnd.Chromakey;
            ChromakeyColor = currWnd.ChromakeyColor;
            ChromakeyTolerance = currWnd.ChromakeyTolerance;
        }

        public void CreateOverlay(bool setFlags = true)
        {
            if (string.IsNullOrWhiteSpace(Name))
                Name = Guid.NewGuid().ToString();
            vrOverlayHandle = 0;

            var error = SteamVR.instance.overlay.CreateOverlay(Name, Name, ref vrOverlayHandle);
            if (error == EVROverlayError.None)
            {
                if (setFlags)
                {
                    SteamVR.instance.overlay.SetOverlayFlag(vrOverlayHandle, VROverlayFlags.SortWithNonSceneOverlays, true);
                    // disable mouse input for not as it ned a propper handler
                    //SteamVR.instance.overlay.SetOverlayFlag(vrOverlayHandle, VROverlayFlags.MakeOverlaysInteractiveIfVisible, true);
                    //SteamVR.instance.overlay.SetOverlayFlag(vrOverlayHandle, VROverlayFlags.HideLaserIntersection, true);

                    //SteamVR.instance.overlay.SetOverlayInputMethod(vrOverlayHandle, VROverlayInputMethod.Mouse);


                }
                Console.WriteLine($"Created SVR overlay handle for: {Name} : 0x{vrOverlayHandle:X8}");
            }
            else
            {
                Console.WriteLine($"Failed to create SVR overlay handle for: {Name} error: {error}");
            }
        }

        public bool SubmitOverlay()
        {
            var tex = new Texture_t
            {
                eType = ETextureType.DirectX,
                eColorSpace = EColorSpace.Auto,
                handle = copiedScreenTexture.NativePointer,
            };
            var vecMouseScale = new HmdVector2_t
            {
                v0 = 1f,
                v1 = aspect
            };
            SteamVR.instance.overlay.SetOverlayMouseScale(vrOverlayHandle, ref vecMouseScale);
            return SteamVR.instance.overlay.SetOverlayTexture(vrOverlayHandle, ref tex) == EVROverlayError.None;
        }

        public void SetOverlayCursors(ulong cursorHandle)
        {
            SteamVR.instance.overlay.SetOverlayCursor(vrOverlayHandle, cursorHandle);
        }

        public void SetOverlayEnabled(bool enabled)
        {
            if (enabled)
                SteamVR.instance.overlay.ShowOverlay(vrOverlayHandle);
            else
                SteamVR.instance.overlay.HideOverlay(vrOverlayHandle);
        }

        public void SetOverlayCurvature()
        {
            SteamVR.instance.overlay.SetOverlayCurvature(vrOverlayHandle, curvature);
        }

        public void SetOverlayTransparency()
        {
            SteamVR.instance.overlay.SetOverlayAlpha(vrOverlayHandle, transparency);
        }

        public void SetOverlayParams(float scale = 1.0f)
        {
            SteamVR.instance.overlay.SetOverlayWidthInMeters(vrOverlayHandle, scale);

            Matrix rotCenter = Matrix.Translation(0, 0, 0);
            rotCenter *= Matrix.RotationX(rotationX);
            rotCenter *= Matrix.RotationY(rotationY);
            rotCenter *= Matrix.RotationZ(rotationZ);
            var transform = Matrix.Scaling(wasGazing && trackingSpace != TrackingSpace.FollowHead ? gazeScale : this.scale) * rotCenter * Matrix.Translation(positionX, positionY, positionZ);
            transform.Transpose();

            if (gazeEnabled && trackingSpace != TrackingSpace.FollowHead)
            {
                if (SetOverlayGazeing(transform))
                {
                    transform = Matrix.Scaling(gazeScale) * rotCenter * Matrix.Translation(positionX, positionY, positionZ);
                    transform.Transpose();
                    SteamVR.instance.overlay.SetOverlayAlpha(vrOverlayHandle, gazeTransparency);
                    wasGazing = true;
                }
                else
                {
                    SteamVR.instance.overlay.SetOverlayAlpha(vrOverlayHandle, transparency);
                    wasGazing = false;
                }
            }

            if (trackingSpace == TrackingSpace.FollowHead)
            {
                HmdMatrix34_t pose = transform.ToHmdMatrix34();
                SteamVR.instance.overlay.SetOverlayTransformTrackedDeviceRelative(vrOverlayHandle, 0, ref pose);
            }
            else
            {
                HmdMatrix34_t pose = transform.ToHmdMatrix34();
                SteamVR.instance.overlay.SetOverlayTransformAbsolute(vrOverlayHandle, (ETrackingUniverseOrigin)trackingSpace, ref pose);
            }
            shouldDraw = false;
        }

        private bool SetOverlayGazeing(Matrix transform)
        {
            float angle = Matrix.Invert(hmdMatrix).Forward.Angle(transform.Forward);
            if (angle <= 90)
            {
                IntersectionResults results = new IntersectionResults();
                if (ComputeIntersection(hmdMatrix.TranslationVector, hmdMatrix.Forward, ref results))
                {
                    return true;
                }
            }
            return false;
        }

        public void Draw()
        {
            SetOverlayEnabled(shouldDraw && enabled);
            if (shouldDraw)
            {
                SubmitOverlay();
                SetOverlayParams();
            }
        }

        public struct IntersectionResults
        {
            public Vector3 point;
            public Vector3 normal;
            public Vector2 UVs;
            public float distance;
        }

        public bool ComputeIntersection(Vector3 source, Vector3 direction, ref IntersectionResults results)
        {
            var overlay = SteamVR.instance.overlay;
            if (overlay == null)
                return false;

            var input = new VROverlayIntersectionParams_t();
            input.eOrigin = OpenVR.Compositor.GetTrackingSpace();
            input.vSource.v0 = source.X;
            input.vSource.v1 = source.Y;
            input.vSource.v2 = source.Z;
            input.vDirection.v0 = direction.X;
            input.vDirection.v1 = direction.Y;
            input.vDirection.v2 = direction.Z;

            var output = new VROverlayIntersectionResults_t();
            if (!overlay.ComputeOverlayIntersection(vrOverlayHandle, ref input, ref output))
                return false;

            results.point = new Vector3(output.vPoint.v0, output.vPoint.v1, output.vPoint.v2);
            results.normal = new Vector3(output.vNormal.v0, output.vNormal.v1, output.vNormal.v2);
            results.UVs = new Vector2(output.vUVs.v0, output.vUVs.v1);
            results.distance = output.fDistance;
            return true;
        }

        public override string ToString()
        {
            return Text;
        }

        public bool MakeTopmost()
        {
            return Win32Stuff.SetWindowPos(hWnd, (IntPtr)Win32Stuff.SpecialWindowHandles.HWND_TOPMOST, 0, 0, 0, 0, Win32Stuff.SetWindowPosFlags.SWP_NOMOVE | Win32Stuff.SetWindowPosFlags.SWP_NOSIZE);
        }

        public bool BringToFront()
        {
            bool ret = Win32Stuff.ShowWindow(hWnd, 1);
            return ret && Win32Stuff.SetForegroundWindow(hWnd) != IntPtr.Zero;
        }

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // TODO: dispose managed state (managed objects).
                    copiedScreenTexture?.Dispose();
                    if (OpenVR.Overlay != null)
                        OpenVR.Overlay.DestroyOverlay(vrOverlayHandle);
                }
                // TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
                // TODO: set large fields to null.
                disposedValue = true;
            }
        }

        // TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
        // ~VROverlayWindow() {
        //   // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
        //   Dispose(false);
        // }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
            // TODO: uncomment the following line if the finalizer is overridden above.
            // GC.SuppressFinalize(this);
        }




        #endregion
    }
}
