#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;
using ZTMZ.PacenoteTool.Avalonia.Models;
using ZTMZ.PacenoteTool.Avalonia.Services;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.Core;
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
internal sealed partial class VoicePackageChoice : ObservableObject
{
    public VoicePackageChoice(VoicePackageSnapshot package)
    {
        Id = package.Id;
        Name = package.Name;
        Description = package.Description;
        Gender = package.Gender;
        Language = package.Language;
        Homepage = package.Homepage;
        Version = package.Version;
        OriginalVersion = package.OriginalVersion;
        PackagePath = package.PackagePath;
        DownloadUrl = package.DownloadUrl;
        NeedsUpdate = package.NeedsUpdate;
        NeedsDownload = package.NeedsDownload;
        IsInstalled = package.IsInstalled;
        IntegritySimple = package.IntegritySimple;
        IntegrityNormal = package.IntegrityNormal;
        IntegrityComplex = package.IntegrityComplex;
    }

    public string Id { get; }
    public string Name { get; }
    public string Description { get; }
    public string Gender { get; }
    public string Language { get; }
    public string Homepage { get; }
    public string Version { get; }
    public string OriginalVersion { get; }
    public string PackagePath { get; set; }
    public string DownloadUrl { get; }
    public bool NeedsUpdate { get; set; }
    public bool NeedsDownload { get; set; }
    public bool IsInstalled { get; set; }
    public float IntegritySimple { get; }
    public float IntegrityNormal { get; }
    public float IntegrityComplex { get; }
    public string GenderSymbol => Gender switch { "M" => "♂", "F" => "♀", _ => "?" };
    public string ActionLabel => AvaloniaLocalization.Get(NeedsDownload ? "misc.download" : "misc.update");
    public IBrush GenderBrush => Gender switch
    {
        "M" => new SolidColorBrush(Color.Parse("#4169E1")),
        "F" => new SolidColorBrush(Color.Parse("#FF4FA3")),
        _ => new SolidColorBrush(Color.Parse("#66727D"))
    };
    public bool NeedsAction => NeedsUpdate || NeedsDownload;

    [ObservableProperty] private bool _isDownloading;
    [ObservableProperty] private bool _isInstalling;
    [ObservableProperty] private double _downloadProgress;
}
internal sealed record VoicePackageAudioFileChoice(int Index, string Path);
internal sealed class VoicePackageTokenChoice
{
    public VoicePackageTokenChoice(VoicePackageTokenSnapshot token)
    {
        Id = token.Id;
        Token = token.Token;
        Description = token.Description;
        Type = token.Type;
        Files = token.AudioFiles.Select((path, index) =>
            new VoicePackageAudioFileChoice(index + 1, path)).ToArray();
    }

    public int Id { get; }
    public string Token { get; }
    public string Description { get; }
    public string Type { get; }
    public IReadOnlyList<VoicePackageAudioFileChoice> Files { get; }
    public int FilesCount => Files.Count;
    public bool IsAvailable => FilesCount > 0;
    public string AvailabilityGlyph => IsAvailable ? "✓" : "✕";
    public IBrush AvailabilityBrush => IsAvailable
        ? new SolidColorBrush(Color.Parse("#198754"))
        : new SolidColorBrush(Color.Parse("#66727D"));
}
internal sealed partial class DashboardSettingChoice : ObservableObject
{
    private readonly Action<DashboardSettingChoice> _save;

    public DashboardSettingChoice(DashboardPackageSetting setting, Action<DashboardSettingChoice> save)
    {
        Key = setting.Key;
        Name = AvaloniaLocalization.Get(setting.Key);
        Tooltip = AvaloniaLocalization.Get(setting.TooltipKey);
        ValueRange = setting.ValueRange;
        _save = save;
        _valueKind = setting.Value switch
        {
            bool => DashboardSettingValueKind.Boolean,
            string or null => DashboardSettingValueKind.Text,
            _ => DashboardSettingValueKind.Numeric
        };
        switch (setting.Value)
        {
            case bool boolean:
                BooleanValue = boolean;
                break;
            case string text:
                TextValue = text;
                break;
            case null:
                TextValue = string.Empty;
                break;
            default:
                NumericValue = Convert.ToDouble(setting.Value, System.Globalization.CultureInfo.InvariantCulture);
                break;
        }
    }

    public string Key { get; }
    public string Name { get; }
    public string Tooltip { get; }
    public IReadOnlyList<double> ValueRange { get; }
    public bool IsBoolean => ValueKind == DashboardSettingValueKind.Boolean;
    public bool IsText => ValueKind == DashboardSettingValueKind.Text;
    public bool IsNumeric => ValueKind == DashboardSettingValueKind.Numeric;
    public bool HasRange => ValueRange.Count >= 2;
    public bool IsHorizontalPosition => string.Equals(Key, "dashboards.settings.positionH", StringComparison.Ordinal);
    public bool IsVerticalPosition => string.Equals(Key, "dashboards.settings.positionV", StringComparison.Ordinal);
    public bool IsPlainNumeric => IsNumeric && !HasRange && !IsHorizontalPosition && !IsVerticalPosition;
    public bool IsRangedNumeric => IsNumeric && HasRange && !IsHorizontalPosition && !IsVerticalPosition;
    public IReadOnlyList<string> HorizontalPositions =>
    [
        AvaloniaLocalization.Get("dashboards.settings.left"),
        AvaloniaLocalization.Get("dashboards.settings.center"),
        AvaloniaLocalization.Get("dashboards.settings.right")
    ];
    public IReadOnlyList<string> VerticalPositions =>
    [
        AvaloniaLocalization.Get("dashboards.settings.top"),
        AvaloniaLocalization.Get("dashboards.settings.center"),
        AvaloniaLocalization.Get("dashboards.settings.bottom")
    ];

