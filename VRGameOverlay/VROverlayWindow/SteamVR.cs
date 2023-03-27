//========= Copyright 2014, Valve Corporation, All rights reserved. ===========
//
// Purpose: Access to SteamVR system (hmd) and compositor (distort) interfaces.
//
//=============================================================================

using System;
using SharpDX;
using MathUtil = VRGameOverlay.VROverlayWindow.MathUtil;
using VRGameOverlay.VROverlayWindow;

public class SteamVR : System.IDisposable
{
    // Set this to false to keep from auto-initializing when calling SteamVR.instance.
    private static bool _enabled = true;

    private static SteamVR _instance;

    private SteamVR()
    {
        hmd = OpenVR.System;
        Console.WriteLine("Connected to " + hmd_TrackingSystemName + ":" + hmd_SerialNumber);

        compositor = OpenVR.Compositor;
        overlay = OpenVR.Overlay;

        // Setup render values
        uint w = 0, h = 0;
        hmd.GetRecommendedRenderTargetSize(ref w, ref h);
        sceneWidth = (float)w;
        sceneHeight = (float)h;
        float l_left = 0.0f, l_right = 0.0f, l_top = 0.0f, l_bottom = 0.0f;
        hmd.GetProjectionRaw(EVREye.Eye_Left, ref l_left, ref l_right, ref l_top, ref l_bottom);

        float r_left = 0.0f, r_right = 0.0f, r_top = 0.0f, r_bottom = 0.0f;
        hmd.GetProjectionRaw(EVREye.Eye_Right, ref r_left, ref r_right, ref r_top, ref r_bottom);

        tanHalfFov = new Vector2(
            MathUtil.Max(-l_left, l_right, -r_left, r_right),
            MathUtil.Max(-l_top, l_bottom, -r_top, r_bottom));

        textureBounds = new VRTextureBounds_t[2];

        textureBounds[0].uMin = 0.5f + 0.5f * l_left / tanHalfFov.X;
        textureBounds[0].uMax = 0.5f + 0.5f * l_right / tanHalfFov.X;
        textureBounds[0].vMin = 0.5f - 0.5f * l_bottom / tanHalfFov.Y;
        textureBounds[0].vMax = 0.5f - 0.5f * l_top / tanHalfFov.Y;

        textureBounds[1].uMin = 0.5f + 0.5f * r_left / tanHalfFov.X;
        textureBounds[1].uMax = 0.5f + 0.5f * r_right / tanHalfFov.X;
        textureBounds[1].vMin = 0.5f - 0.5f * r_bottom / tanHalfFov.Y;
        textureBounds[1].vMax = 0.5f - 0.5f * r_top / tanHalfFov.Y;

        // Grow the recommended size to account for the overlapping fov
        sceneWidth = sceneWidth / Math.Max(textureBounds[0].uMax - textureBounds[0].uMin, textureBounds[1].uMax - textureBounds[1].uMin);
        sceneHeight = sceneHeight / Math.Max(textureBounds[0].vMax - textureBounds[0].vMin, textureBounds[1].vMax - textureBounds[1].vMin);

        aspect = tanHalfFov.X / tanHalfFov.Y;
        fieldOfView = 2.0f * (float)Math.Atan(tanHalfFov.Y) * MathUtil.Rad2Deg;

        graphicsAPI = ETextureType.DirectX;
    }

    ~SteamVR()
    {
        Dispose(false);
    }

    // Use this to check if SteamVR is currently active without attempting
    // to activate it in the process.
    public static bool active { get { return _instance != null; } }
    public static bool enabled
    {
        get { return _enabled; }
        set
        {
            _enabled = value;
            if (!_enabled)
                SafeDispose();
        }
    }
    public static SteamVR instance
    {
        get
        {
            if (!enabled)
                return null;

            if (_instance == null)
            {
                _instance = CreateInstance();

                // If init failed, then auto-disable so scripts don't continue trying to re-initialize things.
                if (_instance == null)
                    _enabled = false;
            }

            return _instance;
        }
    }

    // Use this interface to avoid accidentally creating the instance in the process of attempting to dispose of it.
    public static void SafeDispose()
    {
        if (_instance != null)
        {
            _instance.Dispose();
            _instance = null;
        }

    }

    public void Dispose()
    {
        Dispose(true);
        System.GC.SuppressFinalize(this);
    }

    public string GetTrackedDeviceString(uint deviceId)
    {
        var error = ETrackedPropertyError.TrackedProp_Success;
        var capacity = hmd.GetStringTrackedDeviceProperty(deviceId, ETrackedDeviceProperty.Prop_AttachedDeviceId_String, null, 0, ref error);
        if (capacity > 1)
        {
            var result = new System.Text.StringBuilder((int)capacity);
            hmd.GetStringTrackedDeviceProperty(deviceId, ETrackedDeviceProperty.Prop_AttachedDeviceId_String, result, capacity, ref error);
            return result.ToString();
        }
        return null;
    }

