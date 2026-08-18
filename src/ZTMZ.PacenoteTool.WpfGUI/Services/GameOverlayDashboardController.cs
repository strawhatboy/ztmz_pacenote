using System;
using ZTMZ.PacenoteTool.Base.UI;
using ZTMZ.PacenoteTool.Core;

namespace ZTMZ.PacenoteTool.WpfGUI.Services;

public sealed class GameOverlayDashboardController : IRuntimeDashboardController
{
    private readonly GameOverlayManager _gameOverlayManager;

    public GameOverlayDashboardController(GameOverlayManager gameOverlayManager)
    {
        _gameOverlayManager = gameOverlayManager;
        _gameOverlayManager.OnWebDashboardStatusChanged += _ =>
            StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? StatusChanged;

    public RuntimeDashboardStatus Status => ConvertStatus();

    public RuntimeDashboardDiagnostics Diagnostics
    {
        get
        {
            var diagnostics = _gameOverlayManager.WebDashboardDiagnostics;
            return new RuntimeDashboardDiagnostics(
                ConvertStatus(),
                diagnostics.ConnectedClientCount,
                diagnostics.TargetFrameRate,
                diagnostics.RenderedFrameCount,
                diagnostics.LastRenderedFrameUtc);
        }
    }

    public void Configure(bool enabled, int port, int targetFrameRate) =>
        _gameOverlayManager.ConfigureWebDashboard(enabled, port, targetFrameRate);

    private RuntimeDashboardStatus ConvertStatus()
    {
        var status = _gameOverlayManager.WebDashboardStatus;
        return new RuntimeDashboardStatus(
            status.State.ToString(),
            status.Port,
            status.Url,
            status.ErrorMessage,
            status.IsRunning);
    }
}
