using SharpDX;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VRGameOverlay.VROverlayWindow
{
    // Xenko game engine...
    public class TrackedDevices
    {
        public class Controller
        {
            private static readonly uint sizeVRControllerState_t = (uint)Utilities.SizeOf<VRControllerState_t>();
            // This helper can be used in a variety of ways.  Beware that indices may change
            // as new devices are dynamically added or removed, controllers are physically
            // swapped between hands, arms crossed, etc.
            public enum Hand
            {
                Left,
                Right,
            }

            public static int GetDeviceIndex(Hand hand)
            {
                var currentIndex = 0;
                for (uint index = 0; index < DevicePoses.Length; index++)
                {
                    if (OpenVR.System.GetTrackedDeviceClass(index) == ETrackedDeviceClass.Controller)
                    {
                        if (hand == Hand.Left && OpenVR.System.GetControllerRoleForTrackedDeviceIndex(index) == ETrackedControllerRole.LeftHand)
                        {
                            return currentIndex;
                        }

                        if (hand == Hand.Right && OpenVR.System.GetControllerRoleForTrackedDeviceIndex(index) == ETrackedControllerRole.RightHand)
                        {
                            return currentIndex;
                        }

                        currentIndex++;
                    }
                }

                return -1;
            }

            public class ButtonMask
            {
                public const ulong System = 1ul << (int)EVRButtonId.k_EButton_System; // reserved
                public const ulong ApplicationMenu = 1ul << (int)EVRButtonId.k_EButton_ApplicationMenu;
                public const ulong Grip = 1ul << (int)EVRButtonId.k_EButton_Grip;
                public const ulong Axis0 = 1ul << (int)EVRButtonId.k_EButton_Axis0;
                public const ulong Axis1 = 1ul << (int)EVRButtonId.k_EButton_Axis1;
                public const ulong Axis2 = 1ul << (int)EVRButtonId.k_EButton_Axis2;
                public const ulong Axis3 = 1ul << (int)EVRButtonId.k_EButton_Axis3;
                public const ulong Axis4 = 1ul << (int)EVRButtonId.k_EButton_Axis4;
                public const ulong Touchpad = 1ul << (int)EVRButtonId.k_EButton_SteamVR_Touchpad;
                public const ulong Trigger = 1ul << (int)EVRButtonId.k_EButton_SteamVR_Trigger;
            }

            public enum ButtonId
            {
                ButtonSystem = 0,
                ButtonApplicationMenu = 1,
                ButtonGrip = 2,
                ButtonDPadLeft = 3,
                ButtonDPadUp = 4,
                ButtonDPadRight = 5,
                ButtonDPadDown = 6,
                ButtonA = 7,
                ButtonProximitySensor = 31,
                ButtonAxis0 = 32,
                ButtonAxis1 = 33,
                ButtonAxis2 = 34,
                ButtonAxis3 = 35,
                ButtonAxis4 = 36,
                ButtonSteamVrTouchpad = 32,
                ButtonSteamVrTrigger = 33,
                ButtonDashboardBack = 2,
                ButtonMax = 64,
            }

            public Controller(int controllerIndex)
            {
                var currentIndex = 0;
                for (uint index = 0; index < DevicePoses.Length; index++)
                {
                    if (OpenVR.System.GetTrackedDeviceClass(index) == ETrackedDeviceClass.Controller)
                    {
                        if (currentIndex == controllerIndex)
                        {
                            ControllerIndex = index;
                            break;
                        }
                        currentIndex++;
                    }
                }
            }

            internal uint ControllerIndex;
            internal VRControllerState_t State;
            internal VRControllerState_t PreviousState;

            public bool GetPress(ulong buttonMask) { return (State.ulButtonPressed & buttonMask) != 0; }

            public bool GetPressDown(ulong buttonMask) { return (State.ulButtonPressed & buttonMask) != 0 && (PreviousState.ulButtonPressed & buttonMask) == 0; }

            public bool GetPressUp(ulong buttonMask) { return (State.ulButtonPressed & buttonMask) == 0 && (PreviousState.ulButtonPressed & buttonMask) != 0; }

            public bool GetPress(ButtonId buttonId) { return GetPress(1ul << (int)buttonId); }

            public bool GetPressDown(ButtonId buttonId) { return GetPressDown(1ul << (int)buttonId); }

            public bool GetPressUp(ButtonId buttonId) { return GetPressUp(1ul << (int)buttonId); }

            public bool GetTouch(ulong buttonMask) { return (State.ulButtonTouched & buttonMask) != 0; }

            public bool GetTouchDown(ulong buttonMask) { return (State.ulButtonTouched & buttonMask) != 0 && (PreviousState.ulButtonTouched & buttonMask) == 0; }

            public bool GetTouchUp(ulong buttonMask) { return (State.ulButtonTouched & buttonMask) == 0 && (PreviousState.ulButtonTouched & buttonMask) != 0; }

            public bool GetTouch(ButtonId buttonId) { return GetTouch(1ul << (int)buttonId); }

            public bool GetTouchDown(ButtonId buttonId) { return GetTouchDown(1ul << (int)buttonId); }

            public bool GetTouchUp(ButtonId buttonId) { return GetTouchUp(1ul << (int)buttonId); }

            public Vector2 GetAxis(ButtonId buttonId = ButtonId.ButtonSteamVrTouchpad)
            {
                var axisId = (uint)buttonId - (uint)EVRButtonId.k_EButton_Axis0;
                switch (axisId)
                {
                    case 0: return new Vector2(State.rAxis0.x, State.rAxis0.y);
                    case 1: return new Vector2(State.rAxis1.x, State.rAxis1.y);
                    case 2: return new Vector2(State.rAxis2.x, State.rAxis2.y);
                    case 3: return new Vector2(State.rAxis3.x, State.rAxis3.y);
                    case 4: return new Vector2(State.rAxis4.x, State.rAxis4.y);
                }
                return Vector2.Zero;
            }

            public void Update()
            {
                PreviousState = State;
                OpenVR.System.GetControllerState(ControllerIndex, ref State, sizeVRControllerState_t);
            }
        }

        public class TrackedDevice
        {
            public TrackedDevice(int trackerIndex)
            {
                TrackerIndex = trackerIndex;
            }

            const int StringBuilderSize = 64;
            StringBuilder serialNumberStringBuilder = new StringBuilder(StringBuilderSize);
            internal string SerialNumber
            {
                get
                {
                    var error = ETrackedPropertyError.TrackedProp_Success;
                    serialNumberStringBuilder.Clear();
                    OpenVR.System.GetStringTrackedDeviceProperty((uint)TrackerIndex, ETrackedDeviceProperty.Prop_SerialNumber_String, serialNumberStringBuilder, StringBuilderSize, ref error);
                    if (error == ETrackedPropertyError.TrackedProp_Success)
                        return serialNumberStringBuilder.ToString();
                    else
                        return "";
                }
            }

            internal float BatteryPercentage
            {
                get
                {
                    var error = ETrackedPropertyError.TrackedProp_Success;
                    var value = OpenVR.System.GetFloatTrackedDeviceProperty((uint)TrackerIndex, ETrackedDeviceProperty.Prop_DeviceBatteryPercentage_Float, ref error);
                    if (error == ETrackedPropertyError.TrackedProp_Success)
                        return value;
                    else
                        return 0;
                }
            }

            internal int TrackerIndex;
            internal ETrackedDeviceClass DeviceClass => OpenVR.System.GetTrackedDeviceClass((uint)TrackerIndex);
        }

        private static readonly TrackedDevicePose_t[] DevicePoses = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
        private static readonly TrackedDevicePose_t[] GamePoses = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];

        public static void UpdatePoses()
        {
            //OpenVR.Compositor.PostPresentHandoff();
            var result = OpenVR.Compositor.GetLastPoses(DevicePoses, GamePoses);
            if (result != EVRCompositorError.None)
            {
                Console.WriteLine(result);
            }
        }

        public static void Recenter(ETrackingUniverseOrigin eTrackingUniverse)
        {
            OpenVR.Chaperone.ResetZeroPose(eTrackingUniverse);
        }

        public static void GetEyeToHead(int eyeIndex, out Matrix pose)
        {
            pose = Matrix.Identity;
            var eye = eyeIndex == 0 ? EVREye.Eye_Left : EVREye.Eye_Right;
            var eyeToHead = OpenVR.System.GetEyeToHeadTransform(eye);
            pose = pose.FromHmdMatrix(eyeToHead);
        }
        public static DeviceState GetControllerPose(int controllerIndex, out Matrix pose, out Vector3 velocity, out Vector3 angVelocity)
        {
            var currentIndex = 0;

            pose = Matrix.Identity;
            velocity = Vector3.Zero;
            angVelocity = Vector3.Zero;

            for (uint index = 0; index < DevicePoses.Length; index++)
            {
                if (OpenVR.System.GetTrackedDeviceClass(index) == ETrackedDeviceClass.Controller)
                {
                    if (currentIndex == controllerIndex)
                    {
                        pose = pose.FromHmdMatrix(DevicePoses[index].mDeviceToAbsoluteTracking);
                        velocity = new Vector3(DevicePoses[index].vVelocity.v0, DevicePoses[index].vVelocity.v1, DevicePoses[index].vVelocity.v2);
                        angVelocity = new Vector3(DevicePoses[index].vAngularVelocity.v0, DevicePoses[index].vAngularVelocity.v1, DevicePoses[index].vAngularVelocity.v2);
                        var state = DeviceState.Invalid;
                        if (DevicePoses[index].bDeviceIsConnected && DevicePoses[index].bPoseIsValid)
                        {
                            state = DeviceState.Valid;
                        }
                        else if (DevicePoses[index].bDeviceIsConnected && !DevicePoses[index].bPoseIsValid && DevicePoses[index].eTrackingResult == ETrackingResult.Running_OutOfRange)
                        {
                            state = DeviceState.OutOfRange;
                        }

                        return state;
                    }
                    currentIndex++;
                }
            }

            return DeviceState.Invalid;
        }

        public static DeviceState GetTrackerPose(int trackerIndex, out Matrix pose, out Vector3 velocity, out Vector3 angVelocity)
        {
            pose = Matrix.Identity;
            velocity = Vector3.Zero;
            angVelocity = Vector3.Zero;
            var index = trackerIndex;

            pose = pose.FromHmdMatrix(DevicePoses[index].mDeviceToAbsoluteTracking);
            velocity = new Vector3(DevicePoses[index].vVelocity.v0, DevicePoses[index].vVelocity.v1, DevicePoses[index].vVelocity.v2);
            angVelocity = new Vector3(DevicePoses[index].vAngularVelocity.v0, DevicePoses[index].vAngularVelocity.v1, DevicePoses[index].vAngularVelocity.v2);

            var state = DeviceState.Invalid;
            if (DevicePoses[index].bDeviceIsConnected && DevicePoses[index].bPoseIsValid)
            {
                state = DeviceState.Valid;
            }
            else if (DevicePoses[index].bDeviceIsConnected && !DevicePoses[index].bPoseIsValid && DevicePoses[index].eTrackingResult == ETrackingResult.Running_OutOfRange)
            {
                state = DeviceState.OutOfRange;
            }

            return state;
        }

        public static DeviceState GetHeadPose(out Matrix pose, out Vector3 linearVelocity, out Vector3 angularVelocity)
        {
            pose = Matrix.Identity;
            linearVelocity = Vector3.Zero;
            angularVelocity = Vector3.Zero;
            for (uint index = 0; index < DevicePoses.Length; index++)
            {
                if (OpenVR.System.GetTrackedDeviceClass(index) == ETrackedDeviceClass.HMD)
                {
                    pose = pose.FromHmdMatrix(DevicePoses[index].mDeviceToAbsoluteTracking);
                    linearVelocity = new Vector3(DevicePoses[index].vVelocity.v0, DevicePoses[index].vVelocity.v1, DevicePoses[index].vVelocity.v2);
                    angularVelocity = new Vector3(DevicePoses[index].vAngularVelocity.v0, DevicePoses[index].vAngularVelocity.v1, DevicePoses[index].vAngularVelocity.v2);

                    var state = DeviceState.Invalid;
                    if (DevicePoses[index].bDeviceIsConnected && DevicePoses[index].bPoseIsValid)
                    {
                        state = DeviceState.Valid;
                    }
                    else if (DevicePoses[index].bDeviceIsConnected && !DevicePoses[index].bPoseIsValid && DevicePoses[index].eTrackingResult == ETrackingResult.Running_OutOfRange)
                    {
                        state = DeviceState.OutOfRange;
                    }

                    return state;
                }
            }

            return DeviceState.Invalid;
        }

        public static void GetProjection(int eyeIndex, float near, float far, out Matrix projection)
        {
            projection = Matrix.Identity;
            var eye = eyeIndex == 0 ? EVREye.Eye_Left : EVREye.Eye_Right;
            var proj = OpenVR.System.GetProjectionMatrix(eye, near, far);
            projection = projection.FromHmdMatrix(proj);
        }
    }

}
