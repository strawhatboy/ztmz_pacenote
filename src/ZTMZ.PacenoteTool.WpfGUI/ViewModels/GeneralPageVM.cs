
using System.Collections.Generic;
using System.IO;
using System.Windows.Controls;
using System.Windows.Media;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.UI.Windows;
using ZTMZ.PacenoteTool.WpfGUI.Services;
using ZTMZ.PacenoteTool.WpfGUI.Views.Dialog;
namespace ZTMZ.PacenoteTool.WpfGUI.ViewModels;

public partial class GeneralPageVM : ObservableObject, INavigationAware {
    [ObservableProperty]
    private SolidColorBrush _accentColor = new SolidColorBrush(ThemeHelper.GetAccentColor());

    [ObservableProperty]
    private int _accentColorR = Config.Instance.AccentColorR;
    [ObservableProperty]
    private int _accentColorG = Config.Instance.AccentColorG;
    [ObservableProperty]
    private int _accentColorB = Config.Instance.AccentColorB;

    [ObservableProperty]
    private int _themeSelection = Config.Instance.UseSystemTheme ? 2 : (Config.Instance.IsDarkTheme ? 1 : 0);

    [ObservableProperty]
    private IList<object> _languages = new ObservableCollection<object>();

    [ObservableProperty]
    private int _languageSelection = 0;

    [ObservableProperty]
    private IList<FontChoice> _systemFonts = new ObservableCollection<FontChoice>();

    [ObservableProperty]
    private FontChoice? _selectedSystemFont;

    [ObservableProperty]
    private int _uiFontSizePercent = Math.Clamp(
        Config.Instance.UIFontSizePercent,
        AppFontService.MinimumFontSizePercent,
        AppFontService.MaximumFontSizePercent);

