#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.Core;

public sealed record SupportDiagnosticsSnapshot(
    DateTimeOffset CapturedAtUtc,
    ApplicationDiagnosticsInfo Application,
    GameDiagnosticsInfo Game,
    UdpDiagnosticsInfo? Udp,
    LocalBridgeDiagnosticsInfo? LocalBridge,
    AudioDiagnosticsInfo Audio,
    WebDashboardDiagnosticsInfo WebDashboard);

public sealed record ApplicationDiagnosticsInfo(
    string Version,
    string Runtime,
    string OperatingSystem,
    bool IsPortable);

public sealed record GameDiagnosticsInfo(
    string Name,
    bool IsInitialized,
    bool IsRunning,
    string ReaderType,
    string Transport,
    string ExpectedEndpoint,
    string State,
    PrerequisiteDiagnosticsInfo Prerequisite,
    object? RuntimeDiagnostics);

public sealed record PrerequisiteDiagnosticsInfo(
    string Code,
    bool IsHealthy,
    string Detail);

public sealed record UdpDiagnosticsInfo(
    string Name,
    bool IsListening,
    bool IsOptional,
    string Endpoint,
    int SecondsWithoutMessage,
    long PacketCount,
    DateTimeOffset? LastPacketUtc,
    int LastPacketSize,
    DateTimeOffset? LastErrorUtc,
    string LastError,
    IReadOnlyList<UdpListenerDiagnosticsInfo> AdditionalListeners);

public sealed record UdpListenerDiagnosticsInfo(
    string Name,
    bool IsListening,
    bool IsOptional,
    string Endpoint,
    int SecondsWithoutMessage,
    long PacketCount,
    DateTimeOffset? LastPacketUtc,
    int LastPacketSize,
    DateTimeOffset? LastErrorUtc,
    string LastError);

public sealed record AudioDiagnosticsInfo(
    bool IsActive,
    int DeviceId,
    int DesiredLatencyMilliseconds,
    long QueuedSoundCount,
    DateTimeOffset? LastSoundQueuedUtc,
    DateTimeOffset? LastErrorUtc,
    string LastError);

public sealed record WebDashboardDiagnosticsInfo(
    string State,
    int Port,
    string Url,
    string ErrorMessage,
    int ConnectedClientCount,
    int TargetFrameRate,
    long RenderedFrameCount,
    DateTimeOffset? LastRenderedFrameUtc);

