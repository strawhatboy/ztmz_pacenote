using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using FFMpegCore;
using Microsoft.Win32;
using Wpf.Ui.Controls;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.UI;
using ZTMZ.PacenoteTool.WpfGUI.Models;
using ZTMZ.PacenoteTool.WpfGUI.Services;
using ZTMZ.PacenoteTool.WpfGUI.Views;
namespace ZTMZ.PacenoteTool.WpfGUI.ViewModels;

public partial class ReplayPageVM : ObservableObject, INavigationAware
{
    private readonly INavigationService navigationService;
    private readonly ReplayPlayingPageVM replayPlayingPageVM;
    private readonly ZTMZ.PacenoteTool.Core.ZTMZPacenoteTool tool;

    [ObservableProperty]
    private IList<ReplayModel> _replays = new ObservableCollection<ReplayModel>();

    private object _collectionLock = new();

    public ReplayPageVM(INavigationService navigationService, 
    ReplayPlayingPageVM replayPlayingPageVM,
    ZTMZ.PacenoteTool.Core.ZTMZPacenoteTool tool)
    {
        
        BindingOperations.EnableCollectionSynchronization(Replays, _collectionLock);
        this.tool = tool;
        this.replayPlayingPageVM = replayPlayingPageVM;
        this.navigationService = navigationService;
    }


    [RelayCommand]
    private void NavigateForward(Type type)
    {
        _ = navigationService.NavigateWithHierarchy(type);
    }

    public Task OnNavigatedFromAsync() => Task.CompletedTask;

    public async Task OnNavigatedToAsync()
    {
        // load all replays according to current game
        var replays = await ReplayManager.Instance.getReplays(this.tool.CurrentGame);
        Replays.Clear();
        foreach (var replay in replays)
        {
            Replays.Add(new ReplayModel{
                Id = replay.id,
                Track = replay.track,
                Car = replay.car,
                Car_class = replay.car_class,
                Finish_time = replay.finish_time,
                Date = replay.date,
                Comment = replay.comment,
                Video_path = replay.video_path,
                Locked = replay.locked,
                Available = replay.available
            });
        }
    }

    [RelayCommand]
    private async Task ReplayPlay(int id)
    {
        if (Replays.FirstOrDefault(r => r.Id == id)?.Available != true) {
            return;
        }

        if (!await checkFFmepg()) {
            return;
        }
        
        this.replayPlayingPageVM.ReplayId = id;
        // navigate to replay playing page
        navigationService.NavigateWithHierarchy(typeof(ReplayPlayingPage));
    }

    private async Task<string> getExportFolder() {
        
        // open folder dialog
        
#if NET8_0_OR_GREATER

        OpenFolderDialog openFolderDialog =
            new()
            {
                Multiselect = false,
            };

        if (openFolderDialog.ShowDialog() != true)
        {
            return "";
        }

        if (openFolderDialog.FolderNames.Length == 0)
        {
            return "";
        }
        return openFolderDialog.FolderNames.First();
#else
#endif
    }

    [RelayCommand]
    private async Task ReplayExport(int id)
    {
        if (Replays.FirstOrDefault(r => r.Id == id)?.Available != true) {
            return;
        }

        var folder = await getExportFolder();
        if (string.IsNullOrEmpty(folder)) {
            return;
        }

        try {
            var game = this.tool.CurrentGame;
            var replay = await ReplayManager.Instance.getReplay(id);
            await ReplayManager.Instance.ExportReplay(game, replay, folder);
        } catch (Exception ex) {
            await showReplayActionError(ex);
        }
    }

    private async Task<bool> checkFFmepg() {
        if (!File.Exists(Path.Combine(Config.Instance.ReplayFFmpegPath, "ffmpeg.exe"))) {
            // show message box
            var result = await new Wpf.Ui.Controls.MessageBox
            {
                Title = I18NLoader.Instance["dialog.common.file_not_found"],
                Content = I18NLoader.Instance["dialog.ffmpeg.no_ffmpeg"],
                // SecondaryButtonText = "Don't Save",
                CloseButtonText = I18NLoader.Instance["dialog.common.btn_ok"]
            }.ShowDialogAsync();
            return false;
        }
        return true;
    }

    [RelayCommand]
    private async Task ReplayExportAudio(int id) {
        if (Replays.FirstOrDefault(r => r.Id == id)?.Available != true) {
            return;
        }

        if (!await checkFFmepg()) {
            return;
        }

        GlobalFFOptions.Configure(options => options.BinaryFolder = Config.Instance.ReplayFFmpegPath);
        var folder = await getExportFolder();
        if (string.IsNullOrEmpty(folder)) {
            return;
        }
        try {
            var game = this.tool.CurrentGame;
            var replay = await ReplayManager.Instance.getReplay(id);
            await ReplayManager.Instance.ExportReplayWithAudio(game, replay, folder);
        } catch (Exception ex) {
            await showReplayActionError(ex);
        }
    }

    [RelayCommand]
    private async Task ReplayLock(int id)
    {
        var replay = this.Replays.FirstOrDefault(r => r.Id == id);
        if (replay == null) {
            return;
        }

        replay.Locked = true;
        try {
            await ReplayManager.Instance.LockReplayAsync(id);
        } catch (Exception ex) {
            replay.Locked = false;
            await showReplayActionError(ex);
        }
    }
    [RelayCommand]
    private async Task ReplayUnlock(int id)
    {
        var replay = this.Replays.FirstOrDefault(r => r.Id == id);
        if (replay == null) {
            return;
        }

        replay.Locked = false;
        try {
            await ReplayManager.Instance.UnlockReplayAsync(id);
        } catch (Exception ex) {
            replay.Locked = true;
            await showReplayActionError(ex);
        }
    }

    private async Task showReplayActionError(Exception ex) {
        await new Wpf.Ui.Controls.MessageBox
        {
            Title = "Replay action failed",
            Content = ex.Message,
            CloseButtonText = I18NLoader.Instance["dialog.common.btn_ok"]
        }.ShowDialogAsync();
    }
}
