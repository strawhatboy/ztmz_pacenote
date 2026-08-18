#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZTMZ.PacenoteTool.Avalonia.Services;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.WebDashboard;

namespace ZTMZ.PacenoteTool.Avalonia.ViewModels;

internal sealed record CoDriverChoice(
    string Identity,
    string DisplayName,
    string Name,
    string Description,
    string GenderSymbol,
    IBrush GenderBrush,
    string Language,
    string Homepage,
    string Version,
    string Path);
internal sealed record AudioOutputChoice(string Id, string DisplayName);
internal sealed record DashboardChoice(
    string Id,
    string Name,
    string Description,
    string Author,
    string Version,
    DashboardOutputTarget OutputTarget,
    Bitmap? PreviewImage);

internal sealed partial class MainWindowViewModel : ObservableObject, IAsyncDisposable
{
    private static readonly IBrush WaitingBrush = new SolidColorBrush(Color.Parse("#66727D"));
    private static readonly IBrush ConnectedBrush = new SolidColorBrush(Color.Parse("#198754"));
    private static readonly IBrush FailedBrush = new SolidColorBrush(Color.Parse("#C13D3D"));

    private readonly DesktopRuntime _runtime = new();
    private bool _applyingSnapshot;
    private string _activeImageFileName = string.Empty;
    private string _dashboardCulture = string.Empty;
    private bool _hasNotificationSnapshot;
    private bool _lastHasActiveGame;
    private bool _lastStageActive;
    private string _lastGameName = string.Empty;
    private string _lastTrackName = string.Empty;

    public Bitmap? AppLogo { get; }

    public string WindowTitle { get; } =
        $"ZTMZ Next Generation Pacenote Tool - {Assembly.GetEntryAssembly()?.GetName().Version}";

    public event EventHandler<string>? NavigationRequested;
    public event Action<string, string>? SystemNotificationRequested;

