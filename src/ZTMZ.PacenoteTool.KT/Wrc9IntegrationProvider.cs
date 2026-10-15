using System.Diagnostics;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.KT;

public sealed class Wrc9IntegrationProvider : IGameIntegrationProvider
{
    private readonly SemaphoreSlim injectionLock = new(1, 1);
    public Type ConfigurationType => typeof(Wrc9Config);
    public IReadOnlyList<GameSettingField> Fields { get; } = [new("controlsEnabled", "game.wrc9.enable", GameSettingFieldKind.Boolean,
        Confirmation: new("game.wrc9.title", "game.wrc9.warning", "game.wrc9.inject", WhenEnablingOnly: true))];
    public IReadOnlyList<string> InformationKeys => ["game.wrc9.limitations"];
    public IReadOnlyList<GameIntegrationAction> Actions { get; } = [new("inject", "game.wrc9.inject",
        Confirmation: new("game.wrc9.title", "game.wrc9.warning", "game.wrc9.inject"),
        SuccessTitleKey: "game.wrc9.title", FailureTitleKey: "game.wrc9.title", ShowSuccessMessage: true)];
    public TimeSpan StatusRefreshInterval => TimeSpan.FromSeconds(1);
    public object GetFieldValue(IGameConfig configuration, string fieldId) => fieldId == "controlsEnabled"
        ? ((Wrc9Config)configuration).ControlsEnabled : throw new ArgumentOutOfRangeException(nameof(fieldId));
    public void SetFieldValue(IGameConfig configuration, string fieldId, object? value, IGame game)
    {
        if (fieldId != "controlsEnabled") throw new ArgumentOutOfRangeException(nameof(fieldId));
        var config = (Wrc9Config)configuration;
        config.ControlsEnabled = Convert.ToBoolean(value);
        if (config.ControlsEnabled) GameRuntimeNotifications.NotifyLocalBridgeControlsEnabled(game);
    }
    public GameIntegrationStatus GetStatus(IGameConfig configuration, IGame game)
    {
        var info = ((ILocalGameBridgeControl)game).GetLocalBridgeDiagnostics();
        return new(!info.IsEnabled ? "game.wrc9.disabled" : info.IsInjected ? "game.wrc9.connected" : "game.wrc9.disconnected",
            EnabledActionIds: info.IsEnabled && info.IsAvailable && game.IsRunning && !info.IsInjected ? new HashSet<string> { "inject" } : new HashSet<string>());
    }
    public async Task<GameIntegrationActionResult> ExecuteActionAsync(string actionId, IGameConfig configuration, IGame game, CancellationToken cancellationToken)
    {
        if (actionId != "inject") throw new ArgumentOutOfRangeException(nameof(actionId));
        if (!OperatingSystem.IsWindows()) return new(false, "game.wrc9.windowsOnly", MessageIsResourceKey: true);
        await injectionLock.WaitAsync(cancellationToken);
        try
        {
            if (!((Wrc9Config)configuration).ControlsEnabled) return new(false, "game.wrc9.disabled", MessageIsResourceKey: true);
            var reader = (Wrc9GameDataReader)game.GameDataReader;
            if (reader.BridgeConnected) return new(true, "game.wrc9.connected", MessageIsResourceKey: true);
            using var process = Process.GetProcessesByName("WRC9").FirstOrDefault();
            if (process is null) return new(false, "game.wrc9.startGame", MessageIsResourceKey: true);
            _ = Wrc9MemorySession.Verify(process); // Gate fixed offsets before any injection.
            await reader.Listening.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            using var injector = new Process { StartInfo = new(Path.Combine(Wrc9.BridgeDirectory, "Wrc9BridgeInject.exe"))
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } };
            injector.StartInfo.ArgumentList.Add(process.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
            injector.Start();
            var stdout = injector.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = injector.StandardError.ReadToEndAsync(cancellationToken);
            await injector.WaitForExitAsync(cancellationToken);
            var output = await stdout; var error = await stderr;
            if (injector.ExitCode != 0) return new(false, error + output);
            await reader.BridgeReady.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            GameRuntimeNotifications.NotifyLocalBridgeInjected(game);
            return new(true, "game.wrc9.connected", MessageIsResourceKey: true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            NLog.LogManager.GetCurrentClassLogger().Error(error, "WRC9 injection failed");
            return new(false, error.Message);
        }
        finally { injectionLock.Release(); }
    }
}
