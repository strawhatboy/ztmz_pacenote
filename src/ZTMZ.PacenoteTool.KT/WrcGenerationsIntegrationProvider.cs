using System.Diagnostics;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.KT;

public sealed class WrcGenerationsIntegrationProvider : IGameIntegrationProvider
{
    private readonly SemaphoreSlim injectionLock = new(1, 1);
    internal static bool IsVerifiedFmodVersion(int major, int minor, int build, int revision) =>
        (major, minor, build, revision) == (0, 2, 2, 6);
    public Type ConfigurationType => typeof(WrcGenerationsConfig);
    public IReadOnlyList<GameSettingField> Fields { get; } =
        [new("controlsEnabled", "game.wrcg.enable", GameSettingFieldKind.Boolean,
            Confirmation: new("game.wrcg.title", "game.wrcg.warning", "game.wrcg.inject", WhenEnablingOnly: true)),
         new("forwarded", "game.wrcg.forwarded", GameSettingFieldKind.Boolean, "game.wrcg.forwardedHint")];
    public IReadOnlyList<string> InformationKeys => ["game.wrcg.limitations"];
    public IReadOnlyList<GameIntegrationAction> Actions { get; } =
        [new("inject", "game.wrcg.inject", Confirmation: new("game.wrcg.title", "game.wrcg.warning", "game.wrcg.inject"),
            SuccessTitleKey: "game.wrcg.title", FailureTitleKey: "game.wrcg.title", ShowSuccessMessage: true)];
    public TimeSpan StatusRefreshInterval => TimeSpan.FromSeconds(1);
    public object GetFieldValue(IGameConfig configuration, string fieldId) => fieldId switch
    {
        "forwarded" => ((WrcGenerationsConfig)configuration).ReceiveForwardedTelemetry,
        "controlsEnabled" => ((WrcGenerationsConfig)configuration).ControlsEnabled,
        _ => throw new ArgumentOutOfRangeException(nameof(fieldId))
    };
    public void SetFieldValue(IGameConfig configuration, string fieldId, object? value, IGame game)
    {
        if (fieldId == "controlsEnabled")
        {
            ((WrcGenerationsConfig)configuration).ControlsEnabled = Convert.ToBoolean(value);
            if (((WrcGenerationsConfig)configuration).ControlsEnabled)
                GameRuntimeNotifications.NotifyLocalBridgeControlsEnabled(game);
            return;
        }
        if (fieldId != "forwarded") throw new ArgumentOutOfRangeException(nameof(fieldId));
        ((WrcGenerationsConfig)configuration).ReceiveForwardedTelemetry = Convert.ToBoolean(value);
    }
    public GameIntegrationStatus GetStatus(IGameConfig configuration, IGame game)
    {
        var reader = (WrcGenerationsGameDataReader)game.GameDataReader;
        var status = ((ILocalGameBridgeControl)game).GetLocalBridgeDiagnostics();
        return new(reader.BridgeConnected ? "game.wrcg.connected" : !status.IsEnabled ? "game.wrcg.disabled" : "game.wrcg.disconnected",
            EnabledActionIds: status.IsEnabled && status.IsAvailable && game.IsRunning && !reader.BridgeConnected
                ? new HashSet<string> { "inject" } : new HashSet<string>());
    }

    public async Task<GameIntegrationActionResult> ExecuteActionAsync(string actionId, IGameConfig configuration, IGame game, CancellationToken cancellationToken)
    {
        if (actionId != "inject") throw new ArgumentOutOfRangeException(nameof(actionId));
        if (!OperatingSystem.IsWindows()) return new(false, "game.wrcg.windowsOnly", MessageIsResourceKey: true);
        await injectionLock.WaitAsync(cancellationToken);
        try
        {
            if (!((WrcGenerationsConfig)configuration).ControlsEnabled)
                return new(false, "game.wrcg.disabled", MessageIsResourceKey: true);
            if (((WrcGenerationsGameDataReader)game.GameDataReader).BridgeConnected)
                return new(true, "game.wrcg.connected", MessageIsResourceKey: true);
            using var process = Process.GetProcessesByName("WRCG").FirstOrDefault();
            if (process?.MainModule?.FileName is not { } executable) return new(false, "game.wrcg.startGame", MessageIsResourceKey: true);
            var directory = Path.GetDirectoryName(executable)!;
            var fmod = FileVersionInfo.GetVersionInfo(Path.Combine(directory, "fmodstudio.dll"));
            NLog.LogManager.GetCurrentClassLogger().Info("WRCG FMOD version: {0}; numeric: {1}.{2}.{3}.{4}",
                fmod.FileVersion, fmod.FileMajorPart, fmod.FileMinorPart, fmod.FileBuildPart, fmod.FilePrivatePart);
            if (!IsVerifiedFmodVersion(fmod.FileMajorPart, fmod.FileMinorPart, fmod.FileBuildPart, fmod.FilePrivatePart))
                return new(false, "game.wrcg.unsupportedFmod", MessageIsResourceKey: true);
            var ready = ((WrcGenerationsGameDataReader)game.GameDataReader).EnsureBridgeSession(process.Id, directory);
            var injector = Path.Combine(Path.GetDirectoryName(typeof(WrcGenerations).Assembly.Location)!, "WRCGBridge", "WrcGenerationsBridgeInject.exe");
            using var child = new Process { StartInfo = new(injector) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } };
            child.StartInfo.ArgumentList.Add(process.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
            child.Start();
            var stdout = child.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = child.StandardError.ReadToEndAsync(cancellationToken);
            await child.WaitForExitAsync(cancellationToken);
            var output = await stdout; var error = await stderr;
            if (child.ExitCode != 0) return new(false, error + output);
            await ready.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            NLog.LogManager.GetCurrentClassLogger().Info("WRCG live bridge connected to process {0}", process.Id);
            GameRuntimeNotifications.NotifyLocalBridgeInjected(game);
            return new(true, "game.wrcg.connected", MessageIsResourceKey: true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            NLog.LogManager.GetCurrentClassLogger().Error(error, "WRCG bridge injection failed");
            return new(false, error.Message);
        }
        finally { injectionLock.Release(); }
    }
}
