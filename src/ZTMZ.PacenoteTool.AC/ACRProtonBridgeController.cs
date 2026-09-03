using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ZTMZ.PacenoteTool.AC;

internal static class ACRProtonBridgeController
{
    private const string RelayFileName = "ACRProtonRelay.exe";
    private const string BridgeFileName = "ACRFmodLivePacenoteBridge.dll";
    private const string ManifestFileName = "acr-live-bridge.manifest.json";
    private const string FmodStudioRelativePath = "Engine/Plugins/fmod/FMODStudio/Binaries/Win64/fmodstudio.dll";
    private const string AcrSteamApplicationId = "3917090";
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    private static readonly object ProcessLock = new();
    private static Process? _relayProcess;

    public static ACRExperimentalBridgeStatus Inspect()
    {
        var bundle = GetBundlePaths();
        if (!File.Exists(bundle.RelayPath) || !File.Exists(bundle.BridgePath) || !File.Exists(bundle.ManifestPath))
        {
            return new ACRExperimentalBridgeStatus(false, false, false,
                ACRExperimentalBridgeStatusKind.BundleUnavailable);
        }

        var context = ACRProtonProcessLocator.Find();
        if (context is null)
        {
            return new ACRExperimentalBridgeStatus(true, false, false,
                ACRExperimentalBridgeStatusKind.GameNotRunning);
        }

        var relayStatus = ACRProtonBridgeRuntime.GetStatus();
        return relayStatus.Connected && relayStatus.Injected
            ? new ACRExperimentalBridgeStatus(true, true, true, ACRExperimentalBridgeStatusKind.AlreadyInjected)
            : new ACRExperimentalBridgeStatus(true, true, false, ACRExperimentalBridgeStatusKind.Ready);
    }

    public static async Task<ACRExperimentalBridgeResult> StartAsync()
    {
        var endpoint = ACRProtonBridgeRuntime.GetEndpoint();
        if (endpoint is null)
        {
            return new ACRExperimentalBridgeResult(false,
                "The ACR Proton relay listener is not active. Wait until the game is detected, then try again.");
        }

        var existingStatus = ACRProtonBridgeRuntime.GetStatus();
        if (existingStatus.Connected && existingStatus.Injected)
        {
            return new ACRExperimentalBridgeResult(true, "The ACR Proton bridge is already connected.");
        }

        var context = ACRProtonProcessLocator.Find();
        if (context is null)
        {
            return new ACRExperimentalBridgeResult(false, "Assetto Corsa Rally is not running under Proton.");
        }
        if (string.IsNullOrWhiteSpace(context.ProtonPath) || !File.Exists(context.ProtonPath))
        {
            return new ACRExperimentalBridgeResult(false,
                "The Proton runner used by Assetto Corsa Rally could not be located from the running process.");
        }
        if (string.IsNullOrWhiteSpace(context.CompatDataPath) || !Directory.Exists(context.CompatDataPath))
        {
            return new ACRExperimentalBridgeResult(false,
                "The STEAM_COMPAT_DATA_PATH used by Assetto Corsa Rally could not be located.");
        }

        var bundle = GetBundlePaths();
        var compatibility = await ValidateCompatibilityAsync(context, bundle.ManifestPath).ConfigureAwait(false);
        if (!compatibility.Succeeded)
        {
            return compatibility;
        }

        try
        {
            lock (ProcessLock)
            {
                if (_relayProcess is { HasExited: false })
                {
                    return new ACRExperimentalBridgeResult(false,
                        "The ACR Proton relay is already starting. Wait a moment and refresh its status.");
                }
                _relayProcess?.Dispose();
                _relayProcess = StartRelay(context, bundle.RelayPath, endpoint);
            }

            var timeoutAt = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (DateTime.UtcNow < timeoutAt)
            {
                var status = ACRProtonBridgeRuntime.GetStatus();
                if (status.Connected && status.Injected)
                {
                    return new ACRExperimentalBridgeResult(true, "The ACR Proton bridge is connected and injected.");
                }

                Process? relay;
                lock (ProcessLock)
                {
                    relay = _relayProcess;
                }
                if (relay is { HasExited: true })
                {
                    return new ACRExperimentalBridgeResult(false,
                        $"The ACR Proton relay exited with code {relay.ExitCode}. {status.Detail}".Trim());
                }
                await Task.Delay(100).ConfigureAwait(false);
            }

            var finalStatus = ACRProtonBridgeRuntime.GetStatus();
            return new ACRExperimentalBridgeResult(false,
                string.IsNullOrWhiteSpace(finalStatus.Detail)
                    ? "The ACR Proton relay did not connect within 10 seconds."
                    : $"The ACR Proton relay did not become ready: {finalStatus.Detail}");
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Unable to launch the ACR Proton relay.");
            return new ACRExperimentalBridgeResult(false, $"Unable to launch the ACR Proton relay: {exception.Message}");
        }
    }

