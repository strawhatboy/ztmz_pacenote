// This Source Code Form is subject to the terms of the MIT License.
// If a copy of the MIT was not distributed with this file, You can obtain one at https://opensource.org/licenses/MIT.
// Copyright (C) Leszek Pomianowski and WPF UI Contributors.
// All Rights Reserved.

using Wpf.Ui.Controls;
using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.WpfGUI.ViewModels;

public partial class SettingsVM : ObservableObject, INavigationAware
{
    private bool _isInitialized = false;

    [ObservableProperty]
    private string _appVersion = String.Empty;

    [ObservableProperty]
    private Wpf.Ui.Appearance.ApplicationTheme _currentTheme = Wpf.Ui.Appearance.ApplicationTheme.Unknown;

    public void OnNavigatedTo()
    {
        if (!_isInitialized)
            InitializeViewModel();
    }

    public void OnNavigatedFrom()
    {
    }

    private void InitializeViewModel()
    {
        CurrentTheme = Wpf.Ui.Appearance.ApplicationThemeManager.GetAppTheme();
        AppVersion = $"ZTMZ Next Generation Pacenote Tool - {GetAssemblyVersion()}";

        _isInitialized = true;
    }

    private string GetAssemblyVersion()
    {
        return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? String.Empty;
    }

    [RelayCommand]
    private void OnChangeTheme(string parameter)
    {
        switch (parameter)
        {
            case "theme_light":
                if (CurrentTheme == Wpf.Ui.Appearance.ApplicationTheme.Light)
                    break;

                Wpf.Ui.Appearance.ApplicationThemeManager.Apply(Wpf.Ui.Appearance.ApplicationTheme.Light);
                CurrentTheme = Wpf.Ui.Appearance.ApplicationTheme.Light;
                Config.Instance.IsDarkTheme = false;
                Config.Instance.UseSystemTheme = false;
                Config.Instance.SaveUserConfig();
                Wpf.Ui.Appearance.SystemThemeWatcher.UnWatch(Application.Current.MainWindow);

                break;

            case "theme_dark":
                if (CurrentTheme == Wpf.Ui.Appearance.ApplicationTheme.Dark)
                    break;

                Wpf.Ui.Appearance.ApplicationThemeManager.Apply(Wpf.Ui.Appearance.ApplicationTheme.Dark);
                CurrentTheme = Wpf.Ui.Appearance.ApplicationTheme.Dark;
                Config.Instance.IsDarkTheme = true;
                Config.Instance.UseSystemTheme = false;
                Config.Instance.SaveUserConfig();
                Wpf.Ui.Appearance.SystemThemeWatcher.UnWatch(Application.Current.MainWindow);

                break;
            case "theme_system":
                Wpf.Ui.Appearance.SystemThemeWatcher.UnWatch(Application.Current.MainWindow);
                Wpf.Ui.Appearance.SystemThemeWatcher.Watch(Application.Current.MainWindow);
                CurrentTheme = Wpf.Ui.Appearance.ApplicationTheme.Unknown;
                Config.Instance.UseSystemTheme = true;
                Config.Instance.SaveUserConfig();

                break;
        }
    }
}
