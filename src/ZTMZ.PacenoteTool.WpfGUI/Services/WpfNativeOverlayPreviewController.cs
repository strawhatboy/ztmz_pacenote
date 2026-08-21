using System;
using System.Windows;
using System.Windows.Interop;
using ZTMZ.PacenoteTool.UI.Windows;

namespace ZTMZ.PacenoteTool.WpfGUI.Services;

/// <summary>Owns the WPF-only preview window while the native renderer stays platform-neutral.</summary>
public sealed class WpfNativeOverlayPreviewController
{
    private readonly GameOverlayManager _overlay;
    private SeparateWindowForOverlay? _window;

    public WpfNativeOverlayPreviewController(GameOverlayManager overlay)
    {
        _overlay = overlay;
    }

    public void Show()
    {
        var window = GetOrCreateWindow();
        if (!window.IsVisible)
        {
            window.Show();
        }

        if (!_overlay.InitializeOverlayInPreviewWindow(new WindowInteropHelper(window).Handle))
        {
            window.Activate();
        }
    }

    private SeparateWindowForOverlay GetOrCreateWindow()
    {
        if (_window != null)
        {
            return _window;
        }

        _window = new SeparateWindowForOverlay
        {
            Title = "PacenoteTool GameOverlay Separate Window",
            Width = 1280,
            Height = 720
        };
        _window.Closing += OnWindowClosing;
        return _window;
    }

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs args)
    {
        args.Cancel = true;
        _window?.Hide();
        _overlay.ClosePreviewWindow();
    }
}