    private static Process StartRelay(
        ACRProtonProcessContext context,
        string relayPath,
        ACRProtonEndpoint endpoint)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = context.ProtonPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(relayPath) ?? AppContext.BaseDirectory
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add(relayPath);
        startInfo.ArgumentList.Add(endpoint.Port.ToString(System.Globalization.CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add(endpoint.Token);

        foreach (var pair in context.Environment)
        {
            if (ShouldForwardEnvironmentVariable(pair.Key))
            {
                startInfo.Environment[pair.Key] = pair.Value;
            }
        }
        startInfo.Environment["STEAM_COMPAT_DATA_PATH"] = context.CompatDataPath;
        startInfo.Environment["STEAM_COMPAT_APP_ID"] = AcrSteamApplicationId;
        startInfo.Environment["SteamAppId"] = AcrSteamApplicationId;
        startInfo.Environment["SteamGameId"] = AcrSteamApplicationId;

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Proton did not return a relay process.");
        process.OutputDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                Logger.Debug("ACR Proton relay: {0}", args.Data);
            }
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                Logger.Warn("ACR Proton relay: {0}", args.Data);
            }
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private static bool ShouldForwardEnvironmentVariable(string name)
    {
        return name.StartsWith("STEAM_COMPAT_", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("PROTON_", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("WINE", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("DXVK_", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("VKD3D_", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "SteamAppId", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "SteamGameId", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<ACRExperimentalBridgeResult> ValidateCompatibilityAsync(
        ACRProtonProcessContext context,
        string manifestPath)
    {
        if (string.IsNullOrWhiteSpace(context.ExecutablePath) || !File.Exists(context.ExecutablePath))
        {
            return new ACRExperimentalBridgeResult(false,
                "The running ACR executable path could not be resolved to a Linux file path.");
        }

        ACRBridgeManifest? manifest;
        try
        {
            await using var stream = File.OpenRead(manifestPath);
            manifest = await JsonSerializer.DeserializeAsync<ACRBridgeManifest>(stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            return new ACRExperimentalBridgeResult(false, $"Cannot read the ACR bridge manifest: {exception.Message}");
        }

        if (manifest is null || manifest.SchemaVersion != 1 ||
            string.IsNullOrWhiteSpace(manifest.AcrExecutableSha256) ||
            string.IsNullOrWhiteSpace(manifest.FmodStudioSha256))
        {
            return new ACRExperimentalBridgeResult(false, "The ACR bridge manifest is invalid.");
        }

        var executable = new FileInfo(context.ExecutablePath);
        var gameRoot = executable.Directory?.Parent?.Parent?.Parent?.FullName;
        var fmodPath = gameRoot is null
            ? string.Empty
            : Path.Combine(gameRoot, FmodStudioRelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fmodPath))
        {
            return new ACRExperimentalBridgeResult(false,
                "ACR's FMOD Studio library was not found at the expected location.");
        }

        try
        {
            var executableHash = CalculateSha256Async(context.ExecutablePath);
            var fmodHash = CalculateSha256Async(fmodPath);
            await Task.WhenAll(executableHash, fmodHash).ConfigureAwait(false);
            if (!string.Equals(await executableHash, manifest.AcrExecutableSha256, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(await fmodHash, manifest.FmodStudioSha256, StringComparison.OrdinalIgnoreCase))
            {
                return new ACRExperimentalBridgeResult(false,
                    "This ACR build does not match the bridge manifest. Injection was blocked.");
            }
        }
        catch (Exception exception)
        {
            return new ACRExperimentalBridgeResult(false, $"Cannot validate the running ACR build: {exception.Message}");
        }

        return new ACRExperimentalBridgeResult(true, string.Empty);
    }

    private static async Task<string> CalculateSha256Async(string path)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream).ConfigureAwait(false));
    }

    private static (string RelayPath, string BridgePath, string ManifestPath) GetBundlePaths()
    {
        var assemblyDirectory = Path.GetDirectoryName(typeof(ACRProtonBridgeController).Assembly.Location)
            ?? throw new InvalidOperationException("Cannot locate the ACR game provider directory.");
        var bridgeDirectory = Path.Combine(assemblyDirectory, "ACRExperimentalBridge");
        return (
            Path.Combine(bridgeDirectory, RelayFileName),
            Path.Combine(bridgeDirectory, BridgeFileName),
            Path.Combine(bridgeDirectory, ManifestFileName));
    }

    private sealed class ACRBridgeManifest
    {
        public int SchemaVersion { get; init; }
        public string AcrExecutableSha256 { get; init; } = string.Empty;
        public string FmodStudioSha256 { get; init; } = string.Empty;
    }
}

internal sealed record ACRProtonProcessContext(
    int ProcessId,
    string ExecutablePath,
    string ProtonPath,
    string CompatDataPath,
    IReadOnlyDictionary<string, string> Environment);

internal static class ACRProtonProcessLocator
{
    private const string AcrSteamApplicationId = "3917090";

    public static ACRProtonProcessContext? Find(string procRoot = "/proc")
    {
        if (!Directory.Exists(procRoot))
        {
            return null;
        }

        ACRProtonProcessContext? fallback = null;
        foreach (var processDirectory in Directory.EnumerateDirectories(procRoot))
        {
            if (!int.TryParse(Path.GetFileName(processDirectory), out var processId))
            {
                continue;
            }

            try
            {
                var arguments = ReadNullSeparated(Path.Combine(processDirectory, "cmdline"));
                var processName = ReadText(Path.Combine(processDirectory, "comm")).Trim();
                var environment = ReadEnvironment(Path.Combine(processDirectory, "environ"));
                if (!IsAcrProcess(processName, arguments, environment))
                {
                    continue;
                }

                var executablePath = FindAcrExecutable(arguments, environment);
                var protonPath = FindProtonPath(processDirectory, arguments, environment, procRoot);
                environment.TryGetValue("STEAM_COMPAT_DATA_PATH", out var compatDataPath);
                var candidate = new ACRProtonProcessContext(
                    processId,
                    executablePath,
                    protonPath,
                    compatDataPath ?? string.Empty,
                    environment);
                if (!string.IsNullOrWhiteSpace(candidate.ExecutablePath) &&
                    !string.IsNullOrWhiteSpace(candidate.ProtonPath) &&
                    !string.IsNullOrWhiteSpace(candidate.CompatDataPath))
                {
                    return candidate;
                }
                fallback ??= candidate;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
        return fallback;
    }

    private static bool IsAcrProcess(
        string processName,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> environment)
    {
        var executableMatch = IsAcrExecutableName(processName) || arguments.Any(IsAcrExecutableName);
        if (!executableMatch)
        {
            return false;
        }

        return !environment.TryGetValue("STEAM_COMPAT_APP_ID", out var appId) ||
            string.IsNullOrWhiteSpace(appId) ||
            string.Equals(appId, AcrSteamApplicationId, StringComparison.Ordinal);
    }

    private static bool IsAcrExecutableName(string value)
    {
        var name = Path.GetFileName(value.Replace('\\', '/'));
        return string.Equals(name, "acr", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "acr.exe", StringComparison.OrdinalIgnoreCase);
    }

    private static string FindAcrExecutable(
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> environment)
    {
        foreach (var argument in arguments.Where(IsAcrExecutableName))
        {
            var direct = ConvertWinePath(argument);
            if (File.Exists(direct))
            {
                return Path.GetFullPath(direct);
            }
        }

        if (environment.TryGetValue("STEAM_COMPAT_INSTALL_PATH", out var installPath))
        {
            var directory = Path.Combine(installPath, "ACR", "Binaries", "Win64");
            if (Directory.Exists(directory))
            {
                var candidate = Directory.EnumerateFiles(directory)
                    .FirstOrDefault(path => string.Equals(
                        Path.GetFileName(path),
                        "acr.exe",
                        StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    return candidate;
                }
            }
        }
        return string.Empty;
    }

    private static string ConvertWinePath(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.StartsWith("Z:/", StringComparison.OrdinalIgnoreCase)
            ? normalized[2..]
            : path;
    }

    private static string FindProtonPath(
        string processDirectory,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> environment,
        string procRoot)
    {
        var argumentPath = arguments.FirstOrDefault(IsProtonExecutable);
        if (!string.IsNullOrWhiteSpace(argumentPath))
        {
            return argumentPath;
        }

        if (environment.TryGetValue("STEAM_COMPAT_TOOL_PATHS", out var toolPaths))
        {
            foreach (var toolPath in toolPaths.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = Path.Combine(toolPath, "proton");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        var currentDirectory = processDirectory;
        for (var depth = 0; depth < 12; depth++)
        {
            var parentId = ReadParentProcessId(Path.Combine(currentDirectory, "status"));
            if (parentId <= 0)
            {
                break;
            }
            currentDirectory = Path.Combine(procRoot, parentId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var parentArguments = ReadNullSeparated(Path.Combine(currentDirectory, "cmdline"));
            argumentPath = parentArguments.FirstOrDefault(IsProtonExecutable);
            if (!string.IsNullOrWhiteSpace(argumentPath))
            {
                return argumentPath;
            }
        }
        return string.Empty;
    }

    private static bool IsProtonExecutable(string value)
    {
        return string.Equals(Path.GetFileName(value), "proton", StringComparison.OrdinalIgnoreCase) && File.Exists(value);
    }

    private static int ReadParentProcessId(string statusPath)
    {
        if (!File.Exists(statusPath))
        {
            return 0;
        }
        var line = File.ReadLines(statusPath).FirstOrDefault(candidate => candidate.StartsWith("PPid:", StringComparison.Ordinal));
        return line != null && int.TryParse(line[5..].Trim(), out var parentId) ? parentId : 0;
    }

    private static string ReadText(string path) => File.Exists(path) ? File.ReadAllText(path) : string.Empty;

    private static IReadOnlyList<string> ReadNullSeparated(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }
        return Encoding.UTF8.GetString(File.ReadAllBytes(path))
            .Split('\0', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static IReadOnlyDictionary<string, string> ReadEnvironment(string path)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in ReadNullSeparated(path))
        {
            var separator = entry.IndexOf('=');
            if (separator > 0)
            {
                result[entry[..separator]] = entry[(separator + 1)..];
            }
        }
        return result;
    }
}
