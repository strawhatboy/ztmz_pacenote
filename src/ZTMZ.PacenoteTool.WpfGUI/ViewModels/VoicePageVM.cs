
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

namespace ZTMZ.PacenoteTool.WpfGUI.ViewModels;

public partial class VoicePageVM : ObservableObject {

    private static readonly Logger logger = LogManager.GetCurrentClassLogger();
    private readonly ZTMZ.PacenoteTool.Core.ZTMZPacenoteTool tool;
    private readonly VoicePackagePageVM voicePackagePageVM;
    private readonly INavigationService navigationService;

    [ObservableProperty]
    private ObservableCollection<CodriverPackageUpdateFile> _voicePackages = new ();

    private object _collectionLock = new();

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
                select new CodriverPackageUpdateFile(p.Info) { needUpdate = false, needDownload = false, isAvailable = true }).ToList();
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
            });
            pkg.IsInstalling = false;
            pkg.IsAvailable = true;

            // update the tool's voice packages list?
            this.tool.RefreshCodrivers();
        }
    }
}
