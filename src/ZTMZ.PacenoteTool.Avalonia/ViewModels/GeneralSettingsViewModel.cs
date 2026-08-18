#nullable enable

using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZTMZ.PacenoteTool.Avalonia.Services;
using ZTMZ.PacenoteTool.Base;
#if WINDOWS
using ZTMZ.PacenoteTool.Platform.Windows;
#endif

namespace ZTMZ.PacenoteTool.Avalonia.ViewModels;

internal sealed record LanguageChoice(string Culture, string DisplayName);

internal sealed partial class GeneralSettingsViewModel : ObservableObject, IDisposable
{
    private readonly AvaloniaAppearanceService _appearanceService = new();
    private bool _initializing = true;
    private bool _refreshingFonts;

    public GeneralSettingsViewModel()
    {
        foreach (var choice in _appearanceService.GetSystemFontChoices())
        {
            SystemFonts.Add(choice);
        }
        SelectedSystemFont = _appearanceService.GetConfiguredSystemFontChoice(SystemFonts);

        foreach (var font in _appearanceService.SuggestedFonts)
        {
            SuggestedFonts.Add(font);
        }
        SelectedSuggestedFont = _appearanceService.GetConfiguredSuggestedFont()
                                ?? SuggestedFonts.FirstOrDefault();

        for (var index = 0; index < I18NLoader.Instance.cultures.Count; index++)
        {
            Languages.Add(new LanguageChoice(
                I18NLoader.Instance.cultures[index],
                I18NLoader.Instance.culturesFullname[index]));
        }
        SelectedLanguage = Languages.FirstOrDefault(language => string.Equals(
                               language.Culture,
                               I18NLoader.Instance.CurrentCultureName,
                               StringComparison.OrdinalIgnoreCase))
                           ?? Languages.FirstOrDefault();

        RefreshSuggestedFontStatus();
        _initializing = false;
        AvaloniaLocalization.CultureChanged += OnCultureChanged;
    }

    public event Action<string, string>? NotificationRequested;

    public ObservableCollection<AvaloniaFontChoice> SystemFonts { get; } = [];
    public ObservableCollection<AvaloniaSuggestedFont> SuggestedFonts { get; } = [];
    public ObservableCollection<LanguageChoice> Languages { get; } = [];

    public bool IsWindows => OperatingSystem.IsWindows();
    public bool IsAccentColorVisible => ThemeSelection != 2;
    public bool HasSettingsMessage => !string.IsNullOrWhiteSpace(SettingsMessage);
    public string UiFontSizeText => $"{UiFontSizePercent}%";

