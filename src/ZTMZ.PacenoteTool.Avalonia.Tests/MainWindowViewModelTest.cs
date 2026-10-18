using Avalonia.Headless.XUnit;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Xunit;
using ZTMZ.PacenoteTool.Avalonia.Services;
using ZTMZ.PacenoteTool.Avalonia.ViewModels;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.Core;

namespace ZTMZ.PacenoteTool.Avalonia.Tests;

public class MainWindowViewModelTest
{
    [AvaloniaFact]
    public async Task SettingsSearchNavigatesToVoiceSettingsAndReplayExportPageLoads()
    {
        var root = RepositoryRoot();
        I18NLoader.Instance.Initialize([Path.Combine(root, "src", "ZTMZ.PacenoteTool.I18N", "lang")]);
        I18NLoader.Instance.SetCulture("zh-cn");
        AvaloniaLocalization.ApplyCurrentCulture(global::Avalonia.Application.Current!);
        global::Avalonia.Application.Current!.Styles.Add(new FluentAvalonia.Styling.FluentAvaloniaTheme());
        var runtime = new FakeDesktopRuntime(CreateReadySnapshot() with { VoiceUpdateCount = 2 });
        await using var vm = new MainWindowViewModel(runtime);
        await vm.InitializeAsync();
        var window = new ZTMZ.PacenoteTool.Avalonia.Views.MainWindow(vm);
        try
        {
            window.Show();
            Assert.True(window.FindControl<Border>("VoiceUpdateBadge")!.IsVisible);
            runtime.SetSnapshot(CreateReadySnapshot() with { VoiceUpdateCount = 0 });
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.False(window.FindControl<Border>("VoiceUpdateBadge")!.IsVisible);
            var box = window.FindControl<global::Avalonia.Controls.TextBox>("SettingsSearchBox")!;
            var titleBar = window.FindControl<Grid>("CustomTitleBar")!;
            var titleLabel = window.FindControl<TextBlock>("TitleBarLabel")!;
            Assert.Equal("ZTMZ Next Generation Pacenote Tool", titleLabel.Text);
            Assert.Equal(vm.WindowTitle, ToolTip.GetTip(titleLabel));
            Assert.Equal(vm.WindowTitle, window.Title);
            Assert.False(Views.MainWindow.IsInteractiveTitleBarElement(titleLabel));
            Assert.Contains(titleBar, global::Avalonia.VisualTree.VisualExtensions.GetVisualAncestors(box));
            Assert.True(Views.MainWindow.IsInteractiveTitleBarElement(box));
            Assert.False(Views.MainWindow.IsInteractiveTitleBarElement(titleBar));
            foreach (var width in new[] { 1024d, 1600d })
            {
                window.Width = width;
                global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                var origin = global::Avalonia.VisualExtensions.TranslatePoint(box, default, titleBar)!.Value;
                var titleOrigin = global::Avalonia.VisualExtensions.TranslatePoint(titleLabel, default, titleBar)!.Value;
                Assert.True(titleOrigin.X + titleLabel.Bounds.Width <= origin.X);
                Assert.InRange(Math.Abs(origin.X + box.Bounds.Width / 2 - titleBar.Bounds.Width / 2), 0, 1);
                Assert.InRange(origin.Y + box.Bounds.Height, 0, 48);
                var actions = window.FindControl<StackPanel>("TitleBarActions")!;
                var actionsOrigin = global::Avalonia.VisualExtensions.TranslatePoint(actions, default, titleBar)!.Value;
                Assert.True(origin.X + box.Bounds.Width <= actionsOrigin.X);
            }
            var popup = window.FindControl<global::Avalonia.Controls.Primitives.Popup>("SettingsSearchPopup")!;
            box.Focus();
            box.Text = "no-setting-with-this-name";
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.True(popup.IsOpen);
            Assert.True(window.FindControl<TextBlock>("SettingsSearchEmpty")!.IsVisible);
            box.RaiseEvent(new global::Avalonia.Input.KeyEventArgs
            {
                RoutedEvent = global::Avalonia.Input.InputElement.KeyDownEvent,
                Key = global::Avalonia.Input.Key.Escape
            });
            Assert.False(popup.IsOpen);
            box.Text = "collision";
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.True(popup.IsOpen);
            var results = window.FindControl<global::Avalonia.Controls.ListBox>("SettingsSearchResults")!;
            var item = Assert.IsType<SettingSearchResult>(results.Items[0]);
            Assert.Equal("codriver", item.Entry.PageId);
            results.SelectedItem = item;
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal("codriver", vm.ActiveNavigationPage);
            Assert.True(vm.IsVoiceSettingsOpen);
            Assert.False(popup.IsOpen);
            Assert.Equal(string.Empty, box.Text);
            Assert.Contains(window.GetLogicalDescendants().OfType<Border>(),
                border => Equals(border.BorderBrush, global::Avalonia.Media.Brushes.Goldenrod));
            var replay = new ZTMZ.PacenoteTool.Avalonia.Views.ReplayExportView
            {
                DataContext = new ReplayExportViewModel(runtime)
            };
            Assert.NotNull(replay.Content);
        }
        finally { window.RequestDataFolderMigrationShutdown(); }
    }

