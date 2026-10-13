// This Source Code Form is subject to the terms of the MIT License.
// If a copy of the MIT was not distributed with this file, You can obtain one at https://opensource.org/licenses/MIT.
// Copyright (C) Leszek Pomianowski and WPF UI Contributors.
// All Rights Reserved.

using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.UI.Windows;
using ZTMZ.PacenoteTool.Core;
using ZTMZ.PacenoteTool.WpfGUI.Services;
using ZTMZ.PacenoteTool.WpfGUI.Views.Dialog;

namespace ZTMZ.PacenoteTool.WpfGUI.ViewModels;

public partial class SettingsVM : ObservableObject, INavigationAware
{
    private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
    private bool _isInitialized = false;

    [ObservableProperty]
    private string _appVersion = String.Empty;

    [ObservableProperty]
    private string _versionCopyStatus = String.Empty;

    [RelayCommand]
    private void CopyVersion()
    {
        try
        {
            System.Windows.Clipboard.SetDataObject(AppVersion, true);
            VersionCopyStatus = I18NLoader.Instance["aboutVersion.copied"];
        }
        catch (Exception exception)
        {
            _logger.Warn(exception, "Unable to copy application version");
            VersionCopyStatus = I18NLoader.Instance["aboutVersion.copyFailed"];
        }
    }

    [ObservableProperty]
    private bool _isAutoUpdate = Config.Instance.CheckUpdateWhenStartup;

    [ObservableProperty]
    private string _dataFolderSize = String.Empty;

    [ObservableProperty]
    private bool _isMovingDataFolder;

    private readonly IContentDialogService _contentDialogService;
    private readonly DataFolderMigrationService _dataFolderMigrationService = new();
    private readonly ReplayCatalogService _replayCatalog;

    public SettingsVM(
        IContentDialogService contentDialogService,
        ReplayCatalogService replayCatalog)
    {
        _contentDialogService = contentDialogService;
        _replayCatalog = replayCatalog;
    }

    partial void OnIsAutoUpdateChanged(bool value)
    {
        Config.Instance.CheckUpdateWhenStartup = value;
        Config.Instance.SaveUserConfig();
    }


    public Task OnNavigatedToAsync()
    {
        // update AutoUpdate settings, since it may be changed in General page
        IsAutoUpdate = Config.Instance.CheckUpdateWhenStartup;

        if (!_isInitialized)
            InitializeViewModel();

        // calculate the data folder size.
        var dataFolder = AppLevelVariables.Instance.AppConfigFolder;
        CalculateDataFolderSize(dataFolder);
        return Task.CompletedTask;
    }

    private void CalculateDataFolderSize(string folderPath)
    {
        try
        {
            long size = 0;
            var dirInfo = new DirectoryInfo(folderPath);

            // Calculate size including all subdirectories
            foreach (var file in dirInfo.GetFiles("*", SearchOption.AllDirectories))
            {
                size += file.Length;
            }

            // Convert to appropriate size format
            DataFolderSize = FormatSize(size);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to calculate data folder size");
            DataFolderSize = "N/A";
        }
    }

    private string FormatSize(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int counter = 0;
        decimal number = bytes;

        while (Math.Round(number / 1024) >= 1)
        {
            number = number / 1024;
            counter++;
        }

        return string.Format("{0:n1} {1}", number, suffixes[counter]);
    }

    public Task OnNavigatedFromAsync() => Task.CompletedTask;

    private void InitializeViewModel()
    {
        AppVersion = $"{GetAssemblyVersion()} [{ToolUtils.GetToolVersion()}]";

        _isInitialized = true;
    }

    private string GetAssemblyVersion()
    {
        return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? String.Empty;
    }

    private UserControl _staff = new Views.About.Staff();

    private UserControl _sponsors = new Views.About.Sponsors();

    private UserControl _opensoftware = new Views.About.OpenSoftware();

    private UserControl _contactUs = new Views.About.ContactUs();


