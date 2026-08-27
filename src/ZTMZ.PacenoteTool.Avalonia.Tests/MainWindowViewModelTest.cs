using Avalonia.Headless.XUnit;
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
        viewModel.PlaybackVolume = 42;

        Assert.Equal(true, runtime.LastMuted);
        Assert.Equal(42, runtime.LastPlaybackVolume);
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
        public event EventHandler? SnapshotChanged;
        public IGame? CurrentGame => null;
        public IReadOnlyList<IGame> Games => [];
        public bool InitializeCalled { get; private set; }
        public bool? LastMuted { get; private set; }
        public double? LastPlaybackVolume { get; private set; }
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
        public bool SetPlaybackVolume(double volume) { LastPlaybackVolume = volume; return true; }
        public bool SetPlaybackAdjustSeconds(double seconds) => true;
        public bool SetPlaybackSpeed(double speed) => true;
        public bool SetSilenceRemovalLevel(int level) => true;
        public bool SetIntercomEffect(int effect) => true;
        public bool TryPlayExample() => true;
        public Task<IReadOnlyList<VoicePackageSnapshot>> GetVoicePackagesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<VoicePackageSnapshot>>([]);
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