    private static SteamVR CreateInstance()
    {
        try
        {
            var error = EVRInitError.None;

            OpenVR.Init(ref error, EVRApplicationType.VRApplication_Overlay);
            if (error != EVRInitError.None)
            {
                ReportError(error);
                ShutdownSystems();
                return null;
            }

            // Verify common interfaces are valid.

            OpenVR.GetGenericInterface(OpenVR.IVRCompositor_Version, ref error);
            if (error != EVRInitError.None)
            {
                ReportError(error);
                ShutdownSystems();
                return null;
            }

            OpenVR.GetGenericInterface(OpenVR.IVROverlay_Version, ref error);
            if (error != EVRInitError.None)
            {
                ReportError(error);
                ShutdownSystems();
                return null;
            }
        }
        catch (System.Exception e)
        {
            Console.WriteLine(e.Message);
        }

        return new SteamVR();
    }

    private static void ReportError(EVRInitError error)
    {
        switch (error)
        {
            case EVRInitError.None:
                break;

            case EVRInitError.VendorSpecific_UnableToConnectToOculusRuntime:
                Console.WriteLine("SteamVR Initialization Failed!  Make sure device is on, Oculus runtime is installed, and OVRService_*.exe is running.");
                break;
            case EVRInitError.Init_VRClientDLLNotFound:
                Console.WriteLine("SteamVR drivers not found!  They can be installed via Steam under Library > Tools.  Visit http://steampowered.com to install Steam.");
                break;
            case EVRInitError.Driver_RuntimeOutOfDate:
                Console.WriteLine("SteamVR Initialization Failed!  Make sure device's runtime is up to date.");
                break;
            default:
                Console.WriteLine(OpenVR.GetStringForHmdError(error));
                break;
        }
    }

    #region native interfaces

    internal CVRCompositor compositor { get; private set; }
    internal CVRSystem hmd { get; private set; }
    internal CVROverlay overlay { get; private set; }

    #endregion native interfaces

    #region tracking status

    static public bool[] connected = new bool[OpenVR.k_unMaxTrackedDeviceCount];
    static public bool calibrating { get; private set; }
    static public bool initializing { get; private set; }
    static public bool outOfRange { get; private set; }
    #endregion tracking status

    #region render values

    internal ETextureType graphicsAPI;
    public float aspect { get; private set; }
    public float fieldOfView { get; private set; }
    public float sceneHeight { get; private set; }
    public float sceneWidth { get; private set; }
    public Vector2 tanHalfFov { get; private set; }
    public VRTextureBounds_t[] textureBounds { get; private set; }

    #endregion render values

    #region hmd properties

    public float hmd_DisplayFrequency { get { return GetFloatProperty(ETrackedDeviceProperty.Prop_DisplayFrequency_Float); } }
    public string hmd_ModelNumber { get { return GetStringProperty(ETrackedDeviceProperty.Prop_ModelNumber_String); } }
    public float hmd_SecondsFromVsyncToPhotons { get { return GetFloatProperty(ETrackedDeviceProperty.Prop_SecondsFromVsyncToPhotons_Float); } }
    public string hmd_SerialNumber { get { return GetStringProperty(ETrackedDeviceProperty.Prop_SerialNumber_String); } }
    public string hmd_TrackingSystemName { get { return GetStringProperty(ETrackedDeviceProperty.Prop_TrackingSystemName_String); } }
    #endregion hmd properties
    private static void ShutdownSystems()
    {
        OpenVR.Shutdown();
    }

    private void Dispose(bool disposing)
    {
        ShutdownSystems();
        _instance = null;
    }

    private float GetFloatProperty(ETrackedDeviceProperty prop)
    {
        var error = ETrackedPropertyError.TrackedProp_Success;
        return hmd.GetFloatTrackedDeviceProperty(OpenVR.k_unTrackedDeviceIndex_Hmd, prop, ref error);
    }

    private string GetStringProperty(ETrackedDeviceProperty prop)
    {
        var error = ETrackedPropertyError.TrackedProp_Success;
        var capactiy = hmd.GetStringTrackedDeviceProperty(OpenVR.k_unTrackedDeviceIndex_Hmd, prop, null, 0, ref error);
        if (capactiy > 1)
        {
            var result = new System.Text.StringBuilder((int)capactiy);
            hmd.GetStringTrackedDeviceProperty(OpenVR.k_unTrackedDeviceIndex_Hmd, prop, result, capactiy, ref error);
            return result.ToString();
        }
        return (error != ETrackedPropertyError.TrackedProp_Success) ? error.ToString() : "<unknown>";
    }
}