    [RelayCommand]
    private async void OpenStaffDialog()
    {
        await _contentDialogService.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions()
        {
            Title = I18NLoader.Instance["settings.staff"],
            Content = _staff,
            CloseButtonText = I18NLoader.Instance["dialog.common.btn_ok"]
        });
    }

    [RelayCommand]
    private async void OpenSponsorsDialog()
    {
        await _contentDialogService.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions()
        {
            Title = I18NLoader.Instance["ui.sponsors"],
            Content = _sponsors,
            CloseButtonText = I18NLoader.Instance["dialog.common.btn_ok"]
        });
    }

    [RelayCommand]
    private async void OpenUpdateHistoryDialog()
    {
        var updateHistoryText = "";
        if (ToolUtils.GetToolVersion() == ToolVersion.TEST)
        {
            updateHistoryText = await File.ReadAllTextAsync(getFilePathAccordingToCurrentExecutionPath("更新记录beta.txt"));
        }
        else
        {
            updateHistoryText = await File.ReadAllTextAsync(getFilePathAccordingToCurrentExecutionPath("更新记录.txt"));
        }
        await _contentDialogService.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions()
        {
            Title = I18NLoader.Instance["ui.tb_updates"],
            Content = updateHistoryText,
            CloseButtonText = I18NLoader.Instance["dialog.common.btn_ok"]
        });
    }

    [RelayCommand]
    private async void OpenEULADialog()
    {
        var eula = "";
        if (Config.Instance.Language == "zh-cn")
        {
            eula = await File.ReadAllTextAsync(getFilePathAccordingToCurrentExecutionPath("eula-cn.txt"));
        }
        else
        {
            eula = await File.ReadAllTextAsync(getFilePathAccordingToCurrentExecutionPath("eula.txt"));
        }
        await _contentDialogService.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions()
        {
            Title = I18NLoader.Instance["ui.eula"],
            Content = eula,
            CloseButtonText = I18NLoader.Instance["dialog.common.btn_ok"]
        });
    }

    [RelayCommand]
    private async void OpenLicenseDialog()
    {
        var privacyPolicy = "";
        if (Config.Instance.Language == "zh-cn")
        {
            privacyPolicy = await File.ReadAllTextAsync(getFilePathAccordingToCurrentExecutionPath("license-cn.txt"));
        }
        else
        {
            privacyPolicy = await File.ReadAllTextAsync(getFilePathAccordingToCurrentExecutionPath("license.txt"));
        }
        await _contentDialogService.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions()
        {
            Title = I18NLoader.Instance["ui.license"],
            Content = privacyPolicy,
            CloseButtonText = I18NLoader.Instance["dialog.common.btn_ok"]
        });
    }

    [RelayCommand]
    private async void OpenOpenSourceSoftwareDialog()
    {
        await _contentDialogService.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions()
        {
            Title = I18NLoader.Instance["ui.opensoftware"],
            Content = _opensoftware,
            CloseButtonText = I18NLoader.Instance["dialog.common.btn_ok"]
        });
    }

    [RelayCommand]
    private async void OpenContactUsDialog()
    {
        await _contentDialogService.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions()
        {
            Title = I18NLoader.Instance["ui.contactUs"],
            Content = _contactUs,
            CloseButtonText = I18NLoader.Instance["dialog.common.btn_ok"]
        });
    }

    private string getFilePathAccordingToCurrentExecutionPath(string fileName)
    {
        return Path.Join(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), fileName);
    }

    [RelayCommand]
    private async void OpenDataFolder() {
        var dataFolder = AppLevelVariables.Instance.AppConfigFolder;
        if (Directory.Exists(dataFolder)) {
            System.Diagnostics.Process.Start("explorer.exe", dataFolder);
        }
    }

    partial void OnIsMovingDataFolderChanged(bool value)
    {
        MoveDataFolderCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanMoveDataFolder))]
    private async Task MoveDataFolder()
    {
        if (AppLevelVariables.Instance.IsPortableVersion())
        {
            await ShowMigrationMessageAsync(
                I18NLoader.Instance["dataFolderMigration.title"],
                I18NLoader.Instance["dataFolderMigration.portableNotSupported"]);
            return;
        }

        var sourcePath = AppLevelVariables.Instance.AppConfigFolder;
        var destinationPath = SelectDestinationFolder(sourcePath);
        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            return;
        }

        IsMovingDataFolder = true;
        try
        {
            var plan = await Task.Run(() => _dataFolderMigrationService.CreatePlan(sourcePath, destinationPath));
            var confirmation = new DataFolderMoveConfirmationDialog(
                _contentDialogService.GetDialogHostEx()!,
                plan,
                FormatSize(plan.TotalBytes),
                FormatSize(plan.RequiredFreeSpaceBytes),
                FormatSize(plan.AvailableFreeSpaceBytes));
            if (await confirmation.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            Config.Instance.SaveUserConfig();
            await _replayCatalog.FlushPendingSavesAsync();
            DataFolderMigrationLauncher.Start(new DataFolderMigrationRequest(
                sourcePath,
                destinationPath,
                Environment.ProcessId,
                Config.Instance.Language));
            App.RequestDataFolderMigrationShutdown();
            Application.Current.Shutdown();
        }
        catch (InsufficientDataFolderMigrationSpaceException exception)
        {
            await ShowMigrationMessageAsync(
                I18NLoader.Instance["dataFolderMigration.title"],
                string.Format(
                    I18NLoader.Instance["dataFolderMigration.insufficientSpace"],
                    FormatSize(exception.RequiredBytes),
                    FormatSize(exception.AvailableBytes)));
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to start data-folder migration.");
            await ShowMigrationMessageAsync(
                I18NLoader.Instance["dataFolderMigration.title"],
                string.Format(I18NLoader.Instance["dataFolderMigration.failed"], exception.Message));
        }
        finally
        {
            IsMovingDataFolder = false;
        }
    }

    private bool CanMoveDataFolder()
    {
        return !IsMovingDataFolder;
    }

    private string? SelectDestinationFolder(string sourcePath)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = I18NLoader.Instance["dataFolderMigration.selectDestination"],
            InitialDirectory = Path.GetDirectoryName(sourcePath) ?? sourcePath,
            ShowNewFolderButton = true,
            UseDescriptionForTitle = true
        };

        return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK
            ? dialog.SelectedPath
            : null;
    }

    private async Task ShowMigrationMessageAsync(string title, string content)
    {
        await _contentDialogService.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions
        {
            Title = title,
            Content = content,
            CloseButtonText = I18NLoader.Instance["dialog.common.btn_ok"]
        });
    }
}