    [Fact]
    public void WindowsAvaloniaStagesLoadableRbrProviderAndMappingDatabases()
    {
        var configuration = typeof(MainWindowViewModel).Assembly.GetCustomAttributes(false)
            .OfType<System.Reflection.AssemblyConfigurationAttribute>().Single().Configuration;
        var output = Path.Combine(RepositoryRoot(), "src", "ZTMZ.PacenoteTool.Avalonia", "bin", configuration, "net10.0-windows");
        var provider = Path.Combine(output, "games", "ZTMZ.PacenoteTool.RBR.dll");
        Assert.True(File.Exists(provider));
        DesktopRuntime.ProvisionRbrMappings(Path.Combine(output, "resources"), AppLevelVariables.Instance);
        var providerType = System.Reflection.Assembly.LoadFrom(provider).GetType("ZTMZ.PacenoteTool.RBR.RBR", throwOnError: true)!;
        Assert.IsAssignableFrom<IGame>(Activator.CreateInstance(providerType));
        var games = GamePluginLoader.Load([provider], NLog.LogManager.GetCurrentClassLogger());
        Assert.Contains(games, game => game.Name.Contains("Richard Burns Rally"));
        Assert.True(File.Exists(Path.Combine(output, "resources", "games", "default.zdb")));
        Assert.True(File.Exists(Path.Combine(output, "resources", "games", "jannemod_v3.zdb")));
        Assert.True(File.Exists(Path.Combine(output, "games", "lang", "zh-cn.rbr.json")));
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "ZTMZ.PacenoteTool.Avalonia"))) return directory.FullName;
        throw new DirectoryNotFoundException("Repository root not found");
    }

    [AvaloniaFact]
    public async Task GameWarningsAndInstalledUpdateCountReachTheViewModel()
    {
        var runtime = new FakeDesktopRuntime(CreateReadySnapshot() with
        {
            VoiceUpdateCount = 2, GameMessage = "This stage uses default pacenotes", GameMessageLevel = 0
        });
        await using var vm = new MainWindowViewModel(runtime);
        await vm.InitializeAsync();
        Assert.True(vm.HasVoiceUpdates);
        Assert.Equal(2, vm.VoiceUpdateCount);
        Assert.True(vm.HasGameMessage);
        Assert.Equal("This stage uses default pacenotes", vm.GameMessage);
        Assert.Equal(FluentAvalonia.UI.Controls.FAInfoBarSeverity.Informational, vm.GameMessageSeverity);
        Assert.False(vm.HasError);
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StartupOptionControlsOnlineCheckButAlwaysLoadsLocalPackages(bool enabled)
    {
        var old = Config.Instance.CheckVoicePackageUpdatesOnStartup;
        Config.Instance.CheckVoicePackageUpdatesOnStartup = enabled;
        try
        {
            var runtime = new FakeDesktopRuntime(CreateReadySnapshot());
            await using var vm = new MainWindowViewModel(runtime);
            await vm.InitializeAsync();
            Assert.Equal(enabled ? new[] { false, true } : new[] { false }, runtime.CatalogRequests);
            await vm.RefreshVoicePackagesCommand.ExecuteAsync(null);
            Assert.True(runtime.CatalogRequests.Last());
        }
        finally { Config.Instance.CheckVoicePackageUpdatesOnStartup = old; }
    }

    [AvaloniaFact]
    public async Task OfflineCatalogLeavesInstalledPackagesUsableWithoutInitializationError()
    {
        var old = Config.Instance.CheckVoicePackageUpdatesOnStartup;
        Config.Instance.CheckVoicePackageUpdatesOnStartup = true;
        try
        {
            var runtime = new FakeDesktopRuntime(CreateReadySnapshot()) { FailOnline = true };
            await using var vm = new MainWindowViewModel(runtime);
            await vm.InitializeAsync();
            Assert.Single(vm.VoicePackages);
            Assert.False(vm.HasError);
            Assert.NotEmpty(vm.VoiceCatalogError);
            Assert.False(vm.IsVoicePackageCatalogLoading);
        }
        finally { Config.Instance.CheckVoicePackageUpdatesOnStartup = old; }
    }

    [AvaloniaFact]
    public async Task InitializeAppliesSnapshotWithoutStartingRealDesktopServices()
    {
        var runtime = new FakeDesktopRuntime(CreateReadySnapshot());
        await using var viewModel = new MainWindowViewModel(runtime);

        await viewModel.InitializeAsync();

        Assert.True(runtime.InitializeCalled);
        Assert.True(viewModel.IsReady);
        Assert.True(viewModel.HasActiveGame);
        Assert.Equal("Test Rally", viewModel.ActiveGameName);
        Assert.Equal("Test Stage", viewModel.CurrentTrack);
        Assert.Equal(3, viewModel.GameCount);
        Assert.Equal(4, viewModel.CoDriverCount);
        Assert.Equal(2, viewModel.OutputDeviceCount);
    }

    [AvaloniaFact]
    public async Task UserControlChangesAreForwardedThroughRuntimeContract()
    {
        var runtime = new FakeDesktopRuntime(CreateReadySnapshot());
        await using var viewModel = new MainWindowViewModel(runtime);
        await viewModel.InitializeAsync();

        viewModel.IsMuted = true;
        viewModel.PlaybackGainDb = -6;

        Assert.Equal(true, runtime.LastMuted);
        Assert.Equal(-6, runtime.LastPlaybackGainDb);
        viewModel.ResetPlaybackGainCommand.Execute(null);
        Assert.Equal(0, runtime.LastPlaybackGainDb);
        Assert.True(viewModel.IsMuted);
    }

    [AvaloniaFact]
    public async Task GainSliderPreservesMigratedLevelUntilTheUserChangesIt()
    {
        var gain = PlaybackGain.FromLegacy(500);
        var controls = new SharedRuntimeControlsSnapshot([], null, [], null, false, true,
            gain, 0, 1, 0, 0, false, new RuntimeDashboardStatus("Stopped", 0, "", "", false));
        var runtime = new FakeDesktopRuntime(CreateReadySnapshot() with { Controls = controls });
        await using var vm = new MainWindowViewModel(runtime);
        await vm.InitializeAsync();
        var view = new ZTMZ.PacenoteTool.Avalonia.Views.HomeView { DataContext = vm };
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var slider = view.FindControl<Slider>("PlaybackGainSlider")!;
        Assert.Equal(-60, slider.Minimum);
        Assert.Equal(24, slider.Maximum);
        Assert.Equal(gain, slider.Value);
        Assert.Equal(gain, vm.PlaybackGainDb);
        Assert.Null(runtime.LastPlaybackGainDb);
        slider.Value = -6;
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(-6, runtime.LastPlaybackGainDb);
        vm.ResetPlaybackGainCommand.Execute(null);
        Assert.Equal(0, runtime.LastPlaybackGainDb);
        Assert.True(vm.IsMuted);
    }

    [AvaloniaTheory]
    [InlineData("overlayBackend.componentMissing")]
    [InlineData("overlayBackend.componentBlocked")]
    [InlineData("overlayBackend.componentInvalid")]
    [InlineData("overlayBackend.loadFailed")]
    [InlineData("overlayBackend.gameAccessDenied")]
    public async Task OverlayFailureSnapshotShowsActionAndPathWithExpandableDiagnosticsAndClearsAfterRecovery(string statusKey)
    {
        I18NLoader.Instance.Initialize([Path.Combine(RepositoryRoot(), "src", "ZTMZ.PacenoteTool.I18N", "lang")]);
        I18NLoader.Instance.SetCulture("en-us");
        AvaloniaLocalization.ApplyCurrentCulture(global::Avalonia.Application.Current!);
        var ready = CreateReadySnapshot() with
        {
            NativeOverlay = new DesktopNativeOverlaySnapshot(true, true, false, 60, false, false,
                "Ready", false, "overlayBackend.windowActive")
        };
        var runtime = new FakeDesktopRuntime(ready);
        await using var vm = new MainWindowViewModel(runtime);
        await vm.InitializeAsync();
        var dashboard = new Views.DashboardView { DataContext = vm };
        var home = new Views.HomeView { DataContext = vm };
        var application = global::Avalonia.Application.Current!;
        var theme = application.Styles.OfType<FluentAvalonia.Styling.FluentAvaloniaTheme>().Any()
            ? null : new FluentAvalonia.Styling.FluentAvaloniaTheme();
        if (theme != null) application.Styles.Add(theme);
        var dashboardWindow = new Window { Width = 1000, Height = 900, Content = dashboard };
        var homeWindow = new Window { Width = 1200, Height = 900, Content = home };
        try
        {
            // Headless windows supply templates and inherited DataContext just as the desktop does.
            dashboardWindow.Show();
            homeWindow.Show();
            var failure = new OverlayFailureInfo(statusKey, @"C:\ZTMZ\components\overlay.dll",
                "System.IO.IOException: Test loader failure\n   at FakeOverlay.Load()");

            runtime.SetSnapshot(ready with
            {
                NativeOverlay = ready.NativeOverlay with { State = "Unavailable", BackendStatusKey = statusKey, Failure = failure }
            });
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.True(vm.HasOverlayFailure);
            Assert.True(dashboard.FindControl<StackPanel>("OverlayFailurePanel")!.IsVisible);
            var summary = dashboard.FindControl<TextBlock>("OverlayBackendStatusText")!.Text!;
            Assert.Equal(I18NLoader.Instance[statusKey], summary);
            if (statusKey == "overlayBackend.gameAccessDenied")
            {
                Assert.Contains("different administrator privileges", summary);
                Assert.Contains("security-software restrictions", summary);
                Assert.Contains("Pacenote playback remains available", summary);
                Assert.DoesNotContain("official package", summary);
            }
            else
            {
                Assert.Contains("official package", summary);
                Assert.Contains("Separate overlay window", summary);
            }
            if (statusKey == "overlayBackend.loadFailed") Assert.Contains("Restart the game", summary);
            Assert.Equal(failure.ComponentPath, dashboard.FindControl<TextBlock>("OverlayFailurePathText")!.Text);
            var expander = dashboard.FindControl<Expander>("OverlayDiagnosticsExpander")!;
            Assert.False(expander.IsExpanded);
            expander.IsExpanded = true;
            dashboardWindow.UpdateLayout();
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal(failure.ToDisplayText(), dashboard.FindControl<TextBox>("OverlayFailureDiagnosticsText")!.Text);
            Assert.DoesNotContain(failure.Details, summary);
            Assert.Contains(failure.ComponentPath!, vm.NativeOverlayStatusTooltip);
            Assert.Contains(summary, vm.NativeOverlayStatusTooltip);
            Assert.DoesNotContain(failure.Details, vm.NativeOverlayStatusTooltip);
            var homeStatus = home.FindControl<StackPanel>("NativeOverlayHomeStatus")!;
            ToolTip.SetIsOpen(homeStatus, true);
            homeWindow.UpdateLayout();
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var tooltip = Assert.IsType<TextBlock>(ToolTip.GetTip(homeStatus));
            Assert.Equal(vm.NativeOverlayStatusTooltip, tooltip.Text);
            Assert.NotNull(dashboard.FindControl<Button>("CopyOverlayDiagnosticsButton"));

            runtime.SetSnapshot(ready);
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.False(vm.HasOverlayFailure);
            Assert.False(dashboard.FindControl<StackPanel>("OverlayFailurePanel")!.IsVisible);
            Assert.Equal(string.Empty, dashboard.FindControl<TextBlock>("OverlayFailurePathText")!.Text);
            Assert.Equal(string.Empty, dashboard.FindControl<TextBox>("OverlayFailureDiagnosticsText")!.Text);
            Assert.Equal(I18NLoader.Instance["overlayBackend.windowActive"],
                dashboard.FindControl<TextBlock>("OverlayBackendStatusText")!.Text);
            Assert.DoesNotContain(failure.ComponentPath!, tooltip.Text!);
            Assert.Equal(vm.NativeOverlayStatusTooltip, tooltip.Text);
        }
        finally
        {
            ToolTip.SetIsOpen(home.FindControl<StackPanel>("NativeOverlayHomeStatus")!, false);
            homeWindow.Close();
            dashboardWindow.Close();
            if (theme != null) application.Styles.Remove(theme);
        }
    }

    private static DesktopRuntimeSnapshot CreateReadySnapshot() => new(
        "Ready",
        new DesktopGameSnapshot(
            "Test Rally",
            "Test integration",
            string.Empty,
            DesktopGameConnectionState.Connected),
        "Test Stage",
        1,
        3,
        4,
        2,
        [],
        null,
        new DesktopNativeOverlaySnapshot(false, false, false, 60, false, false, "Unavailable", false),
        true,
        null);

    private sealed class FakeDesktopRuntime(DesktopRuntimeSnapshot snapshot) : IDesktopRuntime
    {
        public void SetSnapshot(DesktopRuntimeSnapshot value)
        {
            snapshot = value;
            SnapshotChanged?.Invoke(this, EventArgs.Empty);
        }
        public List<bool> CatalogRequests { get; } = [];
        public bool FailOnline { get; init; }
        public event EventHandler? SnapshotChanged;
        public IGame? CurrentGame => null;
        public IReadOnlyList<IGame> Games => [];
        public bool InitializeCalled { get; private set; }
        public bool? LastMuted { get; private set; }
        public double? LastPlaybackGainDb { get; private set; }
        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            InitializeCalled = true;
            return Task.CompletedTask;
        }
        public DesktopRuntimeSnapshot GetSnapshot() => snapshot;
        public void ReloadDashboards() => SnapshotChanged?.Invoke(this, EventArgs.Empty);
        public bool SetNativeOverlayEnabled(bool enabled) => true;
        public bool SetNativeOverlayFrameRateLock(bool enabled) => true;
        public bool SetNativeOverlayFrameRate(int frameRate) => true;
        public bool SetNativeOverlayTopMost(bool enabled) => true;
        public bool SetMuted(bool muted) { LastMuted = muted; return true; }
        public bool SetWebDashboardEnabled(bool enabled) => true;
        public bool ConfigureWebDashboard(int port, int targetFrameRate) => true;
        public bool TrySelectCodriver(string identity) => true;
        public bool TrySelectOutputDevice(string id) => true;
        public bool SetPlaybackGainDb(double volume) { LastPlaybackGainDb = volume; return true; }
        public bool SetPlaybackAdjustSeconds(double seconds) => true;
        public bool SetPlaybackSpeed(double speed) => true;
        public bool SetSilenceRemovalLevel(int level) => true;
        public bool SetIntercomEffect(int effect) => true;
        public bool TryPlayExample() => true;
        public Task<IReadOnlyList<VoicePackageSnapshot>> GetVoicePackagesAsync(
            CancellationToken cancellationToken = default, bool checkOnline = true)
        {
            CatalogRequests.Add(checkOnline);
            if (checkOnline && FailOnline) throw new HttpRequestException("offline");
            return Task.FromResult<IReadOnlyList<VoicePackageSnapshot>>([
                new("local", "Local", "", "", "", "", "1.0", "1.0", "local", "", false, false, true, 1, 1, 1)]);
        }
        public Task InstallVoicePackageAsync(
            VoicePackageSnapshot package,
            IProgress<CoDriverPackageUpdateProgress>? progress = null,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ImportVoicePackageAsync(string archivePath, string? existingPackagePath = null) =>
            Task.CompletedTask;
        public Task<CoDriverPackageImportPlan> InspectVoicePackageImportAsync(string archivePath) =>
            throw new NotSupportedException();
        public Task ExportVoicePackageAsync(string id, string archivePath) => Task.CompletedTask;
        public Task<IReadOnlyList<VoicePackageTokenSnapshot>> GetVoicePackageDetailsAsync(string id) =>
            Task.FromResult<IReadOnlyList<VoicePackageTokenSnapshot>>([]);
        public void PlayVoicePackageFile(string path) { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
