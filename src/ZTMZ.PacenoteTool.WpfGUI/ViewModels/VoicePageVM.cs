
using System.Collections.Generic;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;
using System.Windows.Data;
using System.Threading.Tasks;
using ZTMZ.PacenoteTool.Base.UI.Game;
using Wpf.Ui.Controls;
using System.Linq;
using System.Reflection;
using System.Windows.Controls;
using Microsoft.Win32;
using ZTMZ.PacenoteTool.WpfGUI.Services;
using ZTMZ.PacenoteTool.WpfGUI.Models;
using System.IO;
using System.Text;
using SevenZipExtractor;
using NLog;
using System.Runtime.CompilerServices;

namespace ZTMZ.PacenoteTool.WpfGUI.ViewModels;

public partial class VoicePageVM : ObservableObject {

    private static readonly Logger logger = LogManager.GetCurrentClassLogger();
    private readonly ZTMZ.PacenoteTool.Core.ZTMZPacenoteTool tool;
    private readonly VoicePackagePageVM voicePackagePageVM;
    private readonly INavigationService navigationService;

    [ObservableProperty]
    private ObservableCollection<CodriverPackageUpdateFile> _voicePackages = new ();

    private object _collectionLock = new();

    private object _updateLock = new();
    private Queue<CodriverPackageUpdateFile> _updateQueue = new();

    public VoicePageVM(Core.ZTMZPacenoteTool tool, 
    VoicePackagePageVM voicePackagePageVM,
    INavigationService navigationService,
    UpdateService updateService) {
        this.tool = tool;
        this.voicePackagePageVM = voicePackagePageVM;
        this.navigationService = navigationService;

        BindingOperations.EnableCollectionSynchronization(VoicePackages, _collectionLock);

        // TODO: need to check the voice pkgs online and update the list
        // provide update, download/install, create, export, import options
        // import/export can be done by file dialog or drag and drop
        // import/export support zip file format

        VoicePackages.Clear();
        Task.Run(async () => {
            var needUpdatePkgs = await updateService.CheckCodriverPackagesUpdate((from p in tool.CoDriverPackages select p.Info).ToList());
            var needUpdateOrDownload = (from p in needUpdatePkgs where p.needUpdate || p.needDownload select p).ToList();
            foreach (var pkg in needUpdateOrDownload) {
                VoicePackages.Add(pkg);
            }

            // local
            var local = (from p in tool.CoDriverPackages
                where !needUpdateOrDownload.Any(x => x.Id == p.Info.id)
                select new CodriverPackageUpdateFile(p.Info) { NeedUpdate = false, NeedDownload = false, IsAvailable = true, Version=p.Info.version }).ToList();
            foreach (var pkg in local) {
                VoicePackages.Add(pkg);
            }

        });
    }

    [RelayCommand]
    private void NavigateForward(Type type)
    {
        _ = navigationService.NavigateWithHierarchy(type);
    }

    [RelayCommand]
    private void NavigateToVoicePackagePage(string voicePkgPath)
    {
        // set the voice package path in the view model and then navigate to the next page
        voicePackagePageVM.VoicePackagePath = voicePkgPath;
        _ = navigationService.NavigateWithHierarchy(typeof(ZTMZ.PacenoteTool.WpfGUI.Views.VoicePackagePage));
    }

    [RelayCommand]
    private void NavigateBack()
    {
        _ = navigationService.GoBack();
    }

    [RelayCommand]
    private async void UpdateCodriverPkg(string id)
    {
        // download and install the voice package
        // show progress bar
        // show success or failure message
        var pkg = VoicePackages.FirstOrDefault(p => p.id == id);
        if (pkg != null) {
            pkg.IsDownloading = true;
            pkg.NeedUpdate = false;
            pkg.NeedDownload = false;
            lock (_updateLock) {
                _updateQueue.Enqueue(pkg);
            }
            // download and install
            FileDownloader fd = new();
            var progress = new Progress<float>(p => {
                pkg.DownloadProgress = p;   // update progress bar
            });
            var downloadedFiles = await fd.DownloadFiles(new List<string> { pkg.Url }, progress);
            var downloadedFile = downloadedFiles[pkg.url];
            pkg.IsDownloading = false;
            pkg.IsInstalling = true;
            // install, unzip to the voice package folder
            await Task.Run(() => {
                var pathes = Directory.GetDirectories(AppLevelVariables.Instance.GetPath(Constants.PATH_CODRIVERS));
                if (Directory.Exists(pkg.Path)) {
                    try {
                        Directory.Delete(pkg.Path, true);  // delete if exists
                    } catch (Exception e) {
                        // failed to delete, could be in use
                        logger.Warn(e, $"Failed to delete the existing voice package folder {pkg.Path}");
                    }
                }
                using (ArchiveFile f = new(downloadedFile)) {
                    f.Extract(AppLevelVariables.Instance.GetPath(Constants.PATH_CODRIVERS), true);
                }
                var new_pathes = Directory.GetDirectories(AppLevelVariables.Instance.GetPath(Constants.PATH_CODRIVERS));
                var new_path = new_pathes.Except(pathes).FirstOrDefault();
                if (new_path != null) {
                    pkg.Path = new_path;
                }
            });
            pkg.IsInstalling = false;
            pkg.IsAvailable = true;

            await refreshCodrivers();
        }
    }

    private async Task refreshCodrivers() {
        lock (_updateQueue) {
            _updateQueue.Dequeue();
            if (_updateQueue.Count == 0) {
                // update the tool's voice packages list? when this is the last updating one
                this.tool.RefreshCodrivers();
            }
        }
    }

    [RelayCommand]
    private async void ExportCodriverPkg(string id) {
        var pkg = VoicePackages.FirstOrDefault(p => p.id == id);
        if (pkg == null) {
            return;
        }
        var pkgLocal = tool.CoDriverPackages.FirstOrDefault(p => p.Info.id == id);
        if (pkgLocal == null) {
            return;
        }
        // save file dialog
        SaveFileDialog saveFileDialog = new();
        saveFileDialog.Filter = "zpak files (*.zpak)|*.zpak";
        saveFileDialog.FilterIndex = 1;
        saveFileDialog.RestoreDirectory = true;
        saveFileDialog.FileName = $"{pkg.Name}.zpak";

        if (saveFileDialog.ShowDialog() == true) {
            pkg.IsExporting = true;
            await pkgLocal.Export(saveFileDialog.FileName);
            pkg.IsExporting = false;
        }
    }

    [RelayCommand]
    private async void ImportAudioPackage() {
        // open file dialog
        OpenFileDialog openFileDialog = new();
        openFileDialog.Filter = "zpak files (*.zpak)|*.zpak";
        openFileDialog.FilterIndex = 1;
        openFileDialog.RestoreDirectory = true;
        openFileDialog.Multiselect = false;

        if (openFileDialog.ShowDialog() == true) {
            // unzip to the voice package folder
            var path = openFileDialog.FileName;
            var pkgLocal = await CoDriverPackage.Import(path);
            var pkg = new CodriverPackageUpdateFile(pkgLocal.Info);
            if (pkg == null) {
                return;
            }
            lock (_updateQueue) {
                _updateQueue.Enqueue(pkg);
            }
            pkg.IsAvailable = true;
            VoicePackages.Add(pkg);

            await refreshCodrivers();
        }
    }
}
