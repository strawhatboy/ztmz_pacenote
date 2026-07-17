using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;
using FFMpegCore;
using Microsoft.Win32;
using Wpf.Ui.Controls;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.UI;
using ZTMZ.PacenoteTool.WpfGUI.Models;
using ZTMZ.PacenoteTool.WpfGUI.Views;

namespace ZTMZ.PacenoteTool.WpfGUI.ViewModels;

public partial class ReplayPageVM : ObservableObject, INavigationAware
{
    private readonly INavigationService _navigationService;
    private readonly ReplayPlayingPageVM _replayPlayingPageVM;
    private readonly ZTMZ.PacenoteTool.Core.ZTMZPacenoteTool _tool;
    private readonly object _collectionLock = new();

    [ObservableProperty]
    private ObservableCollection<ReplayModel> _replays = new();

    [ObservableProperty]
    private string _searchText = string.Empty;

    public ICollectionView ReplaysView { get; }

    public ReplayPageVM(
        INavigationService navigationService,
        ReplayPlayingPageVM replayPlayingPageVM,
        ZTMZ.PacenoteTool.Core.ZTMZPacenoteTool tool)
    {
        _navigationService = navigationService;
        _replayPlayingPageVM = replayPlayingPageVM;
        _tool = tool;

        BindingOperations.EnableCollectionSynchronization(Replays, _collectionLock);
        ReplaysView = CollectionViewSource.GetDefaultView(Replays);
        ReplaysView.Filter = MatchesSearch;
        ReplaysView.SortDescriptions.Add(new SortDescription(nameof(ReplayModel.Date), ListSortDirection.Descending));
    }

    partial void OnSearchTextChanged(string value)
    {
        ReplaysView.Refresh();
    }

    [RelayCommand]
    private void NavigateForward(Type type)
    {
        _ = _navigationService.NavigateWithHierarchy(type);
    }

    public Task OnNavigatedFromAsync()
    {
        return Task.CompletedTask;
    }

    public async Task OnNavigatedToAsync()
    {
        var replays = await ReplayManager.Instance.getReplays(_tool.CurrentGame);
        Replays.Clear();
        foreach (var replay in replays.OrderByDescending(replay => replay.date))
        {
            Replays.Add(new ReplayModel
            {
                Id = replay.id,
                Track = replay.track,
                Car = replay.car,
                Car_class = replay.car_class,
                Finish_time = replay.finish_time,
                Date = replay.date,
                Comment = replay.comment,
                Video_path = replay.video_path,
                Locked = replay.locked,
                Available = replay.available,
                HasVideo = !string.IsNullOrWhiteSpace(replay.video_path) && File.Exists(replay.video_path)
            });
        }

        ReplaysView.Refresh();
    }

    [RelayCommand]
    private async Task ReplayPlay(int id)
    {
        var replay = Replays.FirstOrDefault(item => item.Id == id);
        if (replay?.Available != true)
        {
            return;
        }

        if (!replay.HasVideo && !await CheckFfmpegAsync())
        {
            return;
        }

        _replayPlayingPageVM.ReplayId = id;
        _navigationService.NavigateWithHierarchy(typeof(ReplayPlayingPage));
    }

    [RelayCommand]
    private async Task ReplayExport(int id)
    {
        if (Replays.FirstOrDefault(replay => replay.Id == id)?.Available != true)
        {
            return;
        }

        var folder = GetExportFolder();
        if (string.IsNullOrEmpty(folder))
        {
            return;
        }

        try
        {
            var replay = await ReplayManager.Instance.getReplay(id);
            await ReplayManager.Instance.ExportReplay(_tool.CurrentGame, replay, folder);
        }
        catch (Exception ex)
        {
            await ShowReplayActionErrorAsync(ex);
        }
    }

    [RelayCommand]
    private async Task ReplayExportAudio(int id)
    {
        if (Replays.FirstOrDefault(replay => replay.Id == id)?.Available != true || !await CheckFfmpegAsync())
        {
            return;
        }

        GlobalFFOptions.Configure(options => options.BinaryFolder = Config.Instance.ReplayFFmpegPath);
        var folder = GetExportFolder();
        if (string.IsNullOrEmpty(folder))
        {
            return;
        }

        try
        {
            var replay = await ReplayManager.Instance.getReplay(id);
            await ReplayManager.Instance.ExportReplayWithAudio(_tool.CurrentGame, replay, folder);
        }
        catch (Exception ex)
        {
            await ShowReplayActionErrorAsync(ex);
        }
    }

    [RelayCommand]
    private async Task ReplayLock(int id)
    {
        var replay = Replays.FirstOrDefault(item => item.Id == id);
        if (replay == null)
        {
            return;
        }

        replay.Locked = true;
        try
        {
            await ReplayManager.Instance.LockReplayAsync(id);
        }
        catch (Exception ex)
        {
            replay.Locked = false;
            await ShowReplayActionErrorAsync(ex);
        }
    }

    [RelayCommand]
    private async Task ReplayUnlock(int id)
    {
        var replay = Replays.FirstOrDefault(item => item.Id == id);
        if (replay == null)
        {
            return;
        }

        replay.Locked = false;
        try
        {
            await ReplayManager.Instance.UnlockReplayAsync(id);
        }
        catch (Exception ex)
        {
            replay.Locked = true;
            await ShowReplayActionErrorAsync(ex);
        }
    }

    private bool MatchesSearch(object item)
    {
        if (item is not ReplayModel replay || string.IsNullOrWhiteSpace(SearchText))
        {
            return item is ReplayModel;
        }

        return replay.SearchText.Contains(SearchText.Trim(), StringComparison.CurrentCultureIgnoreCase);
    }

    private static string GetExportFolder()
    {
#if NET8_0_OR_GREATER
        var dialog = new OpenFolderDialog { Multiselect = false };
        return dialog.ShowDialog() == true && dialog.FolderNames.Length > 0
            ? dialog.FolderNames.First()
            : string.Empty;
#else
        return string.Empty;
#endif
    }

    private async Task<bool> CheckFfmpegAsync()
    {
        if (File.Exists(Path.Combine(Config.Instance.ReplayFFmpegPath, "ffmpeg.exe")))
        {
            return true;
        }

        await new Wpf.Ui.Controls.MessageBox
        {
            Title = I18NLoader.Instance["dialog.common.file_not_found"],
            Content = I18NLoader.Instance["dialog.ffmpeg.no_ffmpeg"],
            CloseButtonText = I18NLoader.Instance["dialog.common.btn_ok"]
        }.ShowDialogAsync();
        return false;
    }

    private async Task ShowReplayActionErrorAsync(Exception ex)
    {
        await new Wpf.Ui.Controls.MessageBox
        {
            Title = I18NLoader.Instance["replay.actionFailed"],
            Content = ex.Message,
            CloseButtonText = I18NLoader.Instance["dialog.common.btn_ok"]
        }.ShowDialogAsync();
    }
}
