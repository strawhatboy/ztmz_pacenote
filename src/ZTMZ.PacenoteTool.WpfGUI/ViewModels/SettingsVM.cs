// This Source Code Form is subject to the terms of the MIT License.
// If a copy of the MIT was not distributed with this file, You can obtain one at https://opensource.org/licenses/MIT.
// Copyright (C) Leszek Pomianowski and WPF UI Contributors.
// All Rights Reserved.

using System.IO;
using System.Reflection;
using System.Windows.Controls;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.UI;
using ZTMZ.PacenoteTool.Core;

namespace ZTMZ.PacenoteTool.WpfGUI.ViewModels;

public partial class SettingsVM : ObservableObject, INavigationAware
{
    private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
    private bool _isInitialized = false;

    [ObservableProperty]
    private string _appVersion = String.Empty;

    [ObservableProperty]
    private bool _isAutoUpdate = Config.Instance.CheckUpdateWhenStartup;

    [ObservableProperty]
    private string _dataFolderSize = String.Empty;

    private readonly IContentDialogService _contentDialogService;

    public SettingsVM(IContentDialogService contentDialogService)
    {
        _contentDialogService = contentDialogService;
    }

    partial void OnIsAutoUpdateChanged(bool value)
    {
        Config.Instance.CheckUpdateWhenStartup = value;
        Config.Instance.SaveUserConfig();
    }


    public void OnNavigatedTo()
    {
        // update AutoUpdate settings, since it may be changed in General page
        IsAutoUpdate = Config.Instance.CheckUpdateWhenStartup;

        if (!_isInitialized)
            InitializeViewModel();

        // calculate the data folder size.
        var dataFolder = AppLevelVariables.Instance.AppConfigFolder;
        CalculateDataFolderSize(dataFolder);
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

    public void OnNavigatedFrom()
    {
    }

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

    [RelayCommand]
    private async void MoveDataFolder() {
        // open folder dialog to get the new folder, and start another process to move the folder and modify the registry, while shutting down the current app to release file lock
    }
}
