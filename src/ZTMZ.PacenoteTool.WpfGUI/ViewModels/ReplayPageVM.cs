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
using ZTMZ.PacenoteTool.WpfGUI.Services;
using ZTMZ.PacenoteTool.WpfGUI.Views;
namespace ZTMZ.PacenoteTool.WpfGUI.ViewModels;

public partial class ReplayPageVM : ObservableObject, INavigationAware
{
    private readonly INavigationService navigationService;
    private readonly ReplayPlayingPageVM replayPlayingPageVM;
    private readonly ZTMZ.PacenoteTool.Core.ZTMZPacenoteTool tool;

    [ObservableProperty]
    private IList<object> _replays = new ObservableCollection<object>();

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

    public void OnNavigatedFrom()
    {
    }

    public async void OnNavigatedTo()
    {
        // load all replays according to current game
        var replays = await ReplayManager.Instance.getReplays(this.tool.CurrentGame);
        _replays.Clear();
        foreach (var replay in replays)
        {
            _replays.Add(new {
                id = replay.id,
                track = replay.track,
                car = replay.car,
                car_class = replay.car_class,
                finish_time = replay.finish_time,
                date = replay.date,
                comment = replay.comment,
                video_path = replay.video_path,
            });
        }
    }

    [RelayCommand]
    private async void ReplayPlay(int id)
    {
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
    private async void ReplayExport(int id)
    {
        var folder = await getExportFolder();
        if (string.IsNullOrEmpty(folder)) {
            return;
        }
        // export to the foler
        var game = this.tool.CurrentGame;
        var replay = await ReplayManager.Instance.getReplay(id);
        await ReplayManager.Instance.ExportReplay(game, replay, folder);
    }

    [RelayCommand]
    private async void ReplayExportAudio(int id) {
        if (!File.Exists(Path.Combine(Config.Instance.ReplayFFmpegPath, "ffmpeg.exe"))) {
            // show message box
            var result = await new Wpf.Ui.Controls.MessageBox
            {
                Title = I18NLoader.Instance["dialog.common.file_not_found"],
                Content = I18NLoader.Instance["dialog.ffmpeg.no_ffmpeg"],
                // SecondaryButtonText = "Don't Save",
                CloseButtonText = I18NLoader.Instance["dialog.common.btn_ok"]
            }.ShowDialogAsync();
            return;
        }

        GlobalFFOptions.Configure(options => options.BinaryFolder = Config.Instance.ReplayFFmpegPath);
        var folder = await getExportFolder();
        if (string.IsNullOrEmpty(folder)) {
            return;
        }
        // export to the foler
        var game = this.tool.CurrentGame;
        var replay = await ReplayManager.Instance.getReplay(id);
        await ReplayManager.Instance.ExportReplayWithAudio(game, replay, folder);
    }

    [RelayCommand]
    private async void ReplayDelete(int id)
    {
        await Task.Delay(1000);
    }
}