    private readonly DashboardSettingValueKind _valueKind;
    private DashboardSettingValueKind ValueKind => _valueKind;

    [ObservableProperty]
    private bool _booleanValue;

    [ObservableProperty]
    private string _textValue = string.Empty;

    [ObservableProperty]
    private double _numericValue;

    [ObservableProperty]
    private int _positionIndex;

    partial void OnBooleanValueChanged(bool value) => Save();
    partial void OnTextValueChanged(string value) => Save();
    partial void OnNumericValueChanged(double value) => Save();
    partial void OnPositionIndexChanged(int value)
    {
        NumericValue = value - 1;
        Save();
    }

    public void InitializePosition()
    {
        if (IsHorizontalPosition || IsVerticalPosition)
        {
            PositionIndex = Math.Clamp((int)Math.Round(NumericValue) + 1, 0, 2);
        }
    }

    public object GetValue() => ValueKind switch
    {
        DashboardSettingValueKind.Boolean => BooleanValue,
        DashboardSettingValueKind.Text => TextValue,
        _ => NumericValue
    };

    private void Save()
    {
        if (_initializing)
        {
            return;
        }
        _save(this);
    }

    private bool _initializing = true;

    public void CompleteInitialization()
    {
        InitializePosition();
        _initializing = false;
    }

    private enum DashboardSettingValueKind { Boolean, Text, Numeric }
}

internal sealed partial class DashboardChoice : ObservableObject
{
    private readonly Action<DashboardChoice, DashboardOutputTarget> _setOutputTarget;
    private bool _changingOutputTarget;

    public DashboardChoice(
        string id,
        string packagePath,
        string name,
        string description,
        string author,
        string version,
        DashboardOutputTarget outputTarget,
        Bitmap? previewImage,
        Action<DashboardChoice, DashboardOutputTarget> setOutputTarget)
    {
        Id = id;
        PackagePath = packagePath;
        Name = name;
        Description = description;
        Author = author;
        Version = version;
        PreviewImage = previewImage;
        _setOutputTarget = setOutputTarget;
        SetOutputTarget(outputTarget, persist: false);
    }

    public string Id { get; }
    public string PackagePath { get; }
    public string Name { get; }
    public string Description { get; }
    public string Author { get; }
    public string Version { get; }
    public Bitmap? PreviewImage { get; }
    public ObservableCollection<DashboardSettingChoice> Settings { get; } = [];

    [ObservableProperty] private bool _isOutputOff;
    [ObservableProperty] private bool _isOutputOverlay;
    [ObservableProperty] private bool _isOutputWeb;
    [ObservableProperty] private bool _isOutputBoth;

    partial void OnIsOutputOffChanged(bool value) => HandleOutputToggle(value, DashboardOutputTarget.None);
    partial void OnIsOutputOverlayChanged(bool value) => HandleOutputToggle(value, DashboardOutputTarget.Overlay);
    partial void OnIsOutputWebChanged(bool value) => HandleOutputToggle(value, DashboardOutputTarget.Web);
    partial void OnIsOutputBothChanged(bool value) => HandleOutputToggle(value, DashboardOutputTarget.Both);

    private void HandleOutputToggle(bool value, DashboardOutputTarget target)
    {
        if (_changingOutputTarget)
        {
            return;
        }

        // WPF's four-button selector always has exactly one selected target.
        SetOutputTarget(target, persist: value);
    }

    public void SetOutputTarget(DashboardOutputTarget target, bool persist = true)
    {
        _changingOutputTarget = true;
        try
        {
            IsOutputOff = target == DashboardOutputTarget.None;
            IsOutputOverlay = target == DashboardOutputTarget.Overlay;
            IsOutputWeb = target == DashboardOutputTarget.Web;
            IsOutputBoth = target == DashboardOutputTarget.Both;
        }
        finally
        {
            _changingOutputTarget = false;
        }
        if (persist)
        {
            _setOutputTarget(this, target);
        }
    }
}

internal sealed partial class StartupStatusEntry : ObservableObject
{
    public StartupStatusEntry(string text, bool isCurrent)
    {
        _text = text;
        _isCurrent = isCurrent;
    }

    [ObservableProperty]
    private string _text;

    [ObservableProperty]
    private bool _isCurrent;
}

