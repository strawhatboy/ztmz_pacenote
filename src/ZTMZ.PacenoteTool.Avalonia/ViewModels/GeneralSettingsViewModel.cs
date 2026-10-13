#nullable enable

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZTMZ.PacenoteTool.Avalonia.Services;
using ZTMZ.PacenoteTool.Base;
#if WINDOWS
using ZTMZ.PacenoteTool.Platform.Windows;
#else
using ZTMZ.PacenoteTool.Platform.Linux;
#endif

namespace ZTMZ.PacenoteTool.Avalonia.ViewModels;

internal sealed record LanguageChoice(string Culture, string DisplayName);

internal sealed partial class GeneralSettingsViewModel : ObservableObject, IDisposable
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    private readonly AvaloniaAppearanceService _appearanceService = new();
    private bool _initializing = true;
    private bool _refreshingFonts;
    [ObservableProperty]
    private bool _checkVoicePackageUpdatesOnStartup = Config.Instance.CheckVoicePackageUpdatesOnStartup;
    partial void OnCheckVoicePackageUpdatesOnStartupChanged(bool value) => Save(value,
        setting => Config.Instance.CheckVoicePackageUpdatesOnStartup = setting);

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

    // The persisted property keeps its historical Windows name for configuration compatibility.
    public event Action<string, string>? NotificationRequested;
    public event Action<string>? LogDirectoryViewRequested;

    /// <summary>Re-reads settings that may have been changed elsewhere.</summary>
    public void RefreshFromConfig()
    {
        _initializing = true;
        try
        {
            ThemeSelection = Config.Instance.UseSystemTheme
                ? 2
                : Config.Instance.IsDarkTheme ? 1 : 0;
            SelectedSystemFont = _appearanceService.GetConfiguredSystemFontChoice(SystemFonts);
            UiFontSizePercent = Math.Clamp(
                Config.Instance.UIFontSizePercent,
                AvaloniaAppearanceService.MinimumFontSizePercent,
                AvaloniaAppearanceService.MaximumFontSizePercent);
            AccentColor = Color.FromRgb(
                (byte)Math.Clamp(Config.Instance.AccentColorR, 0, 255),
                (byte)Math.Clamp(Config.Instance.AccentColorG, 0, 255),
                (byte)Math.Clamp(Config.Instance.AccentColorB, 0, 255));
            SelectedLanguage = Languages.FirstOrDefault(language => string.Equals(
                                   language.Culture,
                                   I18NLoader.Instance.CurrentCultureName,
                                   StringComparison.OrdinalIgnoreCase))
                               ?? Languages.FirstOrDefault();
            IsAutoUpdate = Config.Instance.CheckUpdateWhenStartup;
            CheckVoicePackageUpdatesOnStartup = Config.Instance.CheckVoicePackageUpdatesOnStartup;
            IsStartWithSystem = Config.Instance.StartWithWindows;
            IsStartMinimizedToTray = Config.Instance.StartMinimizedToTray;
            IsWindowsNotificationsEnabled = Config.Instance.EnableWindowsNotifications;
            IsOptInBetaPlan = Config.Instance.OptInBetaPlan;
            LogLevel = Config.Instance.LogLevel;
            ShowClosePrompt = Config.Instance.ShowClosePrompt;
            CloseBehaviorSelection = Config.Instance.CloseWindowToMinimize ? 1 : 0;
        }
        finally
        {
            _initializing = false;
        }
    }

    public ObservableCollection<AvaloniaFontChoice> SystemFonts { get; } = [];
    public ObservableCollection<SuggestedFont> SuggestedFonts { get; } = [];
    public ObservableCollection<LanguageChoice> Languages { get; } = [];

    public bool IsWindows => OperatingSystem.IsWindows();
    public bool IsDesktopNotificationSupported => OperatingSystem.IsWindows() || OperatingSystem.IsLinux();
    public bool IsAccentColorVisible => ThemeSelection != 2;
    public bool HasSettingsMessage => !string.IsNullOrWhiteSpace(SettingsMessage);
    public bool IsLanguageSelectionEnabled => !IsLanguageSwitching;
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
    private SuggestedFont? _selectedSuggestedFont;

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
    [NotifyPropertyChangedFor(nameof(IsLanguageSelectionEnabled))]
    private bool _isLanguageSwitching;

    [ObservableProperty]
    private LanguageChoice? _selectedLanguage;

    [ObservableProperty]
    private bool _isAutoUpdate = Config.Instance.CheckUpdateWhenStartup;

    [ObservableProperty]
    // Config keeps this historical name so existing user settings continue to work.
    private bool _isStartWithSystem = Config.Instance.StartWithWindows;

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
            Logger.Error(exception, "Failed to apply system font {FontId}.", value.Id);
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

    partial void OnSelectedSuggestedFontChanged(SuggestedFont? value)
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
        if (_initializing || value == null || IsLanguageSwitching)
        {
            return;
        }

        _ = ApplyLanguageAsync(value);
    }

    private async Task ApplyLanguageAsync(LanguageChoice choice)
    {
        IsLanguageSwitching = true;
        try
        {
            await AvaloniaLocalization.SetCultureAsync(choice.Culture);
            Config.Instance.SaveUserConfig();
            SettingsMessage = string.Empty;
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Failed to switch application language to {Culture}.", choice.Culture);
            SettingsMessage = exception.Message;
        }
        finally
        {
            IsLanguageSwitching = false;
        }
    }

    partial void OnIsAutoUpdateChanged(bool value) => Save(value, setting =>
        Config.Instance.CheckUpdateWhenStartup = setting);

    partial void OnIsStartWithSystemChanged(bool value)
    {
        if (_initializing)
        {
            return;
        }

        try
        {
#if WINDOWS
            WindowsStartupRegistrationService.SetEnabled(
                value,
                Environment.ProcessPath ?? throw new InvalidOperationException("Application path is unavailable."));
#else
            LinuxStartupRegistrationService.SetEnabled(
                value,
                LinuxStartupRegistrationService.GetCurrentApplicationPath());
#endif
            Config.Instance.StartWithWindows = value;
            Config.Instance.SaveUserConfig();
            SettingsMessage = string.Empty;
        }
        catch (Exception exception)
        {
            SettingsMessage = exception.Message;
            _initializing = true;
            IsStartWithSystem = !value;
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
            Logger.Error(
                exception,
                "Failed to download or apply suggested font {FontId}.",
                SelectedSuggestedFont?.Id ?? "unknown");
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
    private async Task ViewLogsAsync()
    {
        var path = AppLevelVariables.Instance.GetPath("logs");
        Directory.CreateDirectory(path);

        if (!await DesktopFolderLauncher.TryOpenAsync(path))
        {
            Logger.Warn("No system folder handler was available for {LogDirectory}.", path);
            LogDirectoryViewRequested?.Invoke(path);
        }

        SettingsMessage = string.Empty;
    }

    [RelayCommand(CanExecute = nameof(CanSendTestWindowsNotification))]
    private void SendTestWindowsNotification() => NotificationRequested?.Invoke(
        AvaloniaLocalization.Get("notification.title"),
        AvaloniaLocalization.Get("notification.test"));

    private bool CanSendTestWindowsNotification() =>
        IsDesktopNotificationSupported && IsWindowsNotificationsEnabled;

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
