using System.Collections.Generic;
using System.IO;
using System.Windows.Controls;
using System.Windows.Media;
using Wpf.Ui.Controls;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.UI;
using ZTMZ.PacenoteTool.WpfGUI.Services;
namespace ZTMZ.PacenoteTool.WpfGUI.ViewModels;

public partial class ReplayPageVM : ObservableObject, INavigationAware
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

    public void OnNavigatedFrom()
    {
    }

    public void OnNavigatedTo()
    {
    }
}
