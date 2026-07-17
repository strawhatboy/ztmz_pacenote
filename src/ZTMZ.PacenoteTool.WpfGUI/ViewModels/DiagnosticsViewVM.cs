using System;
using System.IO;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Media;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.WpfGUI.Services;

namespace ZTMZ.PacenoteTool.WpfGUI.ViewModels;

public partial class DiagnosticsViewVM : ObservableObject
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    private readonly SupportDiagnosticsService _diagnosticsService;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _hasBundle;

    [ObservableProperty]
    private string _capturedAt = string.Empty;

    [ObservableProperty]
    private string _applicationStatus = string.Empty;

    [ObservableProperty]
    private string _gameStatus = string.Empty;

    [ObservableProperty]
    private string _prerequisiteStatus = string.Empty;

    [ObservableProperty]
    private string _udpStatus = string.Empty;

    [ObservableProperty]
    private string _udpActivity = string.Empty;

    [ObservableProperty]
    private string _audioStatus = string.Empty;

    [ObservableProperty]
    private string _audioActivity = string.Empty;

    [ObservableProperty]
    private string _webDashboardStatus = string.Empty;

    [ObservableProperty]
    private string _webDashboardActivity = string.Empty;

    [ObservableProperty]
    private string _bundleStatus = string.Empty;

    [ObservableProperty]
    private Brush _bundleStatusBrush = Brushes.Gray;

    [ObservableProperty]
    private Brush _gameStatusBrush = Brushes.Gray;

    [ObservableProperty]
    private Brush _udpStatusBrush = Brushes.Gray;

    [ObservableProperty]
    private Brush _audioStatusBrush = Brushes.Gray;

    [ObservableProperty]
    private Brush _webDashboardStatusBrush = Brushes.Gray;

    public DiagnosticsViewVM(SupportDiagnosticsService diagnosticsService)
    {
        _diagnosticsService = diagnosticsService;
    }

    public async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            ApplySnapshot(await _diagnosticsService.CaptureAsync());
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Failed to refresh diagnostics");
            BundleStatus = string.Format(
                I18NLoader.Instance["diagnostics.refreshFailed"],
                exception.Message);
            BundleStatusBrush = Brushes.IndianRed;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task RefreshRuntimeAsync()
    {
        try
        {
            ApplySnapshot(await _diagnosticsService.CaptureRuntimeAsync(), includePrerequisiteStatus: false);
        }
        catch (Exception exception)
        {
            Logger.Warn(exception, "Failed to refresh runtime diagnostics");
        }
    }

    [RelayCommand]
    private Task Refresh()
    {
        return RefreshAsync();
    }

    [RelayCommand]
    private async Task CreateSupportBundle()
    {
        IsBusy = true;
        try
        {
            var result = await _diagnosticsService.CreateBundleAsync();
            BundleStatus = string.Format(
                I18NLoader.Instance["diagnostics.bundleCreated"],
                result.BundlePath);
            BundleStatusBrush = Brushes.ForestGreen;
            HasBundle = true;
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Failed to create support bundle");
            BundleStatus = string.Format(
                I18NLoader.Instance["diagnostics.bundleFailed"],
                exception.Message);
            BundleStatusBrush = Brushes.IndianRed;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenSupportFolder()
    {
        try
        {
            Directory.CreateDirectory(_diagnosticsService.GetSupportFolder());
            Process.Start(new ProcessStartInfo("explorer.exe", _diagnosticsService.GetSupportFolder())
            {
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Failed to open support folder");
            BundleStatus = string.Format(
                I18NLoader.Instance["diagnostics.bundleFailed"],
                exception.Message);
            BundleStatusBrush = Brushes.IndianRed;
        }
    }

    private void ApplySnapshot(SupportDiagnosticsSnapshot snapshot, bool includePrerequisiteStatus = true)
    {
        CapturedAt = FormatTime(snapshot.CapturedAtUtc);
        ApplicationStatus = string.Join(
            " | ",
            snapshot.Application.Version,
            snapshot.Application.Runtime,
            snapshot.Application.OperatingSystem);

        var gameName = string.IsNullOrWhiteSpace(snapshot.Game.Name)
            ? I18NLoader.Instance["diagnostics.notSelected"]
            : snapshot.Game.Name;
        GameStatus = string.Join(
            " | ",
            gameName,
            snapshot.Game.Transport,
            snapshot.Game.ReaderType,
            snapshot.Game.State);
        if (includePrerequisiteStatus)
        {
            PrerequisiteStatus = string.IsNullOrWhiteSpace(snapshot.Game.Prerequisite.Detail)
                ? snapshot.Game.Prerequisite.Code
                : $"{snapshot.Game.Prerequisite.Code} | {snapshot.Game.Prerequisite.Detail}";
            GameStatusBrush = string.IsNullOrWhiteSpace(snapshot.Game.Name)
                ? Brushes.Gray
                : snapshot.Game.Prerequisite.IsHealthy ? Brushes.ForestGreen : Brushes.IndianRed;
        }

        if (snapshot.Udp == null)
        {
            UdpStatus = I18NLoader.Instance["diagnostics.notApplicable"];
            UdpActivity = string.Empty;
            UdpStatusBrush = Brushes.Gray;
        }
        else
        {
            var endpoint = string.IsNullOrWhiteSpace(snapshot.Udp.Endpoint)
                ? snapshot.Game.ExpectedEndpoint
                : snapshot.Udp.Endpoint;

            if (!snapshot.Game.IsInitialized)
            {
                UdpStatus = string.Join(
                    " | ",
                    I18NLoader.Instance["diagnostics.waitingForGame"],
                    endpoint);
                UdpActivity = string.Empty;
                UdpStatusBrush = Brushes.Gray;
            }
            else
            {
                UdpStatus = string.Join(
                    " | ",
                    snapshot.Udp.IsListening
                        ? I18NLoader.Instance["diagnostics.listening"]
                        : snapshot.Udp.IsOptional
                            ? $"{I18NLoader.Instance["diagnostics.optional"]} | {I18NLoader.Instance["diagnostics.stopped"]}"
                            : I18NLoader.Instance["diagnostics.stopped"],
                    endpoint,
                    $"{snapshot.Udp.SecondsWithoutMessage}s");
                UdpActivity = $"{snapshot.Udp.PacketCount} | {FormatTime(snapshot.Udp.LastPacketUtc)} | {snapshot.Udp.LastPacketSize} B";
                if (!string.IsNullOrWhiteSpace(snapshot.Udp.LastError))
                {
                    UdpActivity += $" | {snapshot.Udp.LastError}";
                }
                UdpStatusBrush = !string.IsNullOrWhiteSpace(snapshot.Udp.LastError)
                    ? Brushes.IndianRed
                    : snapshot.Udp.IsListening ? Brushes.ForestGreen : Brushes.Gray;
            }
        }

        AudioStatus = string.Join(
            " | ",
            snapshot.Audio.IsActive
                ? I18NLoader.Instance["diagnostics.active"]
                : I18NLoader.Instance["diagnostics.inactive"],
            $"Device {snapshot.Audio.DeviceId}",
            $"{snapshot.Audio.DesiredLatencyMilliseconds} ms");
        AudioActivity = $"{snapshot.Audio.QueuedSoundCount} | {FormatTime(snapshot.Audio.LastSoundQueuedUtc)}";
        if (!string.IsNullOrWhiteSpace(snapshot.Audio.LastError))
        {
            AudioActivity += $" | {snapshot.Audio.LastError}";
        }
        AudioStatusBrush = !string.IsNullOrWhiteSpace(snapshot.Audio.LastError)
            ? Brushes.IndianRed
            : snapshot.Audio.IsActive ? Brushes.ForestGreen : Brushes.Gray;

        WebDashboardStatus = string.Join(
            " | ",
            snapshot.WebDashboard.State,
            snapshot.WebDashboard.Url,
            $"{snapshot.WebDashboard.Port}");
        WebDashboardActivity = $"{snapshot.WebDashboard.ConnectedClientCount} | {snapshot.WebDashboard.TargetFrameRate} FPS | {snapshot.WebDashboard.RenderedFrameCount} | {FormatTime(snapshot.WebDashboard.LastRenderedFrameUtc)}";
        if (!string.IsNullOrWhiteSpace(snapshot.WebDashboard.ErrorMessage))
        {
            WebDashboardActivity += $" | {snapshot.WebDashboard.ErrorMessage}";
        }
        WebDashboardStatusBrush = snapshot.WebDashboard.State switch
        {
            "Running" => Brushes.ForestGreen,
            "PortUnavailable" or "Error" => Brushes.IndianRed,
            _ => Brushes.Gray
        };
    }

    private static string FormatTime(DateTimeOffset? timestamp)
    {
        return timestamp?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
            ?? I18NLoader.Instance["diagnostics.none"];
    }
}
