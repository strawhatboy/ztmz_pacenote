namespace ZTMZ.PacenoteTool.UI.Windows
{
    public enum NativeOverlayState
    {
        Unavailable,
        Disabled,
        NoDashboard,
        Ready,
        Active,
        Preview
    }

    public readonly record struct NativeOverlayStatus(
        NativeOverlayState State,
        bool IsVisible);

    public static class NativeOverlayVisibilityPolicy
    {
        public static NativeOverlayStatus Resolve(
            bool isOverlayReady,
            bool isPreviewActive,
            bool isNativeHudEnabled,
            bool isStageActive,
            bool hasNativeDashboard)
        {
            if (!isPreviewActive && !isNativeHudEnabled)
            {
                return new NativeOverlayStatus(NativeOverlayState.Disabled, false);
            }

            if (!hasNativeDashboard)
            {
                return new NativeOverlayStatus(NativeOverlayState.NoDashboard, false);
            }

            if (!isOverlayReady)
            {
                return new NativeOverlayStatus(NativeOverlayState.Unavailable, false);
            }

            if (isPreviewActive)
            {
                return new NativeOverlayStatus(NativeOverlayState.Preview, true);
            }

            return isStageActive
                ? new NativeOverlayStatus(NativeOverlayState.Active, true)
                : new NativeOverlayStatus(NativeOverlayState.Ready, false);
        }
    }
}
