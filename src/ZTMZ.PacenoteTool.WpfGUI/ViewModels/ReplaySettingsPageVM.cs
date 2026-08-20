using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Wpf.Ui.Controls;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.UI.Windows;
using ZTMZ.PacenoteTool.WpfGUI.Services;
namespace ZTMZ.PacenoteTool.WpfGUI.ViewModels;

public partial class ReplaySettingsPageVM : ObservableObject, INavigationAware
{
    private readonly ObsManager _obsManager;

    public ReplaySettingsPageVM()
    {
        _obsManager = ObsManager.Instance;
        _obsManager.ConnectionStatusChanged += OnObsConnectionStatusChanged;
        ApplyObsConnectionStatus(_obsManager.ConnectionStatus);
    }

    [ObservableProperty]
    private bool _replaySave = Config.Instance.ReplaySave;
    partial void OnReplaySaveChanged(bool value)
    {
        Config.Instance.ReplaySave = value;
        Config.Instance.SaveUserConfig();
    }

    [ObservableProperty]
    private int _replayPreferredFilter = Config.Instance.ReplayPreferredFilter;
    partial void OnReplayPreferredFilterChanged(int value)
    {
        Config.Instance.ReplayPreferredFilter = value;
        Config.Instance.SaveUserConfig();
    }

    [ObservableProperty]
    private float _replayStoredCountLimit = Config.Instance.ReplayStoredCountLimit;
    partial void OnReplayStoredCountLimitChanged(float value)
    {
        Config.Instance.ReplayStoredCountLimit = Convert.ToInt32(value);
        Config.Instance.SaveUserConfig();
    }

    [ObservableProperty]
    private int _replaySaveInterval = Config.Instance.ReplaySaveInterval;
    partial void OnReplaySaveIntervalChanged(int value)
    {
        var normalized = Math.Max(1, value);
        if (normalized != value)
        {
            ReplaySaveInterval = normalized;
            return;
        }

        Config.Instance.ReplaySaveInterval = normalized;
        Config.Instance.SaveUserConfig();
    }

    [ObservableProperty]
    private bool _onlineRivalsEnabled = Config.Instance.OnlineRivalsEnabled;
    partial void OnOnlineRivalsEnabledChanged(bool value)
    {
        Config.Instance.OnlineRivalsEnabled = value;
        Config.Instance.SaveUserConfig();
    }

    [ObservableProperty]
    private bool _ReplayOBSSave = Config.Instance.ReplayOBSSave;
    partial void OnReplayOBSSaveChanged(bool value)
    {
        Config.Instance.ReplayOBSSave = value;
        Config.Instance.SaveUserConfig();
        _obsManager.RefreshConnectionStatus();
        UpdateObsConnectionRefreshAvailability();

        if (value)
        {
            _ = RefreshObsConnection();
        }
    }

    [ObservableProperty]
    private string _obsConnectionStatus = string.Empty;

    [ObservableProperty]
    private Brush _obsConnectionStatusBrush = Brushes.Gray;

    [ObservableProperty]
    private bool _isRefreshingObsConnection;

    [ObservableProperty]
    private bool _canRefreshObsConnection = Config.Instance.ReplayOBSSave;

    partial void OnIsRefreshingObsConnectionChanged(bool value)
    {
        UpdateObsConnectionRefreshAvailability();
    }

    [ObservableProperty]
    private string _replayOBSWebsocketUrl = Config.Instance.ReplayOBSWebsocketUrl;
    partial void OnReplayOBSWebsocketUrlChanged(string value)
    {
        Config.Instance.ReplayOBSWebsocketUrl = value;
        Config.Instance.SaveUserConfig();
    }

    [ObservableProperty]
    private string _replayOBSWebsocketPassword = Config.Instance.ReplayOBSWebsocketPassword;
    partial void OnReplayOBSWebsocketPasswordChanged(string value)
    {
        Config.Instance.ReplayOBSWebsocketPassword = value;
        Config.Instance.SaveUserConfig();
    }

    [ObservableProperty]
    private int _replayOBSWebsocketTimeout = Config.Instance.ReplayOBSWebsocketTimeout;
    partial void OnReplayOBSWebsocketTimeoutChanged(int value)
    {
        Config.Instance.ReplayOBSWebsocketTimeout = value;
        Config.Instance.SaveUserConfig();
    }