    [ObservableProperty]
    private string _status = AvaloniaLocalization.Get("avalonia.initializingRuntime");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWaitingForGame))]
    private bool _hasActiveGame;

    [ObservableProperty]
    private string _activeGameName = AvaloniaLocalization.Get("avalonia.waitingForGame");

    [ObservableProperty]
    private string _activeGameDescription = string.Empty;

    [ObservableProperty]
    private string _gameConnectionStatus = AvaloniaLocalization.Get("avalonia.automaticDetectionActive");

    [ObservableProperty]
    private string _gameDetectionSummary = AvaloniaLocalization.Get("avalonia.noSupportedGameDetected");

    [ObservableProperty]
    private IBrush _gameStatusBrush = WaitingBrush;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveGameImage))]
    [NotifyPropertyChangedFor(nameof(ShowsGamePlaceholder))]
    private Bitmap? _activeGameImage;

    [ObservableProperty]
    private string _dashboardStatus = AvaloniaLocalization.Get("avalonia.stopped");

    [ObservableProperty]
    private IBrush _dashboardStatusBrush = WaitingBrush;

    [ObservableProperty]
    private string _stageStatus = AvaloniaLocalization.Get("avalonia.waitingForGame");

    [ObservableProperty]
    private string _currentTrack = string.Empty;

    [ObservableProperty]
    private string _audioStatus = AvaloniaLocalization.Get("avalonia.initializing");

    [ObservableProperty]
    private IBrush _audioStatusBrush = WaitingBrush;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenDashboardCommand))]
    [NotifyPropertyChangedFor(nameof(DashboardDisplayUrl))]
    private string _dashboardUrl = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private int _activeGameCount;

    [ObservableProperty]
    private int _gameCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CoDriverPackageSummary))]
    private int _coDriverCount;

    [ObservableProperty]
    private int _outputDeviceCount;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayExampleCommand))]
    private bool _isReady;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayExampleCommand))]
    private bool _isStageActive;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayExampleCommand))]
    private bool _isMuted;

    [ObservableProperty]
    private bool _isWebDashboardEnabled;

    [ObservableProperty]
    private int _webDashboardPort = Config.Instance.WebDashboardPort;

    [ObservableProperty]
    private int _webDashboardFrameRate = Config.Instance.WebDashboardFrameRate;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayExampleCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenCoDriverHomePageCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenCoDriverFolderCommand))]
    private CoDriverChoice? _selectedCoDriver;

    [ObservableProperty]
    private AudioOutputChoice? _selectedOutputDevice;

    [ObservableProperty]
    private double _playbackVolume = 50d;

    [ObservableProperty]
    private double _playbackAdjustSeconds;

    [ObservableProperty]
    private double _playbackSpeed = 1d;

    [ObservableProperty]
    private int _silenceRemovalLevel;

    [ObservableProperty]
    private int _intercomEffect;

    public MainWindowViewModel()
    {
        AppLogo = LoadBitmap(Path.Combine(AppContext.BaseDirectory, "Assets", "app.png"));
        _runtime.SnapshotChanged += (_, _) => Dispatcher.UIThread.Post(RefreshSnapshot);
        AvaloniaLocalization.CultureChanged += OnCultureChanged;
    }

    public ObservableCollection<CoDriverChoice> CoDrivers { get; } = [];

    public ObservableCollection<AudioOutputChoice> OutputDevices { get; } = [];

    public ObservableCollection<DashboardChoice> Dashboards { get; } = [];

    public bool IsWaitingForGame => !HasActiveGame;

    public bool HasActiveGameImage => ActiveGameImage != null;

    public bool ShowsGamePlaceholder => ActiveGameImage == null;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public string CoDriverPackageSummary =>
        AvaloniaLocalization.Format("avalonia.packagesAvailable", CoDriverCount);

    public string DashboardDisplayUrl => string.IsNullOrWhiteSpace(DashboardUrl)
        ? AvaloniaLocalization.Get("settings.tooltip.webDashboardStatusStopped")
        : DashboardUrl;

    public async Task InitializeAsync()
    {
        await _runtime.InitializeAsync();
        RefreshSnapshot();
    }

    public async ValueTask DisposeAsync()
    {
        AvaloniaLocalization.CultureChanged -= OnCultureChanged;
        var activeGameImage = ActiveGameImage;
        ActiveGameImage = null;
        foreach (var dashboard in Dashboards)
        {
            dashboard.PreviewImage?.Dispose();
        }
        Dashboards.Clear();
        await _runtime.DisposeAsync().ConfigureAwait(false);
        activeGameImage?.Dispose();
        AppLogo?.Dispose();
    }

    partial void OnIsMutedChanged(bool value)
    {
        AudioStatus = value
            ? AvaloniaLocalization.Get("avalonia.muted")
            : IsReady
                ? AvaloniaLocalization.Get("avalonia.ready")
                : AvaloniaLocalization.Get("avalonia.initializing");
        AudioStatusBrush = value || !IsReady ? WaitingBrush : ConnectedBrush;

        if (!_applyingSnapshot)
        {
            _runtime.Controls?.SetMuted(value);
        }
    }

    partial void OnIsReadyChanged(bool value)
    {
        AudioStatus = IsMuted
            ? AvaloniaLocalization.Get("avalonia.muted")
            : value
                ? AvaloniaLocalization.Get("avalonia.ready")
                : AvaloniaLocalization.Get("avalonia.initializing");
        AudioStatusBrush = IsMuted || !value ? WaitingBrush : ConnectedBrush;
    }

    partial void OnIsWebDashboardEnabledChanged(bool value)
    {
        if (!_applyingSnapshot)
        {
            _runtime.Controls?.SetWebDashboardEnabled(value);
        }
    }

    partial void OnWebDashboardPortChanged(int value)
    {
        if (!_applyingSnapshot)
        {
            _runtime.Controls?.ConfigureWebDashboard(value, WebDashboardFrameRate);
        }
    }

    partial void OnWebDashboardFrameRateChanged(int value)
    {
        if (!_applyingSnapshot)
        {
            _runtime.Controls?.ConfigureWebDashboard(WebDashboardPort, value);
        }
    }

    partial void OnSelectedCoDriverChanged(CoDriverChoice? value)
    {
        if (!_applyingSnapshot && value != null)
        {
            _runtime.Controls?.TrySelectCodriver(value.Identity);
        }
    }

    partial void OnSelectedOutputDeviceChanged(AudioOutputChoice? value)
    {
        if (!_applyingSnapshot && value != null)
        {
            _runtime.Controls?.TrySelectOutputDevice(value.Id);
        }
    }

    partial void OnPlaybackVolumeChanged(double value)
    {
        if (!_applyingSnapshot)
        {
            _runtime.Controls?.SetPlaybackVolume(value);
        }
    }

    partial void OnPlaybackAdjustSecondsChanged(double value)
    {
        if (!_applyingSnapshot)
        {
            _runtime.Controls?.SetPlaybackAdjustSeconds(value);
        }
    }

    partial void OnPlaybackSpeedChanged(double value)
    {
        if (!_applyingSnapshot)
        {
            _runtime.Controls?.SetPlaybackSpeed(value);
        }
    }

    partial void OnSilenceRemovalLevelChanged(int value)
    {
        if (!_applyingSnapshot)
        {
            _runtime.Controls?.SetSilenceRemovalLevel(value);
        }
    }

    partial void OnIntercomEffectChanged(int value)
    {
        if (!_applyingSnapshot)
        {
            _runtime.Controls?.SetIntercomEffect(value);
        }
    }

    private void RefreshSnapshot()
    {
        var snapshot = _runtime.GetSnapshot();
        _applyingSnapshot = true;
        try
        {
            Status = LocalizeRuntimeStatus(snapshot.Status);
            ApplyActiveGame(snapshot.ActiveGame, snapshot.ActiveGameCount);
            CurrentTrack = snapshot.CurrentTrack;
            GameCount = snapshot.GameCount;
            CoDriverCount = snapshot.CoDriverCount;
            OutputDeviceCount = snapshot.OutputDeviceCount;
            IsReady = snapshot.IsReady;
            ErrorMessage = snapshot.ErrorMessage ?? string.Empty;
            ApplyDashboards(snapshot.Dashboards);

            var controls = snapshot.Controls;
            if (controls != null)
            {
                var choices = controls.Codrivers
                    .Select(info => new CoDriverChoice(
                        CoDriverSelectionIdentity.Create(info),
                        info.DisplayText,
                        info.name ?? string.Empty,
                        info.description ?? string.Empty,
                        info.gender switch
                        {
                            "M" => "♂",
                            "F" => "♀",
                            _ => "?"
                        },
                        info.gender switch
                        {
                            "M" => new SolidColorBrush(Color.Parse("#4169E1")),
                            "F" => new SolidColorBrush(Color.Parse("#FF4FA3")),
                            _ => WaitingBrush
                        },
                        info.language ?? string.Empty,
                        info.homepage ?? string.Empty,
                        info.version ?? string.Empty,
                        info.Path ?? string.Empty))
                    .ToArray();
                if (!CoDrivers.Select(CoDriverDisplayIdentity).SequenceEqual(
                        choices.Select(CoDriverDisplayIdentity)))
                {
                    CoDrivers.Clear();
                    foreach (var choice in choices)
                    {
                        CoDrivers.Add(choice);
                    }
                }

                var selectedIdentity = controls.SelectedCodriver == null
                    ? string.Empty
                    : CoDriverSelectionIdentity.Create(controls.SelectedCodriver);
                SelectedCoDriver = CoDrivers.FirstOrDefault(choice =>
                    string.Equals(choice.Identity, selectedIdentity, StringComparison.Ordinal));

                var outputChoices = controls.OutputDevices
                    .Select(device => new AudioOutputChoice(
                        device.Id,
                        device.IsDefault
                            ? AvaloniaLocalization.Format("avalonia.defaultDevice", device.Name)
                            : device.Name))
                    .ToArray();
                if (!OutputDevices.SequenceEqual(outputChoices))
                {
                    OutputDevices.Clear();
                    foreach (var choice in outputChoices)
                    {
                        OutputDevices.Add(choice);
                    }
                }

                SelectedOutputDevice = controls.SelectedOutputDevice == null
                    ? null
                    : OutputDevices.FirstOrDefault(choice => string.Equals(
                        choice.Id,
                        controls.SelectedOutputDevice.Id,
                        StringComparison.OrdinalIgnoreCase));
                IsStageActive = controls.IsStageActive;
                IsMuted = controls.IsMuted;
                PlaybackVolume = controls.PlaybackVolume;
                PlaybackAdjustSeconds = controls.PlaybackAdjustSeconds;
                PlaybackSpeed = controls.PlaybackSpeed;
                SilenceRemovalLevel = controls.SilenceRemovalLevel;
                IntercomEffect = controls.IntercomEffect;
                IsWebDashboardEnabled = controls.IsWebDashboardEnabled;
                DashboardStatus = LocalizeDashboardStatus(controls.WebDashboardStatus.State);
                DashboardStatusBrush = controls.WebDashboardStatus.IsRunning
                    ? ConnectedBrush
                    : WaitingBrush;
                StageStatus = controls.IsStageActive
                    ? AvaloniaLocalization.Get("avalonia.stageActive")
                    : HasActiveGame
                        ? AvaloniaLocalization.Get("avalonia.waitingForStage")
                        : AvaloniaLocalization.Get("avalonia.waitingForGame");
                DashboardUrl = controls.WebDashboardStatus.IsRunning
                    ? controls.WebDashboardStatus.Url
                    : string.Empty;
            }
        }
        finally
        {
            _applyingSnapshot = false;
        }

        PublishStateNotifications();
    }

    private void PublishStateNotifications()
    {
        var gameName = HasActiveGame ? ActiveGameName : _lastGameName;
        var trackName = string.IsNullOrWhiteSpace(CurrentTrack) ? gameName : CurrentTrack;
        if (_hasNotificationSnapshot && Config.Instance.EnableWindowsNotifications)
        {
            if (!_lastHasActiveGame && HasActiveGame)
            {
                PublishNotification("notification.gameStarted", ActiveGameName);
            }
            else if (_lastHasActiveGame && !HasActiveGame)
            {
                PublishNotification("notification.gameEnded", _lastGameName);
            }

            if (!_lastStageActive && IsStageActive)
            {
                PublishNotification("notification.stageStarted", trackName);
            }
            else if (_lastStageActive && !IsStageActive)
            {
                PublishNotification("notification.stageFinished", _lastTrackName);
            }
        }

        _hasNotificationSnapshot = true;
        _lastHasActiveGame = HasActiveGame;
        _lastStageActive = IsStageActive;
        _lastGameName = gameName;
        _lastTrackName = trackName;
    }

    private void PublishNotification(string messageKey, string argument) =>
        SystemNotificationRequested?.Invoke(
            AvaloniaLocalization.Get("notification.title"),
            AvaloniaLocalization.Format(messageKey, argument));

    private void ApplyActiveGame(DesktopGameSnapshot? game, int activeGameCount)
    {
        ActiveGameCount = activeGameCount;
        HasActiveGame = game != null;
        if (game == null)
        {
            ActiveGameName = AvaloniaLocalization.Get("avalonia.waitingForGame");
            ActiveGameDescription = string.Empty;
            GameConnectionStatus = AvaloniaLocalization.Get("avalonia.automaticDetectionActive");
            GameDetectionSummary = AvaloniaLocalization.Get("avalonia.noSupportedGameDetected");
            GameStatusBrush = WaitingBrush;
            SetActiveGameImage(string.Empty);
            return;
        }

        ActiveGameName = game.Name;
        ActiveGameDescription = game.Description;
        GameConnectionStatus = game.ConnectionState == DesktopGameConnectionState.Connected
            ? AvaloniaLocalization.Get("avalonia.connected")
            : AvaloniaLocalization.Get("avalonia.gameConnectionFailed");
        GameDetectionSummary = activeGameCount > 1
            ? AvaloniaLocalization.Format("avalonia.gamesDetectedUsing", activeGameCount, game.Name)
            : AvaloniaLocalization.Get("avalonia.selectedAutomatically");
        GameStatusBrush = game.ConnectionState == DesktopGameConnectionState.Connected
            ? ConnectedBrush
            : FailedBrush;
        SetActiveGameImage(game.ImageFileName);
    }

    private void SetActiveGameImage(string imageFileName)
    {
        if (string.Equals(_activeImageFileName, imageFileName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _activeImageFileName = imageFileName;
        Bitmap? nextImage = null;
        if (!string.IsNullOrWhiteSpace(imageFileName))
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Games", imageFileName);
            if (File.Exists(path))
            {
                nextImage = LoadBitmap(path);
            }
        }

        var previousImage = ActiveGameImage;
        ActiveGameImage = nextImage;
        previousImage?.Dispose();
    }

    private void ApplyDashboards(IReadOnlyList<WebDashboardDefinition> definitions)
    {
        var culture = I18NLoader.Instance.CurrentCultureName ?? string.Empty;
        if (string.Equals(_dashboardCulture, culture, StringComparison.OrdinalIgnoreCase) &&
            Dashboards.Select(item => item.Id).SequenceEqual(definitions.Select(item => item.Id)))
        {
            return;
        }

        foreach (var dashboard in Dashboards)
        {
            dashboard.PreviewImage?.Dispose();
        }
        Dashboards.Clear();
        _dashboardCulture = culture;

        foreach (var definition in definitions)
        {
            var previewPath = string.IsNullOrWhiteSpace(definition.PreviewImagePath)
                ? string.Empty
                : System.IO.Path.Combine(definition.PackagePath, definition.PreviewImagePath);
            Dashboards.Add(new DashboardChoice(
                definition.Id,
                AvaloniaLocalization.Get(definition.Name),
                AvaloniaLocalization.Get(definition.Description),
                definition.Author,
                definition.Version,
                definition.GetOutputTarget(),
                LoadBitmap(previewPath)));
        }

    }

    private static string CoDriverDisplayIdentity(CoDriverChoice choice) => string.Join(
        '\u001f',
        choice.Identity,
        choice.DisplayName,
        choice.Name,
        choice.Description,
        choice.GenderSymbol,
        choice.Language,
        choice.Homepage,
        choice.Version,
        choice.Path);

    [RelayCommand(CanExecute = nameof(CanPlayExample))]
    private void PlayExample() => _runtime.Controls?.TryPlayExample();

    private bool CanPlayExample() =>
        IsReady && !IsStageActive && !IsMuted && SelectedCoDriver != null;

    [RelayCommand(CanExecute = nameof(CanOpenCoDriverHomePage))]
    private void OpenCoDriverHomePage() => OpenExternal(SelectedCoDriver?.Homepage);

    private bool CanOpenCoDriverHomePage() => Uri.TryCreate(
        SelectedCoDriver?.Homepage,
        UriKind.Absolute,
        out _);

    [RelayCommand(CanExecute = nameof(CanOpenCoDriverFolder))]
    private void OpenCoDriverFolder() => OpenExternal(SelectedCoDriver?.Path);

    private bool CanOpenCoDriverFolder() =>
        !string.IsNullOrWhiteSpace(SelectedCoDriver?.Path) &&
        Directory.Exists(SelectedCoDriver.Path);

    [RelayCommand]
    private void MoreCoDriverSettings() => RequestNavigation("codriver");

    [RelayCommand]
    private void OpenDashboardSettings() => RequestNavigation("dashboard");

    [RelayCommand]
    private void ShowHelp() => OpenExternal(
        Path.Combine(AppContext.BaseDirectory, "Assets", "ztmz_ngptool_3.0_help.png"));

    [RelayCommand]
    private void RaiseIssue() => OpenExternal(
        string.Equals(Config.Instance.Language, "zh-cn", StringComparison.OrdinalIgnoreCase)
            ? "https://gitee.com/ztmz/ztmz_pacenote/issues"
            : "https://github.com/strawhatboy/ztmz_pacenote/issues");

    [RelayCommand]
    private void BuyMeACoffee() => OpenExternal(
        "https://gitee.com/ztmz/ztmz_pacenote/tree/master/buy_me_a_coffee");

    [RelayCommand(CanExecute = nameof(CanOpenDashboard))]
    private void OpenDashboard()
    {
        try
        {
            Process.Start(new ProcessStartInfo(DashboardUrl) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    private bool CanOpenDashboard() => !string.IsNullOrWhiteSpace(DashboardUrl);

    private void RequestNavigation(string pageKey) =>
        NavigationRequested?.Invoke(this, pageKey);

    private void OpenExternal(string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    private void OnCultureChanged(object? sender, EventArgs eventArgs)
    {
        Dispatcher.UIThread.Post(() =>
        {
            OnPropertyChanged(nameof(CoDriverPackageSummary));
            OnPropertyChanged(nameof(DashboardDisplayUrl));
            RefreshSnapshot();
        });
    }

    private static string LocalizeDashboardStatus(string state) => state switch
    {
        "Running" => AvaloniaLocalization.Get("avalonia.dashboardRunning"),
        "PortUnavailable" => AvaloniaLocalization.Get("avalonia.dashboardPortUnavailable"),
        "Error" => AvaloniaLocalization.Get("avalonia.dashboardError"),
        _ => AvaloniaLocalization.Get("avalonia.stopped")
    };

    private static string LocalizeRuntimeStatus(string status)
    {
        var exactKey = status switch
        {
            "Initializing runtime..." => "avalonia.initializingRuntime",
            "Loading pacenote definitions..." => "avalonia.loadingPacenoteDefinitions",
            "Pacenote definitions loaded." => "avalonia.pacenoteDefinitionsLoaded",
            "Loading profile manager..." => "avalonia.loadingProfileManager",
            "Profile manager loaded." => "avalonia.profileManagerLoaded",
            "Loading games..." => "avalonia.loadingGames",
            "Loading profiles..." => "avalonia.loadingProfiles",
            "Loading codrivers..." => "avalonia.loadingCodrivers",
            "Loading output devices..." => "avalonia.loadingOutputDevices",
            "Ready" => "avalonia.ready",
            "Initialization failed" => "avalonia.initializationFailed",
            _ => string.Empty
        };
        if (!string.IsNullOrEmpty(exactKey))
        {
            return AvaloniaLocalization.Get(exactKey);
        }

        foreach (var candidate in new[]
                 {
                     (Suffix: " games loaded.", Key: "avalonia.gamesLoaded"),
                     (Suffix: " profiles loaded.", Key: "avalonia.profilesLoaded"),
                     (Suffix: " codrivers loaded.", Key: "avalonia.codriversLoaded"),
                     (Suffix: " output devices loaded.", Key: "avalonia.outputDevicesLoaded")
                 })
        {
            if (status.EndsWith(candidate.Suffix, StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(
                    status[..^candidate.Suffix.Length],
                    out var count))
            {
                return AvaloniaLocalization.Format(candidate.Key, count);
            }
        }

        return status;
    }

    private static Bitmap? LoadBitmap(string path) =>
        File.Exists(path) ? new Bitmap(path) : null;
}
