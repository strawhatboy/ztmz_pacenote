
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

namespace ZTMZ.PacenoteTool.WpfGUI.ViewModels;

public partial class VoicePageVM : ObservableObject {

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
                where !needUpdateOrDownload.Any(x => x.DisplayText == p.Info.DisplayText)
                select new CodriverPackageUpdateFile(p.Info) { needUpdate = false, needDownload = false }).ToList();
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
    private async void UpdateCodriverPkg(string pkgDisplayText)
    {
        // download and install the voice package
        // show progress bar
        // show success or failure message
        var pkg = VoicePackages.FirstOrDefault(p => p.DisplayText == pkgDisplayText);
        if (pkg != null) {
            pkg.isDownloading = true;
            pkg.needUpdate = false;
            pkg.needDownload = false;
            // download and install
            FileDownloader fd = new();
            var progress = new Progress<float>(p => {
                pkg.downloadProgress = p;   // update progress bar
            });
            var downloadedFiles = await fd.DownloadFiles(new List<string> { pkg.url }, progress);
            var downloadedFile = downloadedFiles[pkg.url];
            pkg.isDownloading = false;
            pkg.isInstalling = true;
            // install, unzip to the voice package folder
            await Task.Run(() => System.IO.Compression.ZipFile.ExtractToDirectory(downloadedFile, Path.Join(AppContext.BaseDirectory, Constants.PATH_CODRIVERS), true));
            pkg.isInstalling = false;
        }
    }
}