    public string SuggestedFontActionText => IsDownloadingSuggestedFont
        ? AvaloniaLocalization.Get("font.downloading")
        : SelectedSuggestedFont != null && _appearanceService.IsInstalled(SelectedSuggestedFont)
            ? AvaloniaLocalization.Get("font.apply")
            : AvaloniaLocalization.Get("font.downloadAndApply");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAccentColorVisible))]
    private int _themeSelection = Config.Instance.UseSystemTheme
        ? 2
        : Config.Instance.IsDarkTheme ? 1 : 0;

    [ObservableProperty]
    private AvaloniaFontChoice? _selectedSystemFont;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UiFontSizeText))]
    private int _uiFontSizePercent = Math.Clamp(
        Config.Instance.UIFontSizePercent,
        AvaloniaAppearanceService.MinimumFontSizePercent,
        AvaloniaAppearanceService.MaximumFontSizePercent);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SuggestedFontActionText))]
    [NotifyCanExecuteChangedFor(nameof(InstallSuggestedFontCommand))]
    private AvaloniaSuggestedFont? _selectedSuggestedFont;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SuggestedFontActionText))]
    [NotifyCanExecuteChangedFor(nameof(InstallSuggestedFontCommand))]
    private bool _isDownloadingSuggestedFont;

    [ObservableProperty]
    private double _suggestedFontDownloadProgress;

    [ObservableProperty]
    private string _suggestedFontStatus = string.Empty;

    [ObservableProperty]
    private Color _accentColor = Color.FromRgb(
        (byte)Math.Clamp(Config.Instance.AccentColorR, 0, 255),
        (byte)Math.Clamp(Config.Instance.AccentColorG, 0, 255),
        (byte)Math.Clamp(Config.Instance.AccentColorB, 0, 255));

    [ObservableProperty]
    private LanguageChoice? _selectedLanguage;

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
    private bool _showClosePrompt = Config.Instance.ShowClosePrompt;

    [ObservableProperty]
    private int _closeBehaviorSelection = Config.Instance.CloseWindowToMinimize ? 1 : 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSettingsMessage))]
    private string _settingsMessage = string.Empty;

    partial void OnThemeSelectionChanged(int value)
    {
        if (_initializing || value is < 0 or > 2 || Application.Current == null)
        {
            return;
        }

        Config.Instance.UseSystemTheme = value == 2;
        Config.Instance.IsDarkTheme = value == 1;
        Config.Instance.SaveUserConfig();
        AvaloniaAppearanceService.ApplyTheme(Application.Current, value);
        AvaloniaAppearanceService.ApplyAccentColor(Application.Current);
    }

    partial void OnSelectedSystemFontChanged(AvaloniaFontChoice? value)
    {
        if (_initializing || _refreshingFonts || value == null)
        {
            return;
        }

        try
        {
            _appearanceService.ApplySystemFont(value);
            Config.Instance.UIFontFamily = value.IsSystemDefault ? string.Empty : value.Id;
            Config.Instance.SaveUserConfig();
            _refreshingFonts = true;
            SelectedSuggestedFont = null;
            _refreshingFonts = false;
            SettingsMessage = string.Empty;
        }
        catch (Exception exception)
        {
            SettingsMessage = string.Format(
                AvaloniaLocalization.Get("font.systemFontApplyFailed"),
                exception.Message);
        }
    }

    partial void OnUiFontSizePercentChanged(int value)
    {
        if (_initializing || Application.Current == null)
        {
            return;
        }

        var bounded = Math.Clamp(
            value,
            AvaloniaAppearanceService.MinimumFontSizePercent,
            AvaloniaAppearanceService.MaximumFontSizePercent);
        if (bounded != value)
        {
            UiFontSizePercent = bounded;
            return;
        }

        Config.Instance.UIFontSizePercent = value;
        Config.Instance.SaveUserConfig();
        AvaloniaAppearanceService.ApplyFontSize(Application.Current, value);
    }

    partial void OnSelectedSuggestedFontChanged(AvaloniaSuggestedFont? value)
    {
        if (!_refreshingFonts)
        {
            RefreshSuggestedFontStatus();
        }
    }

    partial void OnSuggestedFontDownloadProgressChanged(double value) =>
        RefreshSuggestedFontStatus();

    partial void OnAccentColorChanged(Color value) => UpdateAccentColor(value);

    partial void OnSelectedLanguageChanged(LanguageChoice? value)
    {
        if (_initializing || value == null)
        {
            return;
        }

        AvaloniaLocalization.SetCulture(value.Culture);
        Config.Instance.SaveUserConfig();
    }

    partial void OnIsAutoUpdateChanged(bool value) => Save(value, setting =>
        Config.Instance.CheckUpdateWhenStartup = setting);

    partial void OnIsStartWithWindowsChanged(bool value)
    {
        if (_initializing || !OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
#if WINDOWS
            WindowsStartupRegistrationService.SetEnabled(
                value,
                Environment.ProcessPath ?? throw new InvalidOperationException("Application path is unavailable."));
#endif
            Config.Instance.StartWithWindows = value;
            Config.Instance.SaveUserConfig();
            SettingsMessage = string.Empty;
        }
        catch (Exception exception)
        {
            SettingsMessage = exception.Message;
            _initializing = true;
            IsStartWithWindows = !value;
            _initializing = false;
        }
    }

    partial void OnIsStartMinimizedToTrayChanged(bool value) => Save(value, setting =>
        Config.Instance.StartMinimizedToTray = setting);

    partial void OnIsWindowsNotificationsEnabledChanged(bool value) => Save(value, setting =>
        Config.Instance.EnableWindowsNotifications = setting);

    partial void OnIsOptInBetaPlanChanged(bool value) => Save(value, setting =>
        Config.Instance.OptInBetaPlan = setting);

    partial void OnLogLevelChanged(int value)
    {
        if (_initializing || value is < 0 or > 6)
        {
            return;
        }

        Config.Instance.LogLevel = value;
        Config.Instance.SaveUserConfig();
    }

    partial void OnShowClosePromptChanged(bool value) => Save(value, setting =>
        Config.Instance.ShowClosePrompt = setting);

    partial void OnCloseBehaviorSelectionChanged(int value)
    {
        if (_initializing || value is < 0 or > 1)
        {
            return;
        }

        Config.Instance.CloseWindowToMinimize = value == 1;
        Config.Instance.SaveUserConfig();
    }

    [RelayCommand]
    private void ResetUiFontSize() =>
        UiFontSizePercent = AvaloniaAppearanceService.DefaultFontSizePercent;

    [RelayCommand(CanExecute = nameof(CanInstallSuggestedFont))]
    private async Task InstallSuggestedFontAsync()
    {
        if (SelectedSuggestedFont == null)
        {
            return;
        }

        IsDownloadingSuggestedFont = true;
        SuggestedFontDownloadProgress = 0;
        try
        {
            if (!_appearanceService.IsInstalled(SelectedSuggestedFont))
            {
                var progress = new Progress<double>(value => SuggestedFontDownloadProgress = value);
                await _appearanceService.DownloadSuggestedFontAsync(SelectedSuggestedFont, progress);
            }

            if (!_appearanceService.TryApplySuggestedFont(SelectedSuggestedFont))
            {
                throw new InvalidOperationException("The downloaded font could not be loaded by Avalonia.");
            }

            Config.Instance.UIFontFamily =
                AvaloniaAppearanceService.SuggestedFontPrefix + SelectedSuggestedFont.Id;
            Config.Instance.SaveUserConfig();
            _refreshingFonts = true;
            SelectedSystemFont = null;
            _refreshingFonts = false;
            SettingsMessage = string.Empty;
        }
        catch (Exception exception)
        {
            SettingsMessage = string.Format(
                AvaloniaLocalization.Get("font.downloadFailed"),
                exception.Message);
        }
        finally
        {
            IsDownloadingSuggestedFont = false;
            SuggestedFontDownloadProgress = 0;
            RefreshSuggestedFontStatus();
        }
    }

    private bool CanInstallSuggestedFont() =>
        SelectedSuggestedFont != null && !IsDownloadingSuggestedFont;

    [RelayCommand]
    internal async Task RefreshSuggestedFontCatalogAsync()
    {
        try
        {
            var fonts = await _appearanceService.RefreshSuggestedFontsAsync();
            _refreshingFonts = true;
            SuggestedFonts.Clear();
            foreach (var font in fonts)
            {
                SuggestedFonts.Add(font);
            }
            SelectedSuggestedFont = _appearanceService.GetConfiguredSuggestedFont()
                                    ?? SuggestedFonts.FirstOrDefault();
            _refreshingFonts = false;
            SettingsMessage = string.Empty;
            RefreshSuggestedFontStatus();
        }
        catch (Exception)
        {
            _refreshingFonts = false;
            if (SelectedSuggestedFont == null)
            {
                SuggestedFontStatus = AvaloniaLocalization.Get("font.catalogUnavailable");
            }
        }
    }

    [RelayCommand]
    private void ViewLogs()
    {
        var path = AppLevelVariables.Instance.GetPath("logs");
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    [RelayCommand(CanExecute = nameof(CanSendTestWindowsNotification))]
    private void SendTestWindowsNotification() => NotificationRequested?.Invoke(
        AvaloniaLocalization.Get("notification.title"),
        AvaloniaLocalization.Get("notification.test"));

    private bool CanSendTestWindowsNotification() =>
        IsWindows && IsWindowsNotificationsEnabled;

    private void UpdateAccentColor(Color value)
    {
        if (_initializing)
        {
            return;
        }

        Config.Instance.AccentColorR = value.R;
        Config.Instance.AccentColorG = value.G;
        Config.Instance.AccentColorB = value.B;
        Config.Instance.SaveUserConfig();
        if (Application.Current != null)
        {
            AvaloniaAppearanceService.ApplyAccentColor(Application.Current);
        }
    }

    private void RefreshSuggestedFontStatus()
    {
        OnPropertyChanged(nameof(SuggestedFontActionText));
        if (SelectedSuggestedFont == null)
        {
            SuggestedFontStatus = string.Empty;
            return;
        }

        if (IsDownloadingSuggestedFont)
        {
            SuggestedFontStatus = string.Format(
                AvaloniaLocalization.Get("font.downloadingProgress"),
                SuggestedFontDownloadProgress);
            return;
        }

        if (string.Equals(
                Config.Instance.UIFontFamily,
                AvaloniaAppearanceService.SuggestedFontPrefix + SelectedSuggestedFont.Id,
                StringComparison.OrdinalIgnoreCase))
        {
            SuggestedFontStatus = AvaloniaLocalization.Get("font.active");
            return;
        }

        SuggestedFontStatus = _appearanceService.IsInstalled(SelectedSuggestedFont)
            ? AvaloniaLocalization.Get("font.downloaded")
            : string.Format(
                AvaloniaLocalization.Get("font.downloadSize"),
                Math.Ceiling(SelectedSuggestedFont.Size / 1024d / 1024d));
    }

    private void Save(bool value, Action<bool> setter)
    {
        if (_initializing)
        {
            return;
        }

        setter(value);
        Config.Instance.SaveUserConfig();
    }

    private void OnCultureChanged(object? sender, EventArgs eventArgs)
    {
        RefreshSuggestedFontStatus();
        OnPropertyChanged(nameof(SuggestedFontActionText));
    }

    public void Dispose() => AvaloniaLocalization.CultureChanged -= OnCultureChanged;
}
