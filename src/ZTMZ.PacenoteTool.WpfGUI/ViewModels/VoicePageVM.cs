
using System.Collections.Generic;
using ZTMZ.PacenoteTool.Base;
using System.Windows.Data;
using System.Threading.Tasks;
using Wpf.Ui.Controls;
using System.Linq;
using Microsoft.Win32;
using ZTMZ.PacenoteTool.WpfGUI.Services;
using ZTMZ.PacenoteTool.WpfGUI.Models;
using ZTMZ.PacenoteTool.Core;

namespace ZTMZ.PacenoteTool.WpfGUI.ViewModels;

public partial class VoicePageVM : ObservableObject {

    private readonly ZTMZ.PacenoteTool.Core.ZTMZPacenoteTool tool;
    private readonly VoicePackagePageVM voicePackagePageVM;
    private readonly INavigationService navigationService;
    private readonly CoDriverPackageCatalogService packageCatalog;
    private readonly CoDriverPackageUpdateService packageUpdates;

    [ObservableProperty]
    private ObservableCollection<CodriverPackageUpdateFile> _voicePackages = new ();

    private object _collectionLock = new();

    private object _updateLock = new();
    private Queue<CodriverPackageUpdateFile> _updateQueue = new();

    public VoicePageVM(Core.ZTMZPacenoteTool tool, 
    VoicePackagePageVM voicePackagePageVM,
    INavigationService navigationService,
    CoDriverPackageCatalogService packageCatalog,
    CoDriverPackageUpdateService packageUpdates) {
        this.tool = tool;
        this.voicePackagePageVM = voicePackagePageVM;
        this.navigationService = navigationService;
        this.packageCatalog = packageCatalog;
        this.packageUpdates = packageUpdates;

        BindingOperations.EnableCollectionSynchronization(VoicePackages, _collectionLock);

        // TODO: need to check the voice pkgs online and update the list
        // provide update, download/install, create, export, import options
        // import/export can be done by file dialog or drag and drop
        // import/export support zip file format

        VoicePackages.Clear();
        Task.Run(async () => {
            var updateStatuses = await packageUpdates.CheckForUpdatesAsync(
                Config.Instance.UpdateDefinitionUrl_AudioPkg,
                tool.CoDriverPackages.Select(package => package.Info).ToList());
            var needUpdateOrDownload = updateStatuses
                .Where(status => status.NeedsUpdate || status.NeedsDownload)
                .Select(CodriverPackageUpdateFile.FromStatus)
                .ToList();
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
    private async Task UpdateCodriverPkg(string id)
    {
        // download and install the voice package
        // show progress bar
        // show success or failure message
        var pkg = VoicePackages.FirstOrDefault(p => p.id == id);
        if (pkg != null) {
            var neededUpdate = pkg.NeedUpdate;
            var neededDownload = pkg.NeedDownload;
            pkg.IsDownloading = true;
            pkg.NeedUpdate = false;
            pkg.NeedDownload = false;
            lock (_updateLock) {
                _updateQueue.Enqueue(pkg);
            }
            var progress = new Progress<CoDriverPackageUpdateProgress>(p => {
                if (p.Phase == CoDriverPackageUpdatePhase.Installing) {
                    pkg.IsDownloading = false;
                    pkg.IsInstalling = true;
                    return;
                }

                pkg.DownloadProgress = (float)(p.Download?.Fraction ?? 0);
            });
            try {
                var installedPackage = await packageUpdates.DownloadAndInstallAsync(
                    pkg.Url,
                    pkg.Path,
                    progress);
                pkg.Path = installedPackage.Info.Path;
                pkg.IsAvailable = true;
            } catch {
                pkg.NeedUpdate = neededUpdate;
                pkg.NeedDownload = neededDownload;
                throw;
            } finally {
                pkg.IsDownloading = false;
                pkg.IsInstalling = false;
                await RefreshCodriversWhenUpdatesCompleteAsync();
            }
        }
    }

    private async Task RefreshCodriversWhenUpdatesCompleteAsync() {
        var shouldRefresh = false;
        lock (_updateLock) {
            if (_updateQueue.Count > 0) {
                _updateQueue.Dequeue();
            }
            shouldRefresh = _updateQueue.Count == 0;
        }

        if (shouldRefresh) {
            await tool.RefreshCodriversAsync();
        }
    }

    [RelayCommand]
    private async Task ExportCodriverPkg(string id) {
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
            await packageCatalog.ExportAsync(pkgLocal, saveFileDialog.FileName);
            pkg.IsExporting = false;
        }
    }

    [RelayCommand]
    private async Task ImportAudioPackage() {
        // open file dialog
        OpenFileDialog openFileDialog = new();
        openFileDialog.Filter = "zpak files (*.zpak)|*.zpak";
        openFileDialog.FilterIndex = 1;
        openFileDialog.RestoreDirectory = true;
        openFileDialog.Multiselect = false;

        if (openFileDialog.ShowDialog() == true) {
            // unzip to the voice package folder
            var path = openFileDialog.FileName;
            var importPlan = packageCatalog.InspectImport(path);
            if (importPlan.DestinationExists) {
                // already exists, try asking if overwrite or cancel
                var result = await new Wpf.Ui.Controls.MessageBox {
                    Title = I18NLoader.Instance["dialog.voicePkgExist.title"],
                    Content = string.Format(I18NLoader.Instance["dialog.voicePkgExist.content"], importPlan.DestinationPath),
                    PrimaryButtonText = I18NLoader.Instance["dialog.voicePkgExist.ok"],
                    CloseButtonText = I18NLoader.Instance["dialog.voicePkgExist.cancel"]
                }.ShowDialogAsync();

                if (result != Wpf.Ui.Controls.MessageBoxResult.Primary) {
                    return;
                }
            }

            var pkgLocal = await packageCatalog.ImportAsync(path);
            var pkg = new CodriverPackageUpdateFile(pkgLocal.Info);
            lock (_updateLock) {
                _updateQueue.Enqueue(pkg);
            }
            pkg.IsAvailable = true;
            VoicePackages.Add(pkg);

            await RefreshCodriversWhenUpdatesCompleteAsync();
        }
    }
}
