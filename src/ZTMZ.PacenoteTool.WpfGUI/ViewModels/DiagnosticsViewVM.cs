using System;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.Core;
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
    private string _transportStatusLabel = "UDP";

    [ObservableProperty]
    private string _udpActivity = string.Empty;

    [ObservableProperty]
    private string _udpTooltip = string.Empty;

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
            GetGameTransportLabel(snapshot.Game.Transport),
            GetGameReaderLabel(snapshot.Game.ReaderType),
            GetGameStateLabel(snapshot.Game.State));
        if (includePrerequisiteStatus)
        {
            PrerequisiteStatus = string.IsNullOrWhiteSpace(snapshot.Game.Prerequisite.Detail)
                ? snapshot.Game.Prerequisite.Code
                : $"{snapshot.Game.Prerequisite.Code} | {snapshot.Game.Prerequisite.Detail}";
            GameStatusBrush = string.IsNullOrWhiteSpace(snapshot.Game.Name)
                ? Brushes.Gray
                : snapshot.Game.Prerequisite.IsHealthy ? Brushes.ForestGreen : Brushes.IndianRed;
        }

        TransportStatusLabel = "UDP";
        if (snapshot.Udp == null && snapshot.LocalBridge is not null)
        {
            TransportStatusLabel = I18NLoader.Instance["diagnostics.transportBridge"];
            UdpStatus = GetLocalBridgeLabel(snapshot.LocalBridge.Name);
            UdpActivity = snapshot.LocalBridge.IsInjected
                ? I18NLoader.Instance["game.acr.diagnostics.bridgeInjected"]
                : snapshot.LocalBridge.IsEnabled
                    ? I18NLoader.Instance["game.acr.diagnostics.bridgeNotInjected"]
                    : I18NLoader.Instance["game.acr.diagnostics.bridgeDisabled"];
            UdpTooltip = snapshot.LocalBridge.Detail;
            UdpStatusBrush = snapshot.LocalBridge.IsInjected
                ? Brushes.ForestGreen
                : !snapshot.LocalBridge.IsAvailable && snapshot.LocalBridge.IsEnabled
                    ? Brushes.IndianRed
                    : Brushes.Gray;
        }
        else if (snapshot.Udp == null)
        {
            UdpStatus = I18NLoader.Instance["diagnostics.notApplicable"];
            UdpActivity = string.Empty;
            UdpTooltip = UdpStatus;
            UdpStatusBrush = Brushes.Gray;
        }
        else
        {
            var listeners = new List<UdpListenerDiagnosticsInfo>
            {
                new(
                    snapshot.Udp.Name,
                    snapshot.Udp.IsListening,
                    snapshot.Udp.IsOptional,
                    snapshot.Udp.Endpoint,
                    snapshot.Udp.SecondsWithoutMessage,
                    snapshot.Udp.PacketCount,
                    snapshot.Udp.LastPacketUtc,
                    snapshot.Udp.LastPacketSize,
                    snapshot.Udp.LastErrorUtc,
                    snapshot.Udp.LastError)
            };
            listeners.AddRange(snapshot.Udp.AdditionalListeners);

            var hasMultipleListeners = listeners.Count > 1;
            var listenerDetails = new List<string>();
            var allListenersAreListening = true;
            var hasListenerError = false;

            foreach (var listener in listeners)
            {
                var endpoint = GetUdpEndpoint(listener, snapshot.Game.ExpectedEndpoint);
                var state = GetUdpListenerState(listener);
                var activity = $"{listener.PacketCount} | {FormatTime(listener.LastPacketUtc)} | {listener.LastPacketSize} B";
                if (!string.IsNullOrWhiteSpace(listener.LastError))
                {
                    activity += $" | {listener.LastError}";
                    hasListenerError = true;
                }

                listenerDetails.Add(string.Join(
                    " | ",
                    listener.Name,
                    state,
                    endpoint,
                    $"{listener.SecondsWithoutMessage}s",
                    activity));
                allListenersAreListening &= listener.IsListening;
            }

            UdpTooltip = string.Join(Environment.NewLine, listenerDetails);

            if (!snapshot.Game.IsInitialized)
            {
                if (hasMultipleListeners)
                {
                    UdpStatus = string.Join(
                        " | ",
                        listeners.Select(listener => $"{GetCompactUdpName(listener.Name)} {GetCompactEndpoint(GetUdpEndpoint(listener, snapshot.Game.ExpectedEndpoint))}"));
                    UdpActivity = I18NLoader.Instance["diagnostics.waitingForGame"];
                }
                else
                {
                    var endpoint = GetUdpEndpoint(listeners[0], snapshot.Game.ExpectedEndpoint);
                    UdpStatus = string.Join(
                        " | ",
                        I18NLoader.Instance["diagnostics.waitingForGame"],
                        endpoint);
                    UdpActivity = string.Empty;
                }
                UdpStatusBrush = Brushes.Gray;
            }
            else
            {
                if (hasMultipleListeners)
                {
                    UdpStatus = string.Join(
                        " | ",
                        listeners.Select(listener => $"{GetCompactUdpName(listener.Name)} {GetCompactEndpoint(GetUdpEndpoint(listener, snapshot.Game.ExpectedEndpoint))}"));
                    UdpActivity = string.Join(
                        " | ",
                        listeners.Select(listener => $"{GetCompactUdpName(listener.Name)} {GetUdpListenerState(listener)} {listener.SecondsWithoutMessage}s"));
                }
                else
                {
                    var listener = listeners[0];
                    var endpoint = GetUdpEndpoint(listener, snapshot.Game.ExpectedEndpoint);
                    UdpStatus = string.Join(
                        " | ",
                        GetUdpListenerState(listener),
                        endpoint,
                        $"{listener.SecondsWithoutMessage}s");
                    UdpActivity = $"{listener.PacketCount} | {FormatTime(listener.LastPacketUtc)} | {listener.LastPacketSize} B";
                    if (!string.IsNullOrWhiteSpace(listener.LastError))
                    {
                        UdpActivity += $" | {listener.LastError}";
                    }
                }
                UdpStatusBrush = hasListenerError
                    ? Brushes.IndianRed
                    : allListenersAreListening ? Brushes.ForestGreen : Brushes.Gray;
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

    private static string GetGameTransportLabel(string transport)
    {
        return string.Equals(transport, "Shared memory or game-specific", StringComparison.Ordinal)
            ? I18NLoader.Instance["diagnostics.transportSharedMemory"]
            : transport;
    }

    private static string GetGameReaderLabel(string readerType)
    {
        return string.Equals(readerType, "ACRGameDataReader", StringComparison.Ordinal)
            ? I18NLoader.Instance["game.acr.diagnostics.reader"]
            : readerType;
    }

    private static string GetGameStateLabel(string state)
    {
        return state switch
        {
            nameof(GameState.Unknown) => I18NLoader.Instance["diagnostics.gameStateUnknown"],
            nameof(GameState.RaceBegin) => I18NLoader.Instance["diagnostics.gameStateRaceBegin"],
            nameof(GameState.CountingDown) => I18NLoader.Instance["diagnostics.gameStateCountingDown"],
            nameof(GameState.Racing) => I18NLoader.Instance["diagnostics.gameStateRacing"],
            nameof(GameState.Paused) => I18NLoader.Instance["diagnostics.gameStatePaused"],
            nameof(GameState.RaceEnd) => I18NLoader.Instance["diagnostics.gameStateRaceEnd"],
            nameof(GameState.AdHocRaceBegin) => I18NLoader.Instance["diagnostics.gameStateAdHocRaceBegin"],
            _ => state
        };
    }

    private static string GetLocalBridgeLabel(string name)
    {
        return string.Equals(name, "ACR live bridge", StringComparison.Ordinal)
            ? I18NLoader.Instance["game.acr.diagnostics.liveBridge"]
            : name;
    }

    private static string GetUdpEndpoint(UdpListenerDiagnosticsInfo listener, string primaryFallbackEndpoint)
    {
        return string.IsNullOrWhiteSpace(listener.Endpoint)
            ? primaryFallbackEndpoint
            : listener.Endpoint;
    }

    private static string GetUdpListenerState(UdpListenerDiagnosticsInfo listener)
    {
        return listener.IsListening
            ? I18NLoader.Instance["diagnostics.listening"]
            : listener.IsOptional
                ? $"{I18NLoader.Instance["diagnostics.optional"]} | {I18NLoader.Instance["diagnostics.stopped"]}"
                : I18NLoader.Instance["diagnostics.stopped"];
    }

    private static string GetCompactUdpName(string name)
    {
        return string.Equals(name, "Rally bridge", StringComparison.Ordinal)
            ? "Bridge"
            : name;
    }

    private static string GetCompactEndpoint(string endpoint)
    {
        var separatorIndex = endpoint.LastIndexOf(':');
        return separatorIndex >= 0 && separatorIndex < endpoint.Length - 1
            ? endpoint[(separatorIndex + 1)..]
            : endpoint;
    }
}