public sealed class RuntimeDiagnosticsService
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private readonly ZTMZPacenoteTool _tool;
    private readonly IRuntimeDashboardController _dashboardController;
    private readonly IApplicationPaths _paths;

    public RuntimeDiagnosticsService(
        ZTMZPacenoteTool tool,
        IRuntimeDashboardController dashboardController,
        IApplicationPaths paths)
    {
        _tool = tool ?? throw new ArgumentNullException(nameof(tool));
        _dashboardController = dashboardController ??
            throw new ArgumentNullException(nameof(dashboardController));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public SupportDiagnosticsSnapshot Capture(bool includePrerequisiteCheck)
    {
        var game = _tool.CurrentGame;
        var udpConfig = TryGetUdpConfig(game);
        var prerequisite = includePrerequisiteCheck
            ? CapturePrerequisite(game)
            : new PrerequisiteDiagnosticsInfo(string.Empty, true, string.Empty);
        var udp = game?.GameDataReader is UdpGameDataReader udpReader
            ? CreateUdpDiagnostics(udpReader)
            : null;
        var localBridge = game is ILocalGameBridgeDiagnostics bridge
            ? bridge.GetLocalBridgeDiagnostics()
            : null;
        var profileManager = _tool.IsInitialized ? _tool.ProfileManager : null;
        var audio = profileManager?.Player?.Status ?? AudioPlaybackStatus.NotAvailable;
        var dashboard = _dashboardController.Diagnostics;

        return new SupportDiagnosticsSnapshot(
            DateTimeOffset.UtcNow,
            new ApplicationDiagnosticsInfo(
                Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? string.Empty,
                Environment.Version.ToString(),
                Environment.OSVersion.VersionString,
                _paths.IsPortable),
            new GameDiagnosticsInfo(
                game?.Name ?? string.Empty,
                game?.IsInitialized ?? false,
                game?.IsRunning ?? false,
                game?.GameDataReader.GetType().Name ?? string.Empty,
                udpConfig == null ? "Shared memory or game-specific" : "UDP",
                udpConfig == null ? string.Empty : $"{udpConfig.IPAddress}:{udpConfig.Port}",
                game?.GameDataReader.GameState.ToString() ?? string.Empty,
                prerequisite,
                CaptureRuntimeDiagnostics(game)),
            udp,
            localBridge,
            new AudioDiagnosticsInfo(
                audio.IsActive,
                audio.DeviceId,
                audio.DesiredLatencyMilliseconds,
                audio.QueuedSoundCount,
                audio.LastSoundQueuedUtc,
                audio.LastErrorUtc,
                audio.LastError),
            new WebDashboardDiagnosticsInfo(
                dashboard.Status.State,
                dashboard.Status.Port,
                dashboard.Status.Url,
                dashboard.Status.ErrorMessage,
                dashboard.ConnectedClientCount,
                dashboard.TargetFrameRate,
                dashboard.RenderedFrameCount,
                dashboard.LastRenderedFrameUtc));
    }

    private PrerequisiteDiagnosticsInfo CapturePrerequisite(IGame? game)
    {
        if (game == null)
        {
            return new PrerequisiteDiagnosticsInfo("NotSelected", false, string.Empty);
        }

        try
        {
            var result = _tool.CheckPrerequisite(game);
            var details = new List<string>();
            if (!string.IsNullOrWhiteSpace(result.Msg))
            {
                details.Add(RedactSensitivePath(result.Msg));
            }

            if (result.Params != null)
            {
                details.AddRange(result.Params
                    .Where(parameter => parameter != null)
                    .Select(parameter => RedactSensitivePath(parameter!.ToString() ?? string.Empty))
                    .Where(parameter => !string.IsNullOrWhiteSpace(parameter)));
            }

            return new PrerequisiteDiagnosticsInfo(
                result.Code.ToString(),
                result.IsOK,
                string.Join(" | ", details));
        }
        catch (Exception exception)
        {
            Logger.Warn(exception, "Diagnostics prerequisite check failed for {0}", game.Name);
            return new PrerequisiteDiagnosticsInfo(
                "Exception",
                false,
                $"{exception.GetType().Name}: {RedactSensitivePath(exception.Message)}");
        }
    }

    private object? CaptureRuntimeDiagnostics(IGame? game)
    {
        if (game?.GameDataReader is not IGameRuntimeDiagnosticsProvider provider)
        {
            return null;
        }

        try
        {
            return provider.CaptureRuntimeDiagnostics();
        }
        catch (Exception exception)
        {
            Logger.Warn(exception, "Failed to capture runtime diagnostics for {0}", game.Name);
            return new Dictionary<string, string>
            {
                ["status"] = "CaptureFailed",
                ["detail"] = RedactSensitivePath(exception.Message)
            };
        }
    }

    private static UdpDiagnosticsInfo CreateUdpDiagnostics(UdpGameDataReader reader)
    {
        var listeners = reader.GetUdpListenerDiagnostics();
        var primary = listeners.Count > 0
            ? listeners[0]
            : new UdpListenerDiagnostics(
                "UDP",
                reader.UdpStatus,
                reader.IsUdpTelemetryOptional,
                reader.SecondsWithoutUdpMessage,
                string.Empty);
        var primaryInfo = CreateUdpListenerDiagnostics(primary);
        return new UdpDiagnosticsInfo(
            primaryInfo.Name,
            primaryInfo.IsListening,
            primaryInfo.IsOptional,
            primaryInfo.Endpoint,
            primaryInfo.SecondsWithoutMessage,
            primaryInfo.PacketCount,
            primaryInfo.LastPacketUtc,
            primaryInfo.LastPacketSize,
            primaryInfo.LastErrorUtc,
            primaryInfo.LastError,
            listeners.Skip(1).Select(CreateUdpListenerDiagnostics).ToArray());
    }

    private static UdpListenerDiagnosticsInfo CreateUdpListenerDiagnostics(
        UdpListenerDiagnostics listener)
    {
        var status = listener.Status;
        var endpoint = string.IsNullOrWhiteSpace(status.ListenAddress)
            ? listener.ConfiguredEndpoint
            : $"{status.ListenAddress}:{status.Port}";
        return new UdpListenerDiagnosticsInfo(
            listener.Name,
            status.IsListening,
            listener.IsOptional,
            endpoint,
            listener.SecondsWithoutMessage,
            status.PacketCount,
            status.LastPacketUtc,
            status.LastPacketSize,
            status.LastErrorUtc,
            status.LastError);
    }

    private static UdpGameConfig? TryGetUdpConfig(IGame? game)
    {
        return game?.GameConfigurations != null &&
               game.GameConfigurations.TryGetValue(UdpGameConfig.Name, out var configuration) &&
               configuration is UdpGameConfig udp
            ? udp
            : null;
    }

    private string RedactSensitivePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var replacements = new[]
        {
            (Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "%USERPROFILE%"),
            (_paths.DataRoot, "%ZTMZ_HOME%")
        };
        foreach (var (path, replacement) in replacements)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                value = value.Replace(path, replacement, StringComparison.OrdinalIgnoreCase);
            }
        }

        return value;
    }
}
