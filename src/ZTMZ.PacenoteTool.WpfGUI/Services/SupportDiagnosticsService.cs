using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.Base.UI;
using ZTMZ.PacenoteTool.Core;

namespace ZTMZ.PacenoteTool.WpfGUI.Services;

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

public sealed class SupportDiagnosticsService
{
    private const int MaximumLogFiles = 7;
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly ZTMZPacenoteTool _tool;
    private readonly GameOverlayManager _gameOverlayManager;
    private readonly SupportBundleWriter _bundleWriter;

    public SupportDiagnosticsService(
        ZTMZPacenoteTool tool,
        GameOverlayManager gameOverlayManager)
    {
        _tool = tool;
        _gameOverlayManager = gameOverlayManager;
        _bundleWriter = new SupportBundleWriter();
    }

    public Task<SupportDiagnosticsSnapshot> CaptureAsync()
    {
        return Task.Run(() => Capture(includePrerequisiteCheck: true));
    }

    public Task<SupportDiagnosticsSnapshot> CaptureRuntimeAsync()
    {
        return Task.Run(() => Capture(includePrerequisiteCheck: false));
    }

    public async Task<SupportBundleResult> CreateBundleAsync()
    {
        var snapshot = await CaptureAsync();
        var diagnosticsJson = JsonSerializer.Serialize(snapshot, JsonOptions);
        var logFiles = GetRecentLogFiles();
        var outputDirectory = AppLevelVariables.Instance.GetPath("support");

        return await Task.Run(() => _bundleWriter.Create(outputDirectory, diagnosticsJson, logFiles));
    }

    public string GetSupportFolder()
    {
        return AppLevelVariables.Instance.GetPath("support");
    }

    private SupportDiagnosticsSnapshot Capture(bool includePrerequisiteCheck)
    {
        IGame? game = _tool.CurrentGame;
        UdpGameConfig? udpConfig = TryGetUdpConfig(game);
        var prerequisite = includePrerequisiteCheck
            ? CapturePrerequisite(game)
            : new PrerequisiteDiagnosticsInfo(string.Empty, true, string.Empty);
        var udpReader = game?.GameDataReader as UdpGameDataReader;
        var udp = udpReader == null ? null : CreateUdpDiagnostics(udpReader);
        var localBridge = game is ILocalGameBridgeDiagnostics bridgeDiagnostics
            ? bridgeDiagnostics.GetLocalBridgeDiagnostics()
            : null;
        var runtimeDiagnostics = CaptureRuntimeDiagnostics(game);
        var profileManager = _tool.IsInitialized ? _tool.ProfileManager : null;
        var audioStatus = profileManager?.Player?.Status ?? AudioPlaybackStatus.NotAvailable;
        var webDashboard = _gameOverlayManager.WebDashboardDiagnostics;

        return new SupportDiagnosticsSnapshot(
            DateTimeOffset.UtcNow,
            new ApplicationDiagnosticsInfo(
                typeof(SupportDiagnosticsService).Assembly.GetName().Version?.ToString() ?? string.Empty,
                Environment.Version.ToString(),
                Environment.OSVersion.VersionString,
                AppLevelVariables.Instance.IsPortableVersion()),
            new GameDiagnosticsInfo(
                game?.Name ?? string.Empty,
                game?.IsInitialized ?? false,
                game?.IsRunning ?? false,
                game?.GameDataReader.GetType().Name ?? string.Empty,
                udpConfig == null ? "Shared memory or game-specific" : "UDP",
                udpConfig == null ? string.Empty : $"{udpConfig.IPAddress}:{udpConfig.Port}",
                game?.GameDataReader.GameState.ToString() ?? string.Empty,
                prerequisite,
                runtimeDiagnostics),
            udp,
            localBridge,
            new AudioDiagnosticsInfo(
                audioStatus.IsActive,
                audioStatus.DeviceId,
                audioStatus.DesiredLatencyMilliseconds,
                audioStatus.QueuedSoundCount,
                audioStatus.LastSoundQueuedUtc,
                audioStatus.LastErrorUtc,
                audioStatus.LastError),
            new WebDashboardDiagnosticsInfo(
                webDashboard.Status.State.ToString(),
                webDashboard.Status.Port,
                webDashboard.Status.Url,
                webDashboard.Status.ErrorMessage,
                webDashboard.ConnectedClientCount,
                webDashboard.TargetFrameRate,
                webDashboard.RenderedFrameCount,
                webDashboard.LastRenderedFrameUtc));
    }

    private static UdpDiagnosticsInfo CreateUdpDiagnostics(UdpGameDataReader reader)
    {
        var listeners = reader.GetUdpListenerDiagnostics();
        var primaryListener = listeners.Count > 0
            ? listeners[0]
            : new UdpListenerDiagnostics(
                "UDP",
                reader.UdpStatus,
                reader.IsUdpTelemetryOptional,
                reader.SecondsWithoutUdpMessage,
                string.Empty);
        var primaryInfo = CreateUdpListenerDiagnostics(primaryListener);
        var additionalListeners = listeners
            .Skip(1)
            .Select(CreateUdpListenerDiagnostics)
            .ToArray();

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
            additionalListeners);
    }

    private static UdpListenerDiagnosticsInfo CreateUdpListenerDiagnostics(UdpListenerDiagnostics listener)
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
        if (game?.GameConfigurations != null
            && game.GameConfigurations.TryGetValue(UdpGameConfig.Name, out var configuration)
            && configuration is UdpGameConfig udpConfig)
        {
            return udpConfig;
        }

        return null;
    }

    private static object? CaptureRuntimeDiagnostics(IGame? game)
    {
        if (game?.GameDataReader is not IGameRuntimeDiagnosticsProvider diagnosticsProvider)
        {
            return null;
        }

        try
        {
            return diagnosticsProvider.CaptureRuntimeDiagnostics();
        }
        catch (Exception exception)
        {
            Logger.Warn(exception, "Failed to capture runtime diagnostics for {0}", game?.Name ?? string.Empty);
            return new Dictionary<string, string>
            {
                ["status"] = "CaptureFailed",
                ["detail"] = RedactSensitivePath(exception.Message)
            };
        }
    }

    private static PrerequisiteDiagnosticsInfo CapturePrerequisite(IGame? game)
    {
        if (game == null)
        {
            return new PrerequisiteDiagnosticsInfo("NotSelected", false, string.Empty);
        }

        try
        {
            var result = game.GamePrerequisiteChecker.CheckPrerequisites(game);
            var details = new List<string>();
            if (!string.IsNullOrWhiteSpace(result.Msg))
            {
                details.Add(result.Msg);
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

    private static IReadOnlyList<string> GetRecentLogFiles()
    {
        var logDirectory = AppLevelVariables.Instance.GetPath("logs");
        if (!Directory.Exists(logDirectory))
        {
            return Array.Empty<string>();
        }

        return Directory.EnumerateFiles(logDirectory, "*.log")
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Take(MaximumLogFiles)
            .Select(file => file.FullName)
            .ToList();
    }

    private static string RedactSensitivePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var replacements = new[]
        {
            (Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "%USERPROFILE%"),
            (AppLevelVariables.Instance.AppConfigFolder, "%ZTMZ_HOME%")
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