internal sealed partial class MainWindowViewModel : ObservableObject, IAsyncDisposable
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    private static readonly IBrush WaitingBrush = new SolidColorBrush(Color.Parse("#66727D"));
    private static readonly IBrush ConnectedBrush = new SolidColorBrush(Color.Parse("#198754"));
    private static readonly IBrush FailedBrush = new SolidColorBrush(Color.Parse("#C13D3D"));

    private readonly DesktopRuntime _runtime = new();
    private bool _applyingSnapshot;
    private string _activeImageFileName = string.Empty;
    private string _dashboardCulture = string.Empty;
    private IGame? _gameSettingsGame;
    private bool _hasNotificationSnapshot;
    private bool _lastHasActiveGame;
    private bool _lastStageActive;
    private string _lastGameName = string.Empty;
    private string _lastTrackName = string.Empty;
    private readonly DispatcherTimer _dashboardReloadTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private readonly SemaphoreSlim _dashboardOperationGate = new(1, 1);
    private long _dashboardOperationVersion;
    private long _dashboardReloadVersion;
    private int _isDisposing;

    public Bitmap? AppLogo { get; }

    public string WindowTitle { get; } =
        $"ZTMZ Next Generation Pacenote Tool - {Assembly.GetEntryAssembly()?.GetName().Version}";

    public event EventHandler<string>? NavigationRequested;
    public event Action<string, string>? SystemNotificationRequested;
    public event Action<string>? FolderOpenRequested;

    [ObservableProperty]
    private string _status = AvaloniaLocalization.Get("runtime.initializingRuntime");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWaitingForGame))]
    private bool _hasActiveGame;

    [ObservableProperty]
    private string _activeGameName = AvaloniaLocalization.Get("gameSession.waitingForGame");

    [ObservableProperty]
    private string _activeGameDescription = string.Empty;

    [ObservableProperty]
    private string _gameConnectionStatus = AvaloniaLocalization.Get("gameSession.automaticDetectionActive");

    [ObservableProperty]
    private string _gameDetectionSummary = AvaloniaLocalization.Get("gameSession.noSupportedGameDetected");

    [ObservableProperty]
    private IBrush _gameStatusBrush = WaitingBrush;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveGameImage))]
    [NotifyPropertyChangedFor(nameof(ShowsGamePlaceholder))]
    private Bitmap? _activeGameImage;

    [ObservableProperty]
    private string _dashboardStatus = AvaloniaLocalization.Get("dashboardStatus.stopped");

    [ObservableProperty]
    private IBrush _dashboardStatusBrush = WaitingBrush;

    [ObservableProperty]
    private string _stageStatus = AvaloniaLocalization.Get("gameSession.waitingForGame");

    [ObservableProperty]
    private string _currentTrack = string.Empty;

    [ObservableProperty]
    private string _audioStatus = AvaloniaLocalization.Get("audioStatus.initializing");

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
    private bool _isNativeOverlaySupported;

    [ObservableProperty]
    private bool _isNativeOverlayEnabled;

    [ObservableProperty]
    private bool _isNativeOverlayInitializing;

    [ObservableProperty]
    private int _nativeOverlayFrameRate = Config.Instance.HudFPS;

    [ObservableProperty]
    private bool _isNativeOverlayFrameRateLocked = Config.Instance.HudLockFPS;

    [ObservableProperty]
    private bool _isNativeOverlayTopMost = Config.Instance.HudTopMost;

    [ObservableProperty]
    private string _nativeOverlayStatus = string.Empty;

    [ObservableProperty]
    private IBrush _nativeOverlayStatusBrush = WaitingBrush;

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

    [ObservableProperty]
    private bool _isVoicePackageCatalogLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVoicePackageListVisible))]
    [NotifyPropertyChangedFor(nameof(VoiceBreadcrumbItems))]
    [NotifyPropertyChangedFor(nameof(NavigationBreadcrumbItems))]
    [NotifyPropertyChangedFor(nameof(HasNavigationBreadcrumb))]
    private bool _isVoicePackageDetailsOpen;

    [ObservableProperty]
    private bool _isVoicePackageDetailsLoading;

    private int _voicePackageDetailsLoadVersion;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVoicePackageListVisible))]
    [NotifyPropertyChangedFor(nameof(VoiceBreadcrumbItems))]
    [NotifyPropertyChangedFor(nameof(NavigationBreadcrumbItems))]
    [NotifyPropertyChangedFor(nameof(HasNavigationBreadcrumb))]
    private bool _isVoiceSettingsOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VoiceBreadcrumbItems))]
    [NotifyPropertyChangedFor(nameof(NavigationBreadcrumbItems))]
    [NotifyPropertyChangedFor(nameof(HasNavigationBreadcrumb))]
    private VoicePackageChoice? _selectedVoicePackage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NavigationBreadcrumbItems))]
    [NotifyPropertyChangedFor(nameof(HasNavigationBreadcrumb))]
    private string _activeNavigationPage = "home";

    // ── Game Settings Panes ──────────────────────────────────────────────

    [ObservableProperty]
    private ObservableCollection<object> _currentGameSettings = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGameSettingsLocked))]
    private bool _isGameSettingsUnlocked;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGameSettingsLocked))]
    private bool _hasGameSettingsContent;

    [ObservableProperty]
    private bool _isLanguageSwitchInProgress;

    public bool IsGameSettingsLocked => HasGameSettingsContent && !IsGameSettingsUnlocked;

    public IGame? CurrentActiveGame => _runtime.CurrentGame;

    // ── end Game Settings ────────────────────────────────────────────────

    public MainWindowViewModel()
    {
        AppLogo = LoadBitmap(Path.Combine(AppContext.BaseDirectory, "Assets", "app.png"));
        _dashboardReloadTimer.Tick += (_, _) =>
        {
            _dashboardReloadTimer.Stop();
            _ = ReloadDashboardsAsync(Volatile.Read(ref _dashboardReloadVersion));
        };
        UpdateStartupStatusHistory(Status);
        _runtime.SnapshotChanged += (_, _) => Dispatcher.UIThread.Post(RefreshSnapshot);
        AvaloniaLocalization.CultureChanging += OnCultureChanging;
        AvaloniaLocalization.CultureChanged += OnCultureChanged;
    }

    public ObservableCollection<CoDriverChoice> CoDrivers { get; } = [];

    public ObservableCollection<AudioOutputChoice> OutputDevices { get; } = [];

    public ObservableCollection<DashboardChoice> Dashboards { get; } = [];

    public ObservableCollection<VoicePackageChoice> VoicePackages { get; } = [];

    public ObservableCollection<VoicePackageTokenChoice> VoicePackageDetails { get; } = [];

    public VoiceSettingsViewModel VoiceSettings { get; } = new();

    public bool IsVoicePackageListVisible => !IsVoicePackageDetailsOpen && !IsVoiceSettingsOpen;

    public IReadOnlyList<string> VoiceBreadcrumbItems => IsVoiceSettingsOpen
        ? [AvaloniaLocalization.Get("tabs.voices"), AvaloniaLocalization.Get("settings.subsection.voicePackage")]
        : IsVoicePackageDetailsOpen && SelectedVoicePackage != null
            ? [AvaloniaLocalization.Get("tabs.voices"), SelectedVoicePackage.Name]
            : [AvaloniaLocalization.Get("tabs.voices")];

    public IReadOnlyList<string> NavigationBreadcrumbItems =>
        string.Equals(ActiveNavigationPage, "codriver", StringComparison.OrdinalIgnoreCase)
            ? VoiceBreadcrumbItems
            : [AvaloniaLocalization.Get(ActiveNavigationPage switch
            {
                "home" => "tabs.home",
                "general" => "tabs.general",
                "play" => "tabs.playback",
                "dashboard" => "tabs.dashboard",
                "scriptEditor" => "tabs.scriptEditor",
                "about" => "tabs.about",
                _ => "tabs.home"
            })];

    public bool HasNavigationBreadcrumb => NavigationBreadcrumbItems.Count > 1;

    // The splash keeps a compact, ordered trace of genuine runtime startup stages.
    public ObservableCollection<StartupStatusEntry> StartupStatusHistory { get; } = [];

    public bool IsWaitingForGame => !HasActiveGame;

    public bool HasActiveGameImage => ActiveGameImage != null;

    public bool ShowsGamePlaceholder => ActiveGameImage == null;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public string CoDriverPackageSummary =>
        AvaloniaLocalization.Format("codriverCatalog.packagesAvailable", CoDriverCount);

    public string DashboardDisplayUrl => string.IsNullOrWhiteSpace(DashboardUrl)
        ? AvaloniaLocalization.Get("settings.tooltip.webDashboardStatusStopped")
        : DashboardUrl;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _runtime.InitializeAsync(cancellationToken);
        RefreshSnapshot();
        _ = RefreshVoicePackagesAsync();
    }

    public async ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _isDisposing, 1);
        Interlocked.Increment(ref _dashboardOperationVersion);
        Interlocked.Increment(ref _dashboardReloadVersion);
        _dashboardReloadTimer.Stop();
        AvaloniaLocalization.CultureChanging -= OnCultureChanging;
        AvaloniaLocalization.CultureChanged -= OnCultureChanged;
        await _dashboardOperationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            DisposeGameSettingsPanes();
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
        finally
        {
            _dashboardOperationGate.Release();
        }
    }

    partial void OnIsMutedChanged(bool value)
    {
        AudioStatus = value
            ? AvaloniaLocalization.Get("audioStatus.muted")
            : IsReady
                ? AvaloniaLocalization.Get("runtime.ready")
                : AvaloniaLocalization.Get("audioStatus.initializing");
        AudioStatusBrush = value || !IsReady ? WaitingBrush : ConnectedBrush;

        if (!_applyingSnapshot)
        {
            _runtime.Controls?.SetMuted(value);
        }
    }

    partial void OnIsReadyChanged(bool value)
    {
        AudioStatus = IsMuted
            ? AvaloniaLocalization.Get("audioStatus.muted")
            : value
                ? AvaloniaLocalization.Get("runtime.ready")
                : AvaloniaLocalization.Get("audioStatus.initializing");
        AudioStatusBrush = IsMuted || !value ? WaitingBrush : ConnectedBrush;
    }

    partial void OnIsWebDashboardEnabledChanged(bool value)
    {
        if (!_applyingSnapshot && Volatile.Read(ref _isDisposing) == 0)
        {
            var operationVersion = Interlocked.Increment(ref _dashboardOperationVersion);
            _ = SetWebDashboardEnabledAsync(value, operationVersion);
        }
    }

    private async Task SetWebDashboardEnabledAsync(bool enabled, long operationVersion)
    {
        try
        {
            await _dashboardOperationGate.WaitAsync().ConfigureAwait(false);
            try
            {
                // A later toggle supersedes work that has not reached the server yet.
                if (Volatile.Read(ref _isDisposing) != 0 ||
                    operationVersion != Volatile.Read(ref _dashboardOperationVersion))
                {
                    return;
                }

                var controls = _runtime.Controls;
                if (controls == null)
                {
                    return;
                }

                var applied = await Task.Run(
                    () => controls.SetWebDashboardEnabled(enabled)).ConfigureAwait(false);
                if (!applied && operationVersion == Volatile.Read(ref _dashboardOperationVersion))
                {
                    await Dispatcher.UIThread.InvokeAsync(() => RestoreWebDashboardEnabled(enabled));
                }
            }
            finally
            {
                _dashboardOperationGate.Release();
            }
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Failed to change the Web Dashboard enabled state.");
            if (operationVersion == Volatile.Read(ref _dashboardOperationVersion))
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    RestoreWebDashboardEnabled(enabled);
                    ErrorMessage = exception.Message;
                });
            }
        }
    }

    private void RestoreWebDashboardEnabled(bool requestedValue)
    {
        _applyingSnapshot = true;
        try
        {
            IsWebDashboardEnabled = !requestedValue;
        }
        finally
        {
            _applyingSnapshot = false;
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

    partial void OnIsNativeOverlayEnabledChanged(bool value)
    {
        if (!_applyingSnapshot && !_runtime.SetNativeOverlayEnabled(value))
        {
            RefreshSnapshot();
        }
    }

    partial void OnIsNativeOverlayFrameRateLockedChanged(bool value)
    {
        if (!_applyingSnapshot && !_runtime.SetNativeOverlayFrameRateLock(value))
        {
            RefreshSnapshot();
        }
    }

    partial void OnNativeOverlayFrameRateChanged(int value)
    {
        if (!_applyingSnapshot && !_runtime.SetNativeOverlayFrameRate(value))
        {
            RefreshSnapshot();
        }
    }

    partial void OnIsNativeOverlayTopMostChanged(bool value)
    {
        if (!_applyingSnapshot && !_runtime.SetNativeOverlayTopMost(value))
        {
            RefreshSnapshot();
        }
    }

    private void RefreshSnapshot()
    {
        var snapshot = _runtime.GetSnapshot();
        _applyingSnapshot = true;
        try
        {
            Status = LocalizeRuntimeStatus(snapshot.Status);
            UpdateStartupStatusHistory(Status);
            ApplyActiveGame(snapshot.ActiveGame, snapshot.ActiveGameCount);
            CurrentTrack = snapshot.CurrentTrack;
            GameCount = snapshot.GameCount;
            CoDriverCount = snapshot.CoDriverCount;
            OutputDeviceCount = snapshot.OutputDeviceCount;
            IsReady = snapshot.IsReady;
            ErrorMessage = snapshot.ErrorMessage ?? string.Empty;
            ApplyDashboards(snapshot.Dashboards);
            IsNativeOverlaySupported = snapshot.NativeOverlay.IsSupported;
            IsNativeOverlayEnabled = snapshot.NativeOverlay.IsEnabled;
            IsNativeOverlayInitializing = snapshot.NativeOverlay.IsInitializing;
            NativeOverlayFrameRate = snapshot.NativeOverlay.FrameRate;
            IsNativeOverlayFrameRateLocked = snapshot.NativeOverlay.IsFrameRateLocked;
            IsNativeOverlayTopMost = snapshot.NativeOverlay.IsTopMost;
            NativeOverlayStatus = LocalizeNativeOverlayStatus(snapshot.NativeOverlay.State);
            NativeOverlayStatusBrush = snapshot.NativeOverlay.State switch
            {
                "Active" or "Preview" => ConnectedBrush,
                "Ready" => new SolidColorBrush(Color.Parse("#B8860B")),
                _ => WaitingBrush
            };

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
                            ? AvaloniaLocalization.Format("audioStatus.defaultDevice", device.Name)
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
                    ? AvaloniaLocalization.Get("gameSession.stageActive")
                    : HasActiveGame
                        ? AvaloniaLocalization.Get("gameSession.waitingForStage")
                        : AvaloniaLocalization.Get("gameSession.waitingForGame");
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
            ActiveGameName = AvaloniaLocalization.Get("gameSession.waitingForGame");
            ActiveGameDescription = string.Empty;
            GameConnectionStatus = AvaloniaLocalization.Get("gameSession.automaticDetectionActive");
            GameDetectionSummary = AvaloniaLocalization.Get("gameSession.noSupportedGameDetected");
            GameStatusBrush = WaitingBrush;
            SetActiveGameImage(string.Empty);
            RebuildGameSettingsPanes(currentGame: null);
            return;
        }

        ActiveGameName = game.Name;
        ActiveGameDescription = game.Description;
        GameConnectionStatus = game.ConnectionState == DesktopGameConnectionState.Connected
            ? AvaloniaLocalization.Get("gameSession.connected")
            : AvaloniaLocalization.Get("gameSession.connectionFailed");
        GameDetectionSummary = activeGameCount > 1
            ? AvaloniaLocalization.Format("gameSession.gamesDetectedUsing", activeGameCount, game.Name)
            : AvaloniaLocalization.Get("gameSession.selectedAutomatically");
        GameStatusBrush = game.ConnectionState == DesktopGameConnectionState.Connected
            ? ConnectedBrush
            : FailedBrush;
        SetActiveGameImage(game.ImageFileName);
        RebuildGameSettingsPanes(currentGame: _runtime.CurrentGame);
    }

    private void RebuildGameSettingsPanes(IGame? currentGame)
    {
        if (ReferenceEquals(_gameSettingsGame, currentGame))
        {
            return;
        }

        _gameSettingsGame = currentGame;
        DisposeGameSettingsPanes();
        IsGameSettingsUnlocked = false;

        if (currentGame is null)
        {
            HasGameSettingsContent = false;
            return;
        }

        var panes = GameSettingsPaneFactory.CreatePanesForGame(currentGame);
        foreach (var pane in panes)
        {
            pane.RestartNeeded += OnGameSettingsRestartNeeded;
            CurrentGameSettings.Add(pane);
        }
        HasGameSettingsContent = CurrentGameSettings.Count > 0;
    }

    private void DisposeGameSettingsPanes()
    {
        foreach (var pane in CurrentGameSettings.OfType<GameSettingsPane>())
        {
            pane.RestartNeeded -= OnGameSettingsRestartNeeded;
            pane.Dispose();
        }
        CurrentGameSettings.Clear();
    }

    private void OnGameSettingsRestartNeeded() =>
        Status = AvaloniaLocalization.Get("settings.restartNeeded");

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
                definition.PackagePath,
                AvaloniaLocalization.Get(definition.Name),
                AvaloniaLocalization.Get(definition.Description),
                definition.Author,
                definition.Version,
                definition.GetOutputTarget(),
                LoadBitmap(previewPath),
                SaveDashboardOutputTarget));
            var choice = Dashboards[^1];
            foreach (var setting in DashboardPackageSettings.Load(definition.PackagePath)
                         .Where(setting => !string.Equals(setting.Key, "dashboards.settings.enabled", StringComparison.Ordinal)))
            {
                var settingChoice = new DashboardSettingChoice(
                    setting,
                    item => SaveDashboardSetting(choice, item));
                choice.Settings.Add(settingChoice);
                settingChoice.CompleteInitialization();
            }
        }

    }

    private void SaveDashboardOutputTarget(DashboardChoice dashboard, DashboardOutputTarget target)
    {
        try
        {
            Config.Instance.SetDashboardOutputTarget(dashboard.Id, target);
            Config.Instance.SaveUserConfig();
            ScheduleDashboardReload();
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    private void SaveDashboardSetting(DashboardChoice dashboard, DashboardSettingChoice setting)
    {
        try
        {
            DashboardPackageSettings.SaveValue(dashboard.PackagePath, setting.Key, setting.GetValue());
            ScheduleDashboardReload();
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    private void ScheduleDashboardReload()
    {
        Interlocked.Increment(ref _dashboardReloadVersion);
        _dashboardReloadTimer.Stop();
        _dashboardReloadTimer.Start();
    }

    private async Task ReloadDashboardsAsync(long reloadVersion)
    {
        try
        {
            await _dashboardOperationGate.WaitAsync().ConfigureAwait(false);
            try
            {
                // Slider changes coalesce into one reload using the most recent value.
                if (Volatile.Read(ref _isDisposing) != 0 ||
                    reloadVersion != Volatile.Read(ref _dashboardReloadVersion))
                {
                    return;
                }

                await Task.Run(_runtime.ReloadDashboards).ConfigureAwait(false);
            }
            finally
            {
                _dashboardOperationGate.Release();
            }
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Failed to reload Web Dashboard packages after a settings change.");
            if (reloadVersion == Volatile.Read(ref _dashboardReloadVersion) &&
                Volatile.Read(ref _isDisposing) == 0)
            {
                await Dispatcher.UIThread.InvokeAsync(() => ErrorMessage = exception.Message);
            }
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
    private void OpenCoDriverFolder()
    {
        if (!string.IsNullOrWhiteSpace(SelectedCoDriver?.Path))
        {
            FolderOpenRequested?.Invoke(SelectedCoDriver.Path);
        }
    }

    private bool CanOpenCoDriverFolder() =>
        !string.IsNullOrWhiteSpace(SelectedCoDriver?.Path) &&
        Directory.Exists(SelectedCoDriver.Path);

    [RelayCommand]
    private void SelectCoDriver(CoDriverChoice? coDriver)
    {
        if (coDriver != null)
        {
            SelectedCoDriver = coDriver;
        }
    }

    [RelayCommand]
    private async Task RefreshVoicePackagesAsync()
    {
        if (IsVoicePackageCatalogLoading)
            return;

        IsVoicePackageCatalogLoading = true;
        try
        {
            var packages = await _runtime.GetVoicePackagesAsync();
            VoicePackages.Clear();
            foreach (var package in packages.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                VoicePackages.Add(new VoicePackageChoice(package));
            }
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
        finally
        {
            IsVoicePackageCatalogLoading = false;
        }
    }

    [RelayCommand]
    private async Task UpdateVoicePackageAsync(VoicePackageChoice? package)
    {
        if (package == null || string.IsNullOrWhiteSpace(package.DownloadUrl))
            return;

        package.IsDownloading = true;
        try
        {
            var progress = new Progress<CoDriverPackageUpdateProgress>(update =>
            {
                package.IsInstalling = update.Phase == CoDriverPackageUpdatePhase.Installing;
                package.IsDownloading = !package.IsInstalling;
                package.DownloadProgress = update.Download?.Fraction ?? package.DownloadProgress;
            });
            await _runtime.InstallVoicePackageAsync(new VoicePackageSnapshot(
                package.Id, package.Name, package.Description, package.Gender, package.Language,
                package.Homepage, package.Version, package.OriginalVersion, package.PackagePath,
                package.DownloadUrl, package.NeedsUpdate, package.NeedsDownload, package.IsInstalled,
                package.IntegritySimple, package.IntegrityNormal, package.IntegrityComplex), progress);
            await RefreshVoicePackagesAsync();
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
        finally
        {
            package.IsDownloading = false;
            package.IsInstalling = false;
        }
    }

    internal Task<CoDriverPackageImportPlan> InspectVoicePackageImportAsync(string archivePath) =>
        _runtime.InspectVoicePackageImportAsync(archivePath);

    internal async Task ImportVoicePackageAsync(string archivePath, string? existingPackagePath = null)
    {
        await _runtime.ImportVoicePackageAsync(archivePath, existingPackagePath);
        await RefreshVoicePackagesAsync();
    }

    internal Task ExportVoicePackageAsync(VoicePackageChoice package, string archivePath) =>
        _runtime.ExportVoicePackageAsync(package.Id, archivePath);

    [RelayCommand]
    private async Task OpenVoicePackageDetailsAsync(VoicePackageChoice? package)
    {
        if (package == null || !package.IsInstalled)
            return;

        var loadVersion = Interlocked.Increment(ref _voicePackageDetailsLoadVersion);
        SelectedVoicePackage = package;
        IsVoicePackageDetailsOpen = true;
        IsVoicePackageDetailsLoading = true;
        VoicePackageDetails.Clear();
        try
        {
            // Let the detail view and loading indicator render before package work begins.
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);
            var details = await _runtime.GetVoicePackageDetailsAsync(package.Id);
            if (loadVersion != Volatile.Read(ref _voicePackageDetailsLoadVersion))
                return;

            await AddVoicePackageDetailsInBatchesAsync(details, loadVersion);
        }
        catch (Exception exception)
        {
            if (loadVersion == Volatile.Read(ref _voicePackageDetailsLoadVersion))
                ErrorMessage = exception.Message;
        }
        finally
        {
            if (loadVersion == Volatile.Read(ref _voicePackageDetailsLoadVersion))
                IsVoicePackageDetailsLoading = false;
        }
    }

    private async Task AddVoicePackageDetailsInBatchesAsync(
        IReadOnlyList<VoicePackageTokenSnapshot> details,
        int loadVersion)
    {
        const int batchSize = 16;
        for (var offset = 0; offset < details.Count; offset += batchSize)
        {
            if (loadVersion != Volatile.Read(ref _voicePackageDetailsLoadVersion))
                return;

            var end = Math.Min(offset + batchSize, details.Count);
            for (var index = offset; index < end; index++)
                VoicePackageDetails.Add(new VoicePackageTokenChoice(details[index]));

            // Queue the next batch behind input and layout work.
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);
        }
    }

    [RelayCommand]
    private void CloseVoicePackageDetails()
    {
        Interlocked.Increment(ref _voicePackageDetailsLoadVersion);
        IsVoicePackageDetailsOpen = false;
        IsVoicePackageDetailsLoading = false;
        SelectedVoicePackage = null;
        VoicePackageDetails.Clear();
    }

    [RelayCommand]
    private void OpenVoiceSettings() => IsVoiceSettingsOpen = true;

    [RelayCommand]
    private void CloseVoiceSettings() => IsVoiceSettingsOpen = false;

    [RelayCommand]
    private void PlayVoicePackageFile(VoicePackageAudioFileChoice? file)
    {
        if (file == null)
            return;

        try
        {
            _runtime.PlayVoicePackageFile(file.Path);
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

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

    [RelayCommand]
    private void OpenExternalUri(string? uri) => OpenExternal(uri);

    [RelayCommand]
    private async Task UnlockGameSettings()
    {
        if (IsGameSettingsUnlocked)
            return;

        var dialog = FaContentDialogFactory.Create(new FAContentDialog
        {
            Title = AvaloniaLocalization.Get("gameSettingsUnlockDialogTitle"),
            Content = AvaloniaLocalization.Get("gameSettingsUnlockDialogMessage"),
            PrimaryButtonText = AvaloniaLocalization.Get("gameSettingsUnlockConfirm"),
            CloseButtonText = AvaloniaLocalization.Get("dialog.common.btn_cancel"),
            DefaultButton = FAContentDialogButton.Primary
        });
        var result = await dialog.ShowAsync();
        if (result == FAContentDialogResult.Primary)
        {
            IsGameSettingsUnlocked = true;
        }
    }

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

    internal void SetActiveNavigationPage(string pageKey) => ActiveNavigationPage = pageKey;

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
            OnPropertyChanged(nameof(VoiceBreadcrumbItems));
            OnPropertyChanged(nameof(NavigationBreadcrumbItems));
            OnPropertyChanged(nameof(HasNavigationBreadcrumb));
            RefreshSnapshot();

            if (IsVoicePackageDetailsOpen && SelectedVoicePackage != null)
            {
                _ = OpenVoicePackageDetailsAsync(SelectedVoicePackage);
            }

            IsLanguageSwitchInProgress = false;
        });
    }

    private void OnCultureChanging(object? sender, EventArgs eventArgs)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            IsLanguageSwitchInProgress = true;
            return;
        }

        Dispatcher.UIThread.Post(() => IsLanguageSwitchInProgress = true);
    }

    private static string LocalizeDashboardStatus(string state) => state switch
    {
        "Running" => AvaloniaLocalization.Get("dashboardStatus.running"),
        "PortUnavailable" => AvaloniaLocalization.Get("dashboardStatus.portUnavailable"),
        "Error" => AvaloniaLocalization.Get("dashboardStatus.error"),
        _ => AvaloniaLocalization.Get("dashboardStatus.stopped")
    };

    private static string LocalizeNativeOverlayStatus(string state) => state switch
    {
        "Disabled" => AvaloniaLocalization.Get("settings.tooltip.overlayHudHomeStatusDisabled"),
        "NoDashboard" => AvaloniaLocalization.Get("settings.tooltip.overlayHudHomeStatusNoDashboard"),
        "Ready" => AvaloniaLocalization.Get("settings.tooltip.overlayHudHomeStatusReady"),
        "Active" => AvaloniaLocalization.Get("settings.tooltip.overlayHudHomeStatusActive"),
        "Preview" => AvaloniaLocalization.Get("settings.tooltip.overlayHudHomeStatusPreview"),
        _ => AvaloniaLocalization.Get("settings.tooltip.overlayHudHomeStatusUnavailable")
    };

    private static string LocalizeRuntimeStatus(string status)
    {
        var exactKey = status switch
        {
            "Initializing runtime..." => "runtime.initializingRuntime",
            "Loading pacenote definitions..." => "runtime.loadingPacenoteDefinitions",
            "Pacenote definitions loaded." => "runtime.pacenoteDefinitionsLoaded",
            "Loading profile manager..." => "runtime.loadingProfileManager",
            "Profile manager loaded." => "runtime.profileManagerLoaded",
            "Loading games..." => "runtime.loadingGames",
            "Loading profiles..." => "runtime.loadingProfiles",
            "Loading codrivers..." => "runtime.loadingCodrivers",
            "Loading output devices..." => "runtime.loadingOutputDevices",
            "Ready" => "runtime.ready",
            "Initialization failed" => "runtime.initializationFailed",
            _ => string.Empty
        };
        if (!string.IsNullOrEmpty(exactKey))
        {
            return AvaloniaLocalization.Get(exactKey);
        }

        foreach (var candidate in new[]
                 {
                     (Suffix: " games loaded.", Key: "runtime.gamesLoaded"),
                     (Suffix: " profiles loaded.", Key: "runtime.profilesLoaded"),
                     (Suffix: " codrivers loaded.", Key: "runtime.codriversLoaded"),
                     (Suffix: " output devices loaded.", Key: "runtime.outputDevicesLoaded")
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

    private void UpdateStartupStatusHistory(string status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return;
        }

        var latest = StartupStatusHistory.LastOrDefault();
        if (latest != null && string.Equals(latest.Text, status, StringComparison.Ordinal))
        {
            latest.IsCurrent = true;
            return;
        }

        foreach (var entry in StartupStatusHistory)
        {
            entry.IsCurrent = false;
        }

        StartupStatusHistory.Add(new StartupStatusEntry(status, isCurrent: true));
        while (StartupStatusHistory.Count > 5)
        {
            StartupStatusHistory.RemoveAt(0);
        }
    }

    private static Bitmap? LoadBitmap(string path) =>
        File.Exists(path) ? new Bitmap(path) : null;
}
