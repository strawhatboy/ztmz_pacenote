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
    private bool _isInitialized = false;

    [ObservableProperty]
    private string _appVersion = String.Empty;

    [ObservableProperty]
    private bool _isAutoUpdate = Config.Instance.CheckUpdateWhenStartup;

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
        
    }

    public void OnNavigatedFrom()
    {
    }

    private void InitializeViewModel()
    {
        AppVersion = GetAssemblyVersion();

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
    private async void OpenStaffDialog() {
        await _contentDialogService.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions() {
            Title = I18NLoader.Instance["settings.staff"],
            Content = _staff,
            CloseButtonText = I18NLoader.Instance["dialog.common.btn_ok"]
        });
    }

    [RelayCommand]
    private async void OpenUpdateHistoryDialog() {
        var updateHistoryText = "";
        if (ToolUtils.GetToolVersion() == ToolVersion.TEST) {  
            updateHistoryText = await File.ReadAllTextAsync(getFilePathAccordingToCurrentExecutionPath("更新记录beta.txt"));
        } else {
            updateHistoryText = await File.ReadAllTextAsync(getFilePathAccordingToCurrentExecutionPath("更新记录.txt"));
        }
        await _contentDialogService.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions() {
            Title = I18NLoader.Instance["ui.tb_updates"],
            Content = updateHistoryText,
            CloseButtonText = I18NLoader.Instance["dialog.common.btn_ok"]
        });
    }

    [RelayCommand]
    private async void OpenEULADialog() {
        var eula = "";
        if (Config.Instance.Language == "zh-cn") {
            eula = await File.ReadAllTextAsync(getFilePathAccordingToCurrentExecutionPath("eula-cn.txt"));
        } else {
            eula = await File.ReadAllTextAsync(getFilePathAccordingToCurrentExecutionPath("eula.txt"));
        }
        await _contentDialogService.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions() {
            Title = I18NLoader.Instance["ui.eula"],
            Content = eula,
            CloseButtonText = I18NLoader.Instance["dialog.common.btn_ok"]
        });
    }

    [RelayCommand]
    private async void OpenLicenseDialog() {
        var privacyPolicy = "";
        if (Config.Instance.Language == "zh-cn") {
            privacyPolicy = await File.ReadAllTextAsync(getFilePathAccordingToCurrentExecutionPath("license-cn.txt"));
        } else {
            privacyPolicy = await File.ReadAllTextAsync(getFilePathAccordingToCurrentExecutionPath("license.txt"));
        }
        await _contentDialogService.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions() {
            Title = I18NLoader.Instance["ui.license"],
            Content = privacyPolicy,
            CloseButtonText = I18NLoader.Instance["dialog.common.btn_ok"]
        });
    }

    [RelayCommand]
    private async void OpenOpenSourceSoftwareDialog() {
        await _contentDialogService.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions() {
            Title = I18NLoader.Instance["ui.opensoftware"],
            Content = _opensoftware,
            CloseButtonText = I18NLoader.Instance["dialog.common.btn_ok"]
        });
    }

    [RelayCommand]
    private async void OpenContactUsDialog() {
        await _contentDialogService.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions() {
            Title = I18NLoader.Instance["ui.contactUs"],
            Content = _contactUs,
            CloseButtonText = I18NLoader.Instance["dialog.common.btn_ok"]
        });
    }

    private string getFilePathAccordingToCurrentExecutionPath(string fileName)
    {
        return Path.Join(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), fileName);
    }
}