    [ObservableProperty]
    private IList<SuggestedFont> _suggestedFonts = new ObservableCollection<SuggestedFont>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SuggestedFontActionText))]
    private SuggestedFont? _selectedSuggestedFont;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SuggestedFontActionText))]
    private bool _isDownloadingSuggestedFont;

    [ObservableProperty]
    private double _suggestedFontDownloadProgress;

    [ObservableProperty]
    private string _suggestedFontStatus = string.Empty;

    [ObservableProperty]
    private Visibility _accentColorPickerVisibility = Config.Instance.UseSystemTheme ? Visibility.Collapsed : Visibility.Visible;

    [ObservableProperty]
    private bool _isAutoUpdate = Config.Instance.CheckUpdateWhenStartup;

    [ObservableProperty]
    private bool _isStartWithWindows = Config.Instance.StartWithWindows;

    [ObservableProperty]
    private bool _isStartMinimizedToTray = Config.Instance.StartMinimizedToTray;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendTestWindowsNotificationCommand))]
    private bool _isWindowsNotificationsEnabled = Config.Instance.EnableWindowsNotifications;

    [ObservableProperty]
    private bool _isOptInBetaPlan = Config.Instance.OptInBetaPlan;

    [ObservableProperty]
    private int _logLevel = Config.Instance.LogLevel;

    [ObservableProperty]
    private int _closeBehaviorSelection = Config.Instance.CloseWindowToMinimize ? 1 : 0;

    [ObservableProperty]
    private bool _showClosePrompt = Config.Instance.ShowClosePrompt;

    partial void OnAccentColorRChanged(int value)
    {
        Config.Instance.AccentColorR = value;
        updateAccentColor();
    }
    partial void OnAccentColorGChanged(int value)
    {
        Config.Instance.AccentColorG = value;
        updateAccentColor();
    }
    partial void OnAccentColorBChanged(int value)
    {
        Config.Instance.AccentColorB = value;
        updateAccentColor();
    }

    partial void OnShowClosePromptChanged(bool value)
    {
        Config.Instance.ShowClosePrompt = value;
        Config.Instance.SaveUserConfig();
    }

    private Wpf.Ui.Appearance.ApplicationTheme CurrentTheme = Config.Instance.UseSystemTheme ? Wpf.Ui.Appearance.ApplicationTheme.Unknown : 
        (Config.Instance.IsDarkTheme ? Wpf.Ui.Appearance.ApplicationTheme.Dark : Wpf.Ui.Appearance.ApplicationTheme.Light);

    private void updateAccentColor() {
        Config.Instance.SaveUserConfig();
        AccentColor.Color = ThemeHelper.GetAccentColor();
        if (!Config.Instance.UseSystemTheme) {
            Wpf.Ui.Appearance.ApplicationAccentColorManager.Apply(ThemeHelper.GetAccentColor(), 
                Wpf.Ui.Appearance.ApplicationThemeManager.GetAppTheme());
            Wpf.Ui.Appearance.ApplicationThemeManager.Apply(Config.Instance.IsDarkTheme ? Wpf.Ui.Appearance.ApplicationTheme.Dark : Wpf.Ui.Appearance.ApplicationTheme.Light,
                    WindowBackdropType.Mica,
                    false);
        }
    }

    private readonly StartupService _startupService;
    private readonly SystemNotificationService _systemNotificationService;
    private readonly AppFontService _appFontService;
    private readonly IContentDialogService _contentDialogService;
    private bool _isRefreshingFontChoices;

    public string SuggestedFontActionText
    {
        get
        {
            if (IsDownloadingSuggestedFont)
            {
                return I18NLoader.Instance["font.downloading"];
            }

            return SelectedSuggestedFont != null && _appFontService.IsInstalled(SelectedSuggestedFont)
                ? I18NLoader.Instance["font.apply"]
                : I18NLoader.Instance["font.downloadAndApply"];
        }
    }

    public GeneralPageVM(
        StartupService startupService,
        SystemNotificationService systemNotificationService,
        AppFontService appFontService,
        IContentDialogService contentDialogService) {
        _startupService = startupService;
        _systemNotificationService = systemNotificationService;
        _appFontService = appFontService;
        _contentDialogService = contentDialogService;
        foreach (var c in I18NLoader.Instance.culturesFullname)
        {
            Languages.Add(c);
        }
        var cindex = I18NLoader.Instance.cultures.FindIndex(a => a.Equals(Config.Instance.Language));
        if (cindex == -1)
        {
            LanguageSelection = 0;
        } else
        {
            LanguageSelection = cindex;
        }

        RefreshFontChoices();
    }

    [RelayCommand]
    private void ThemeSelectionChanged() {
        Config.Instance.IsDarkTheme = ThemeSelection == 1;
        var theme = selectionToTheme(ThemeSelection);
        if (CurrentTheme == theme)
            return;
        if (ThemeSelection <= 1) {

            Wpf.Ui.Appearance.ApplicationAccentColorManager.Apply(ThemeHelper.GetAccentColor(), 
                Wpf.Ui.Appearance.ApplicationThemeManager.GetAppTheme());
            Wpf.Ui.Appearance.ApplicationThemeManager.Apply(theme,
                WindowBackdropType.Mica,
                false);
            CurrentTheme = theme;
            Config.Instance.UseSystemTheme = false;
            Wpf.Ui.Appearance.SystemThemeWatcher.UnWatch(Application.Current.MainWindow);
            AccentColorPickerVisibility = Visibility.Visible;
        } else {
            Wpf.Ui.Appearance.SystemThemeWatcher.UnWatch(Application.Current.MainWindow);
            Wpf.Ui.Appearance.SystemThemeWatcher.Watch(Application.Current.MainWindow);
            Wpf.Ui.Appearance.ApplicationThemeManager.ApplySystemTheme();
            CurrentTheme = Wpf.Ui.Appearance.ApplicationTheme.Unknown;
            Config.Instance.UseSystemTheme = true;
            AccentColorPickerVisibility = Visibility.Collapsed;
        }
        Config.Instance.SaveUserConfig();
    }

    private Wpf.Ui.Appearance.ApplicationTheme selectionToTheme(int selection) {
        if (selection == 1) {
            return Wpf.Ui.Appearance.ApplicationTheme.Dark;
        } else if (selection == 0) {
            return Wpf.Ui.Appearance.ApplicationTheme.Light;
        } else {
            return Wpf.Ui.Appearance.ApplicationTheme.Unknown;
        }
    }

    partial void OnLanguageSelectionChanged(int value)
    {
        var c = I18NLoader.Instance.cultures[value];
        Config.Instance.Language = c;
        Config.Instance.SaveUserConfig();
        I18NLoader.Instance.SetCulture(c);
        I18NHelper.ApplyI18NToApplication();
        RefreshFontChoices();
    }

    partial void OnSelectedSystemFontChanged(FontChoice? value)
    {
        if (_isRefreshingFontChoices || value == null)
        {
            return;
        }

        if (value.IsSystemDefault)
        {
            Config.Instance.UIFontFamily = string.Empty;
            Config.Instance.SaveUserConfig();
            _appFontService.ApplySystemDefaultFont();
            return;
        }

        _ = ConfirmAndApplySystemFontAsync(value);
    }

    partial void OnUiFontSizePercentChanged(int value)
    {
        var boundedValue = Math.Clamp(
            value,
            AppFontService.MinimumFontSizePercent,
            AppFontService.MaximumFontSizePercent);
        if (boundedValue != value)
        {
            UiFontSizePercent = boundedValue;
            return;
        }

        Config.Instance.UIFontSizePercent = value;
        Config.Instance.SaveUserConfig();
        _appFontService.ApplyFontSize(value);
    }

    [RelayCommand]
    private void ResetUiFontSize()
    {
        UiFontSizePercent = AppFontService.DefaultFontSizePercent;
    }

    private async Task ConfirmAndApplySystemFontAsync(FontChoice fontChoice)
    {
        var dialog = new SystemFontWarningDialog(
            _contentDialogService.GetDialogHostEx()!,
            fontChoice.DisplayName);
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            RefreshFontChoices();
            return;
        }

        try
        {
            _appFontService.ApplySystemFont(fontChoice.Id);
            Config.Instance.UIFontFamily = fontChoice.Id;
            Config.Instance.SaveUserConfig();
        }
        catch (ArgumentException)
        {
            await _contentDialogService.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions
            {
                Title = I18NLoader.Instance["font.systemFontWarningTitle"],
                Content = I18NLoader.Instance["font.systemFontApplyFailed"],
                CloseButtonText = I18NLoader.Instance["dialog.common.btn_ok"]
            });
            RefreshFontChoices();
        }
    }

    partial void OnSelectedSuggestedFontChanged(SuggestedFont? value)
    {
        RefreshSuggestedFontStatus();
    }

    partial void OnSuggestedFontDownloadProgressChanged(double value)
    {
        RefreshSuggestedFontStatus();
    }

    [RelayCommand]
    private async Task InstallSuggestedFont()
    {
        if (SelectedSuggestedFont == null || IsDownloadingSuggestedFont)
        {
            return;
        }

        IsDownloadingSuggestedFont = true;
        SuggestedFontDownloadProgress = 0;
        RefreshSuggestedFontStatus();
        try
        {
            if (!_appFontService.IsInstalled(SelectedSuggestedFont))
            {
                var progress = new Progress<double>(value => SuggestedFontDownloadProgress = value);
                await _appFontService.DownloadSuggestedFontAsync(SelectedSuggestedFont, progress);
            }

            if (!_appFontService.TryApplySuggestedFont(SelectedSuggestedFont))
            {
                throw new InvalidOperationException("The downloaded font could not be loaded by WPF.");
            }

            Config.Instance.UIFontFamily = "suggested:" + SelectedSuggestedFont.Id;
            Config.Instance.SaveUserConfig();
            _isRefreshingFontChoices = true;
            SelectedSystemFont = null;
            _isRefreshingFontChoices = false;
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                string.Format(I18NLoader.Instance["font.downloadFailed"], ex.Message),
                I18NLoader.Instance["font.downloadFailedTitle"],
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }
        finally
        {
            IsDownloadingSuggestedFont = false;
            SuggestedFontDownloadProgress = 0;
            RefreshSuggestedFontStatus();
        }
    }

    private void RefreshFontChoices()
    {
        _isRefreshingFontChoices = true;
        try
        {
            SystemFonts = new ObservableCollection<FontChoice>(_appFontService.GetSystemFontChoices());
            SelectedSystemFont = _appFontService.GetConfiguredSystemFontChoice(SystemFonts);
            SuggestedFonts = new ObservableCollection<SuggestedFont>(_appFontService.SuggestedFonts);
            SelectedSuggestedFont = _appFontService.GetConfiguredSuggestedFont()
                ?? (SuggestedFonts.Count > 0 ? SuggestedFonts[0] : null);
            RefreshSuggestedFontStatus();
        }
        finally
        {
            _isRefreshingFontChoices = false;
        }
    }

    private void RefreshSuggestedFontStatus()
    {
        if (SelectedSuggestedFont == null)
        {
            SuggestedFontStatus = string.Empty;
            return;
        }

        if (IsDownloadingSuggestedFont)
        {
            SuggestedFontStatus = string.Format(
                I18NLoader.Instance["font.downloadingProgress"],
                SuggestedFontDownloadProgress);
            OnPropertyChanged(nameof(SuggestedFontActionText));
            return;
        }

        if (string.Equals(
                Config.Instance.UIFontFamily,
                "suggested:" + SelectedSuggestedFont.Id,
                StringComparison.OrdinalIgnoreCase))
        {
            SuggestedFontStatus = I18NLoader.Instance["font.active"];
            return;
        }

        SuggestedFontStatus = _appFontService.IsInstalled(SelectedSuggestedFont)
            ? I18NLoader.Instance["font.downloaded"]
            : string.Format(I18NLoader.Instance["font.downloadSize"], Math.Ceiling(SelectedSuggestedFont.Size / 1024d / 1024d));
        OnPropertyChanged(nameof(SuggestedFontActionText));
    }

    partial void OnIsAutoUpdateChanged(bool value)
    {
        Config.Instance.CheckUpdateWhenStartup = value;
        Config.Instance.SaveUserConfig();
    }

    partial void OnIsStartWithWindowsChanged(bool value)
    {
        Config.Instance.StartWithWindows = value;
        Config.Instance.SaveUserConfig();
        if (value) {
            // set to start with windows
            _startupService.AddApplicationToCurrentUserStartup();
        } else {
            // remove from startup
            _startupService.RemoveApplicationFromCurrentUserStartup();
        }
    }

    partial void OnIsStartMinimizedToTrayChanged(bool value)
    {
        Config.Instance.StartMinimizedToTray = value;
        Config.Instance.SaveUserConfig();
    }

    partial void OnIsWindowsNotificationsEnabledChanged(bool value)
    {
        Config.Instance.EnableWindowsNotifications = value;
        Config.Instance.SaveUserConfig();
    }

    partial void OnIsOptInBetaPlanChanged(bool value)
    {
        Config.Instance.OptInBetaPlan = value;
        Config.Instance.SaveUserConfig();
    }

    partial void OnLogLevelChanged(int value)
    {
        Config.Instance.LogLevel = value;
        Config.Instance.SaveUserConfig();
    }

    [RelayCommand]
    private void ViewLogs() {
        var logPath = AppLevelVariables.Instance.GetPath("logs");
        if (!Directory.Exists(logPath)) 
        {
            Directory.CreateDirectory(logPath);
        }
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe",
            AppLevelVariables.Instance.GetPath("logs")));
    }

    
    public async Task OnNavigatedToAsync()
    {
        // update AutoUpdate settings, since it may be changed in About/Settings page
        IsAutoUpdate = Config.Instance.CheckUpdateWhenStartup;

        // these 2 could be also changed during closing the main window
        CloseBehaviorSelection = Config.Instance.CloseWindowToMinimize ? 1 : 0;
        ShowClosePrompt = Config.Instance.ShowClosePrompt;
        IsStartMinimizedToTray = Config.Instance.StartMinimizedToTray;
        IsWindowsNotificationsEnabled = Config.Instance.EnableWindowsNotifications;
        await RefreshSuggestedFontCatalog();
    }

    public Task OnNavigatedFromAsync() => Task.CompletedTask;

    [RelayCommand]
    private async Task RefreshSuggestedFontCatalog()
    {
        try
        {
            var fonts = await _appFontService.RefreshSuggestedFontsAsync();
            _isRefreshingFontChoices = true;
            try
            {
                SuggestedFonts = new ObservableCollection<SuggestedFont>(fonts);
                SelectedSuggestedFont = _appFontService.GetConfiguredSuggestedFont()
                    ?? (SuggestedFonts.Count > 0 ? SuggestedFonts[0] : null);
            }
            finally
            {
                _isRefreshingFontChoices = false;
            }

            RefreshSuggestedFontStatus();
        }
        catch (Exception)
        {
            if (SelectedSuggestedFont == null)
            {
                SuggestedFontStatus = I18NLoader.Instance["font.catalogUnavailable"];
            }
        }
    }

    [RelayCommand]
    public void CloseBehaviorSelectionChanged()
    {
        Config.Instance.CloseWindowToMinimize = CloseBehaviorSelection == 1;
        Config.Instance.SaveUserConfig();
    }

    [RelayCommand(CanExecute = nameof(CanSendTestWindowsNotification))]
    private void SendTestWindowsNotification()
    {
        _systemNotificationService.ShowTestNotification();
    }

    private bool CanSendTestWindowsNotification()
    {
        return IsWindowsNotificationsEnabled;
    }
}
