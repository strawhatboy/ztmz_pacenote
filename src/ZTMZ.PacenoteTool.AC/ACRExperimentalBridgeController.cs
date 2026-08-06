using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace ZTMZ.PacenoteTool.AC;

internal sealed record ACRExperimentalBridgeStatus(
    bool BundleAvailable,
    bool GameRunning,
    bool AlreadyInjected,
    string Message);

internal sealed record ACRExperimentalBridgeResult(bool Succeeded, string Message);

internal static class ACRExperimentalBridgeController
{
    private const string BridgeDirectoryName = "ACRExperimentalBridge";
    private const string InjectorFileName = "ACRPacenoteTraceInjector.exe";
    private const string BridgeFileName = "ACRFmodLivePacenoteBridge.dll";
    private const string ManifestFileName = "acr-live-bridge.manifest.json";
    private const string FmodStudioRelativePath = "Engine\\Plugins\\fmod\\FMODStudio\\Binaries\\Win64\\fmodstudio.dll";
    private static readonly JsonSerializerOptions ManifestJsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static ACRExperimentalBridgeStatus Inspect()
    {
        var bundle = GetBundlePaths();
        if (!File.Exists(bundle.InjectorPath) || !File.Exists(bundle.BridgePath) || !File.Exists(bundle.ManifestPath))
        {
            return new ACRExperimentalBridgeStatus(false, false, false,
                "Internal ACR bridge files are not installed in this build.");
        }

        var process = FindRunningAcr();
        if (process is null)
        {
            return new ACRExperimentalBridgeStatus(true, false, false,
                "Waiting for Assetto Corsa Rally to start.");
        }

        return IsBridgeLoaded(process)
            ? new ACRExperimentalBridgeStatus(true, true, true,
                "Experimental bridge is already loaded. Exit Assetto Corsa Rally to remove it.")
            : new ACRExperimentalBridgeStatus(true, true, false,
                "Ready for an explicit experimental bridge injection.");
    }

    public static async Task<ACRExperimentalBridgeResult> InjectAsync()
    {
        var status = Inspect();
        if (!status.BundleAvailable || !status.GameRunning || status.AlreadyInjected)
        {
            return new ACRExperimentalBridgeResult(false, status.Message);
        }

        var process = FindRunningAcr();
        if (process is null)
        {
            return new ACRExperimentalBridgeResult(false, "Assetto Corsa Rally is no longer running.");
        }

        var bundle = GetBundlePaths();
        var compatibility = await ValidateCompatibilityAsync(process, bundle.ManifestPath);
        if (!compatibility.Succeeded)
        {
            return compatibility;
        }

        try
        {
            using var injector = Process.Start(new ProcessStartInfo
            {
                FileName = bundle.InjectorPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = bundle.DirectoryPath
            }.WithArguments(process.Id, bundle.BridgePath));

            if (injector is null)
            {
                return new ACRExperimentalBridgeResult(false, "Unable to start the internal bridge injector.");
            }

            var stdoutTask = injector.StandardOutput.ReadToEndAsync();
            var stderrTask = injector.StandardError.ReadToEndAsync();
            await injector.WaitForExitAsync();
            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            if (injector.ExitCode != 0)
            {
                var detail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                return new ACRExperimentalBridgeResult(false,
                    $"Bridge injection failed (exit code {injector.ExitCode}): {detail.Trim()}");
            }

            return new ACRExperimentalBridgeResult(true,
                string.IsNullOrWhiteSpace(stdout) ? "Experimental bridge injected. Drive through a pacenote to test it." : stdout.Trim());
        }
        catch (Exception exception)
        {
            return new ACRExperimentalBridgeResult(false, $"Bridge injection failed: {exception.Message}");
        }
    }

    private static async Task<ACRExperimentalBridgeResult> ValidateCompatibilityAsync(Process process, string manifestPath)
    {
        ACRExperimentalBridgeManifest? manifest;
        try
        {
            await using var manifestStream = File.OpenRead(manifestPath);
            manifest = await JsonSerializer.DeserializeAsync<ACRExperimentalBridgeManifest>(manifestStream, ManifestJsonOptions);
        }
        catch (Exception exception)
        {
            return new ACRExperimentalBridgeResult(false, $"Cannot read the internal bridge manifest: {exception.Message}");
        }

        var executablePath = TryGetExecutablePath(process);
        if (manifest is null || string.IsNullOrWhiteSpace(executablePath) ||
            manifest.SchemaVersion != 1 || string.IsNullOrWhiteSpace(manifest.AcrExecutableSha256) ||
            string.IsNullOrWhiteSpace(manifest.FmodStudioSha256))
        {
            return new ACRExperimentalBridgeResult(false, "The internal bridge manifest is invalid or the ACR executable cannot be inspected.");
        }

        var gameRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(executablePath)!, "..", "..", ".."));
        var fmodPath = Path.Combine(gameRoot, FmodStudioRelativePath);
        if (!File.Exists(fmodPath))
        {
            return new ACRExperimentalBridgeResult(false, "ACR's FMOD Studio library was not found at the expected location.");
        }

        try
        {
            var executableHashTask = CalculateSha256Async(executablePath);
            var fmodHashTask = CalculateSha256Async(fmodPath);
            await Task.WhenAll(executableHashTask, fmodHashTask);
            if (!string.Equals(await executableHashTask, manifest.AcrExecutableSha256, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(await fmodHashTask, manifest.FmodStudioSha256, StringComparison.OrdinalIgnoreCase))
            {
                return new ACRExperimentalBridgeResult(false,
                    "This ACR build does not match the internal bridge manifest. Do not inject until the bridge has been validated for this game update.");
            }
        }
        catch (Exception exception)
        {
            return new ACRExperimentalBridgeResult(false, $"Cannot validate the running ACR build: {exception.Message}");
        }

        return new ACRExperimentalBridgeResult(true, string.Empty);
    }

    private static Task<string> CalculateSha256Async(string path) => Task.Run(() =>
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    });

    private static Process? FindRunningAcr()
    {
        var processes = Process.GetProcessesByName("acr");
        return processes.Length == 1 ? processes[0] : null;
    }

    private static bool IsBridgeLoaded(Process process)
    {
        try
        {
            return process.Modules.Cast<ProcessModule>().Any(module =>
                string.Equals(Path.GetFileName(module.FileName), BridgeFileName, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    private static string? TryGetExecutablePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    private static (string DirectoryPath, string InjectorPath, string BridgePath, string ManifestPath) GetBundlePaths()
    {
        var assemblyDirectory = Path.GetDirectoryName(typeof(ACRExperimentalBridgeController).Assembly.Location)
            ?? throw new InvalidOperationException("Cannot locate the ACR game provider directory.");
        var directoryPath = Path.Combine(assemblyDirectory, BridgeDirectoryName);
        return (directoryPath,
            Path.Combine(directoryPath, InjectorFileName),
            Path.Combine(directoryPath, BridgeFileName),
            Path.Combine(directoryPath, ManifestFileName));
    }

    private sealed class ACRExperimentalBridgeManifest
    {
        public int SchemaVersion { get; init; }
        public string AcrExecutableSha256 { get; init; } = string.Empty;
        public string FmodStudioSha256 { get; init; } = string.Empty;
    }
}

internal static class ProcessStartInfoExtensions
{
    public static ProcessStartInfo WithArguments(this ProcessStartInfo startInfo, int processId, string bridgePath)
    {
        startInfo.ArgumentList.Add(processId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add(bridgePath);
        return startInfo;
    }
}