    [ObservableProperty]
    private bool _replayDeleteRelatedVideo = Config.Instance.ReplayDeleteRelatedVideo;
    partial void OnReplayDeleteRelatedVideoChanged(bool value)
    {
        Config.Instance.ReplayDeleteRelatedVideo = value;
        Config.Instance.SaveUserConfig();
    }

    [ObservableProperty]
    private bool _replaySaveWithoutInterval = Config.Instance.ReplaySaveWithoutInterval;
    partial void OnReplaySaveWithoutIntervalChanged(bool value)
    {
        ReplaySaveIntervalVisibility = value ? Visibility.Collapsed : Visibility.Visible;
        Config.Instance.ReplaySaveWithoutInterval = value;
        Config.Instance.SaveUserConfig();
    }

    [ObservableProperty]
    private string _replayFFmpegPath = Config.Instance.ReplayFFmpegPath;
    partial void OnReplayFFmpegPathChanged(string value)
    {
        Config.Instance.ReplayFFmpegPath = value;
        Config.Instance.SaveUserConfig();
    }

    [ObservableProperty]
    private Visibility _replaySaveIntervalVisibility = Config.Instance.ReplaySaveWithoutInterval ? Visibility.Collapsed : Visibility.Visible;

    [ObservableProperty]
    private bool _replayCleanUpAbnormalVideo = Config.Instance.ReplayCleanUpAbnormalVideo;
    partial void OnReplayCleanUpAbnormalVideoChanged(bool value)
    {
        Config.Instance.ReplayCleanUpAbnormalVideo = value;
        Config.Instance.SaveUserConfig();
    }
    
    public Task OnNavigatedFromAsync() => Task.CompletedTask;

    public Task OnNavigatedToAsync()
    {
        ApplyObsConnectionStatus(_obsManager.ConnectionStatus);
        if (ReplayOBSSave)
        {
            _ = RefreshObsConnection();
        }

        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task RefreshObsConnection()
    {
        if (IsRefreshingObsConnection)
        {
            return;
        }

        IsRefreshingObsConnection = true;
        try
        {
            await _obsManager.Connect();
        }
        finally
        {
            IsRefreshingObsConnection = false;
            ApplyObsConnectionStatus(_obsManager.ConnectionStatus);
        }
    }

    private void OnObsConnectionStatusChanged(ObsConnectionStatus status)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted)
        {
            return;
        }

        if (!dispatcher.CheckAccess())
        {
            _ = dispatcher.BeginInvoke(() => ApplyObsConnectionStatus(status));
            return;
        }

        ApplyObsConnectionStatus(status);
    }

    private void ApplyObsConnectionStatus(ObsConnectionStatus status)
    {
        ObsConnectionStatus = status.State switch
        {
            ObsConnectionState.Disabled => I18NLoader.Instance["settings.obsConnectionDisabled"],
            ObsConnectionState.Connecting => I18NLoader.Instance["settings.obsConnectionConnecting"],
            ObsConnectionState.Connected => I18NLoader.Instance["settings.obsConnectionConnected"],
            ObsConnectionState.Failed => string.Format(
                I18NLoader.Instance["settings.obsConnectionFailed"],
                status.ErrorMessage),
            _ => I18NLoader.Instance["settings.obsConnectionDisconnected"]
        };
        ObsConnectionStatusBrush = status.State switch
        {
            ObsConnectionState.Connected => Brushes.ForestGreen,
            ObsConnectionState.Connecting => Brushes.DodgerBlue,
            ObsConnectionState.Failed => Brushes.IndianRed,
            _ => Brushes.Gray
        };
    }

    private void UpdateObsConnectionRefreshAvailability()
    {
        CanRefreshObsConnection = ReplayOBSSave && !IsRefreshingObsConnection;
    }

    [RelayCommand]
    private async void LocateFFmpeg() {
        OpenFileDialog openFileDialog = new OpenFileDialog();
        openFileDialog.Filter = "FFmpeg|ffmpeg.exe";
        if (openFileDialog.ShowDialog() == true)
        {
            ReplayFFmpegPath = Path.GetDirectoryName(openFileDialog.FileName);
        }
    }

    [RelayCommand]
    private async void DownloadFFmpeg() {
        // download ffmpeg
        var url = "https://gitee.com/ztmz/opensource_tools/releases/tag/ffmpeg";
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}
