using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Wpf.Ui.Controls;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.UI;
using ZTMZ.PacenoteTool.WpfGUI.Services;
namespace ZTMZ.PacenoteTool.WpfGUI.ViewModels;

public partial class ReplaySettingsPageVM : ObservableObject, INavigationAware
{
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
        Config.Instance.ReplaySaveInterval = value;
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
    
    public void OnNavigatedFrom()
    {
        // leaving, try to connect to obs here
        ObsManager.Instance.Connect();
    }

    public void OnNavigatedTo()
    {
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
