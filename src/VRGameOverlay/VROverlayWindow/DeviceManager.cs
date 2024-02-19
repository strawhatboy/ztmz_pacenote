using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
//using System;
//using System.Windows.Forms;
using Device = SharpDX.Direct3D11.Device;

namespace VRGameOverlay.VROverlayWindow
{
    public enum GraphicsDeviceStatus
    {
        /// <summary>
        /// The device is running fine.
        /// </summary>
        Normal,

        /// <summary>
        /// The video card has been physically removed from the system, or a driver upgrade for the video card has occurred. The application should destroy and recreate the device.
        /// </summary>
        Removed,

        /// <summary>
        /// The application's device failed due to badly formed commands sent by the application. This is an design-time issue that should be investigated and fixed.
        /// </summary>
        Hung,

        /// <summary>
        /// The device failed due to a badly formed command. This is a run-time issue; The application should destroy and recreate the device.
        /// </summary>
        Reset,

        /// <summary>
        /// The driver encountered a problem and was put into the device removed state.
        /// </summary>
        InternalError,

        /// <summary>
        /// The application provided invalid parameter data; this must be debugged and fixed before the application is released.
        /// </summary>
        InvalidCall,
    }

    public class DeviceManager : IDisposable
    {
        protected bool disposedValue = false; // To detect redundant calls
        protected Device _device;
        protected SwapChain _swapChain;

        public Device device { get { return _device; } }
        public DeviceContext1 context { get; private set; }
        //public Factory4 factory { get; private set; }
        public RenderTargetView backBufferView { get; private set; }
        public SwapChain swapChain { get { return _swapChain; } }


        public SharpDX.Direct2D1.Device Device2D { get; private set; }
        public SharpDX.Direct2D1.DeviceContext DeviceContext2D { get; private set; }
        public SharpDX.DXGI.Device DXGIDevice { get; private set; }

        public DeviceManager(CVRSystem vrSystem /*, Form form = null */)
        {
            try
            {
                int adapterIndex = 0;
                if (vrSystem != null)
                {
                    vrSystem.GetDXGIOutputInfo(ref adapterIndex);
                }

                using (var factory = new Factory4())
                using (var adapter = factory.GetAdapter(adapterIndex))
                {
                    /*
                    if (form != null)
                    {
                        var swapChainDescription = new SwapChainDescription
                        {
                            BufferCount = 1,
                            Flags = SwapChainFlags.None,
                            IsWindowed = true,
                            ModeDescription =
                            {
                                Format = Format.B8G8R8A8_UNorm,
                                Width = form.ClientSize.Width,
                                Height = form.ClientSize.Height,
                                RefreshRate = { Numerator = 144, Denominator = 1 }
                            },
                            OutputHandle = form.Handle,
                            SampleDescription = { Count = 1, Quality = 0 },
                            SwapEffect = SwapEffect.Discard,
                            Usage = Usage.RenderTargetOutput
                        };

                        // Retrieve the Direct3D 11.1 device and device context
                        Device.CreateWithSwapChain(adapter, DeviceCreationFlags.BgraSupport, swapChainDescription, out _device, out _swapChain);
                        
                        using (var backBuffer = swapChain.GetBackBuffer<Texture2D>(0))
                            backBufferView = new RenderTargetView(device, backBuffer);

                        factory.MakeWindowAssociation(form.Handle, WindowAssociationFlags.None);
                    }
                    else
                    {
                        var creationFlags = DeviceCreationFlags.BgraSupport;
#if DEBUG
                        // Enable D3D device debug layer
                        creationFlags |= DeviceCreationFlags.Debug;
#endif
                        _device = new Device(adapter, creationFlags);
                    }
                    */


                    var creationFlags = DeviceCreationFlags.BgraSupport;
                    _device = new Device(adapter, creationFlags);

                    DXGIDevice = device.QueryInterface<SharpDX.DXGI.Device>();
                    Device2D = new SharpDX.Direct2D1.Device(DXGIDevice);
                    DeviceContext2D = new SharpDX.Direct2D1.DeviceContext(Device2D, SharpDX.Direct2D1.DeviceContextOptions.None);

                    context = _device.ImmediateContext.QueryInterface<DeviceContext1>();
                }
            }
            catch (SharpDXException e)
            {
                Console.WriteLine(e.Message);
            }
        }

        internal bool testDevice()
        {
            return true;
        }

        internal void refreshDevice()
        {
            device.Dispose();
            //device = new Device(SharpDX.Direct3D.DriverType.Hardware);
        }

        public GraphicsDeviceStatus GraphicsDeviceStatus
        {
            get
            {
                var result = device.DeviceRemovedReason;
                if (result == SharpDX.DXGI.ResultCode.DeviceRemoved)
                {
                    return GraphicsDeviceStatus.Removed;
                }

                if (result == SharpDX.DXGI.ResultCode.DeviceReset)
                {
                    return GraphicsDeviceStatus.Reset;
                }

                if (result == SharpDX.DXGI.ResultCode.DeviceHung)
                {
                    return GraphicsDeviceStatus.Hung;
                }

                if (result == SharpDX.DXGI.ResultCode.DriverInternalError)
                {
                    return GraphicsDeviceStatus.InternalError;
                }

                if (result == SharpDX.DXGI.ResultCode.InvalidCall)
                {
                    return GraphicsDeviceStatus.InvalidCall;
                }

                if (result.Code < 0)
                {
                    return GraphicsDeviceStatus.Reset;
                }

                return GraphicsDeviceStatus.Normal;
            }
        }
        protected virtual void Dispose(bool disposing)
        {

        }

        ~DeviceManager()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            if (!disposedValue)
            {
                DeviceContext2D?.Dispose();
                Device2D?.Dispose();
                DXGIDevice?.Dispose();
                context?.Dispose();
                device?.Dispose();
                _swapChain?.Dispose();
                DeviceContext2D = null;
                Device2D = null;
                DXGIDevice = null;
                _device = null;
                _swapChain = null;
                disposedValue = true;
                context = null;
            }
            GC.SuppressFinalize(this);
        }
    }
}
