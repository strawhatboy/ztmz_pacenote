using System.Globalization;
using System.Runtime.InteropServices;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.Core;
using ZTMZ.PacenoteTool.Audio.PortAudio;
using ZTMZ.PacenoteTool.WebDashboard;

namespace ZTMZ.PacenoteTool.Linux;

public static class Program
{
    private const string DataRootOption = "--data-root";
    private const string ResourceRootOption = "--resource-root";
    private const string ListenGameOption = "--listen-game";
    private const string ListenSecondsOption = "--listen-seconds";
    private const string PlayAudioOption = "--play-audio";
    private const string AudioDeviceOption = "--audio-device";
    private const string WebDashboardOption = "--web-dashboard";
    private const string WebDashboardPortOption = "--web-dashboard-port";
    private const string ResourceHomeEnvironmentVariable = "ZTMZ_RESOURCE_HOME";

    public static async Task<int> Main(string[] args)
    {
        if (args.Contains("--help", StringComparer.OrdinalIgnoreCase) ||
            args.Contains("-h", StringComparer.OrdinalIgnoreCase))
        {
            PrintUsage();
            return 0;
        }

        if (!TryReadOptions(args, out var options, out var optionError))
        {
            Console.Error.WriteLine(optionError);
            PrintUsage();
            return 64;
        }

        if (!string.IsNullOrWhiteSpace(options.DataRoot))
        {
            Environment.SetEnvironmentVariable(
                EnvironmentConfigurationLocation.DataHomeEnvironmentVariable,
                options.DataRoot);
        }

        try
        {
            AudioDecodingServices.Decoder = new SoundFileAudioFileDecoder();
            return await RunDiagnosticsAsync(AppLevelVariables.Instance, options);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"ZTMZ Linux host failed: {exception}");
            return 1;
        }
    }

    private static async Task<int> RunDiagnosticsAsync(
        IApplicationPaths paths,
        LinuxHostOptions options)
    {
        var dataRoot = paths.DataRoot;
        var dataRootWritable = VerifyWritableDirectory(dataRoot);
        var resourceRoot = ResolveResourceRoot(options.ResourceRoot);
        var gamesDirectory = Path.Combine(AppContext.BaseDirectory, Constants.PATH_GAMES);
        var audioDevices = new PortAudioAudioOutputDeviceEnumerator();
        var audioPlayback = new PortAudioAudioPlaybackEngineFactory();
        var runtime = new ZtmzRuntime(paths, audioDevices, audioPlayback);
        var state = await runtime.InitializeAsync(
            new ZtmzRuntimeOptions(
                gamesDirectory,
                new ResourceBootstrapOptions(
                    Path.Combine(
                        resourceRoot,
                        ZTMZ.PacenoteTool.Base.Script.ScriptResource.DBNAME),
                    [
                        Path.Combine(resourceRoot, Constants.PATH_LANGUAGE),
                        paths.GetPath(Constants.PATH_LANGUAGE)
                    ],
                    Config.Instance.Language,
                    InitializeLocalization: true),
                CheckPrerequisites: true));

        Console.WriteLine("ZTMZ native Linux host bootstrap");
        Console.WriteLine($"OS: {RuntimeInformation.OSDescription}");
        Console.WriteLine($"Architecture: {RuntimeInformation.OSArchitecture}");
        Console.WriteLine($"Data root: {dataRoot}");
        Console.WriteLine($"Data root writable: {FormatAvailability(dataRootWritable)}");
        Console.WriteLine($"Resource root: {resourceRoot}");
        Console.WriteLine(
            $"Official pacenote data: " +
            $"{state.Resources.ProvisioningStatus?.ToString().ToLowerInvariant() ?? "existing"}; " +
            $"{state.Resources.PacenoteCount} tokens loaded");
        Console.WriteLine($"Game plugin directory: {gamesDirectory}");
        Console.WriteLine($"Game providers loaded: {state.Games.Count}");
        foreach (var game in state.Games)
        {
            Console.WriteLine(
                $"  - {game.Name}{FormatUdpConfiguration(game)}; " +
                $"prerequisite: {FormatPrerequisiteStatus(state, game)}");
        }
        Console.WriteLine($"Co-driver packages loaded: {state.CoDrivers.Count}");
        foreach (var coDriver in state.CoDrivers)
        {
            var audioFileCount = Config.Instance.PreloadSounds
                ? coDriver.tokens.Values.Sum(sounds => sounds.Count)
                : coDriver.tokensPath.Values.Sum(paths => paths.Count);
            Console.WriteLine(
                $"  - {coDriver.Info?.name ?? coDriver.Info?.Path ?? "unknown"}: " +
                $"{audioFileCount} audio files");
        }

        Console.WriteLine($"Profiles loaded: {state.Profiles.Count}");
        foreach (var profile in state.Profiles)
        {
            var scriptCount = Directory.EnumerateFiles(
                    profile,
                    "*.pacenote",
                    SearchOption.AllDirectories)
                .Count();
            Console.WriteLine($"  - {Path.GetFileName(profile)}: {scriptCount} pacenote scripts");
        }

        Console.WriteLine($"PortAudio output devices: {state.OutputDevices.Count}");
        foreach (var device in state.OutputDevices)
        {
            Console.WriteLine(
                $"  - [{device.Id}] {device.Name}" +
                (device.IsDefault ? " (default)" : string.Empty));
        }
        Console.WriteLine("Native overlay: unavailable on Linux");

        if (!dataRootWritable)
        {
            return 2;
        }

        using var webDashboard = StartWebDashboard(options, resourceRoot);

        if (state.Games.Count == 0 && webDashboard == null)
        {
            return 4;
        }

        if (state.Games.Count == 0 && !string.IsNullOrWhiteSpace(options.ListenGame))
        {
            Console.Error.WriteLine("No game providers are available for telemetry.");
            return 5;
        }

        if (!string.IsNullOrWhiteSpace(options.ListenGame) &&
            !await ListenForGameTelemetryAsync(
                state.Games,
                options.ListenGame,
                options.ListenSecondsSpecified || webDashboard == null
                    ? options.ListenSeconds
                    : 0,
                webDashboard))
        {
            return 5;
        }

        if (!string.IsNullOrWhiteSpace(options.PlayAudio) &&
            !await PlayAudioDiagnosticAsync(
                options.PlayAudio,
                options.AudioDevice,
                state.OutputDevices,
                audioPlayback))
        {
            return 6;
        }

        if (webDashboard != null && string.IsNullOrWhiteSpace(options.ListenGame))
        {
            Console.WriteLine("Web Dashboard is running. Press Ctrl+C to stop.");
            await WaitForCancellationAsync();
        }

        return 0;
    }

    private static WebDashboardRuntime? StartWebDashboard(
        LinuxHostOptions options,
        string resourceRoot)
    {
        if (!options.WebDashboard && !Config.Instance.WebDashboardEnabled)
        {
            return null;
        }

        var port = options.WebDashboardPort ?? Config.Instance.WebDashboardPort;
        var packagesPath = ResolveDashboardPackagesPath(resourceRoot);
        var runtime = new WebDashboardRuntime();
        try
        {
            runtime.Start(new WebDashboardRuntimeOptions(
                packagesPath,
                AppContext.BaseDirectory,
                port,
                Config.Instance.WebDashboardFrameRate,
                Config.Instance.WebDashboardBrowserLuaEnabled));
            Console.WriteLine(
                $"Web Dashboard: {runtime.Status.Url}; " +
                $"{runtime.Dashboards.Count} packages loaded");
            return runtime;
        }
        catch
        {
            runtime.Dispose();
            throw;
        }
    }

    private static string ResolveDashboardPackagesPath(string resourceRoot)
    {
        var packagedPath = Path.Combine(resourceRoot, Constants.PATH_DASHBOARDS);
        return Directory.Exists(packagedPath)
            ? packagedPath
            : Path.Combine(AppContext.BaseDirectory, Constants.PATH_DASHBOARDS);
    }

    private static async Task WaitForCancellationAsync()
    {
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        ConsoleCancelEventHandler handler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            completion.TrySetResult();
        };
        Console.CancelKeyPress += handler;
        try
        {
            await completion.Task;
        }
        finally
        {
            Console.CancelKeyPress -= handler;
        }
    }

    private static string FormatPrerequisiteStatus(ZtmzRuntimeState state, IGame game)
    {
        var status = state.Prerequisites.FirstOrDefault(candidate =>
            ReferenceEquals(candidate.Game, game));
        return status?.Result?.Code.ToString() ??
               $"unavailable ({status?.Error ?? "not checked"})";
    }

    private static async Task<bool> PlayAudioDiagnosticAsync(
        string audioPath,
        string? requestedDeviceId,
        IReadOnlyList<AudioOutputDeviceInfo> audioDevices,
        IAudioPlaybackEngineFactory audioPlayback)
    {
        try
        {
            audioPath = Path.GetFullPath(audioPath);
            if (!File.Exists(audioPath))
            {
                Console.Error.WriteLine($"Audio file not found: {audioPath}");
                return false;
            }

            var selectedDevice = string.IsNullOrWhiteSpace(requestedDeviceId)
                ? audioDevices.FirstOrDefault(device => device.IsDefault) ??
                  audioDevices.FirstOrDefault()
                : audioDevices.FirstOrDefault(device =>
                    string.Equals(
                        device.Id,
                        requestedDeviceId,
                        StringComparison.OrdinalIgnoreCase));
            if (selectedDevice == null)
            {
                Console.Error.WriteLine(
                    string.IsNullOrWhiteSpace(requestedDeviceId)
                        ? "No PortAudio output device is available."
                        : $"PortAudio output device not found: {requestedDeviceId}");
                return false;
            }

            Console.WriteLine($"Decoding audio: {audioPath}");
            var sound = new AutoResampledCachedSound(audioPath);
            Console.WriteLine(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Playing {0:F2} seconds through [{1}] {2}...",
                    sound.Duration,
                    selectedDevice.Id,
                    selectedDevice.Name));

            using var engine = audioPlayback.Create(
                new AudioPlaybackEngineOptions(
                    selectedDevice.Id,
                    int.TryParse(
                        selectedDevice.Id,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out var deviceIndex)
                        ? deviceIndex
                        : 0,
                    true,
                    Config.Instance.PlaybackDeviceDesiredLatency));
            engine.SetMuted(false);
            engine.PlaySound(sound, isSequential: false);
            await Task.Delay(TimeSpan.FromSeconds(sound.Duration + 0.5));
            if (!string.IsNullOrWhiteSpace(engine.Status.LastError))
            {
                Console.Error.WriteLine(
                    $"Audio output reported an error: {engine.Status.LastError}");
                return false;
            }

            Console.WriteLine("Audio playback diagnostic completed.");
            return true;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Audio playback diagnostic failed: {exception}");
            return false;
        }
    }

    private static async Task<bool> ListenForGameTelemetryAsync(
        IReadOnlyList<IGame> games,
        string gameName,
        int listenSeconds,
        WebDashboardRuntime? webDashboard)
    {
        var game = games.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, gameName, StringComparison.OrdinalIgnoreCase));
        if (game == null)
        {
            Console.Error.WriteLine($"Game provider not found: {gameName}");
            return false;
        }

        if (game.GameDataReader is not UdpGameDataReader udpReader)
        {
            Console.Error.WriteLine($"Game provider does not use UDP telemetry: {game.Name}");
            return false;
        }

        long sampleCount = 0;
        var sampleLock = new object();
        var latestSample = default(GameData);
        var dashboardContext = new GameContext();
        udpReader.onNewGameData += (_, current) =>
        {
            lock (sampleLock)
            {
                latestSample = current;
            }

            Interlocked.Increment(ref sampleCount);
            webDashboard?.PublishTelemetry(current, dashboardContext);
        };
        try
        {
            if (!udpReader.Initialize(game))
            {
                Console.Error.WriteLine($"UDP reader did not initialize for {game.Name}.");
                return false;
            }

            var listener = udpReader.GetUdpListenerDiagnostics().First();
            if (listenSeconds > 0)
            {
                Console.WriteLine(
                    $"Listening for {game.Name} telemetry on " +
                    $"{listener.Status.ListenAddress}:{listener.Status.Port} for {listenSeconds} seconds...");
                await Task.Delay(TimeSpan.FromSeconds(listenSeconds));
            }
            else
            {
                Console.WriteLine(
                    $"Listening for {game.Name} telemetry on " +
                    $"{listener.Status.ListenAddress}:{listener.Status.Port}. Press Ctrl+C to stop.");
                await WaitForCancellationAsync();
            }

            var finalStatus = udpReader.UdpStatus;
            Console.WriteLine(
                $"UDP telemetry result: {finalStatus.PacketCount} packets, " +
                $"{sampleCount} decoded samples, last packet {finalStatus.LastPacketSize} bytes");
            if (sampleCount > 0)
            {
                GameData sample;
                lock (sampleLock)
                {
                    sample = latestSample;
                }

                Console.WriteLine(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Latest decoded sample: speed {0:F1} km/h, gear {1:F0}, " +
                        "RPM {2:F0}, stage distance {3:F1} m",
                        sample.Speed,
                        sample.Gear,
                        sample.RPM,
                        sample.LapDistance));
            }

            return true;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"UDP listener failed for {game.Name}: {exception.Message}");
            return false;
        }
        finally
        {
            udpReader.Uninitialize(game);
        }
    }

    private static string FormatUdpConfiguration(IGame game)
    {
        return game.GameConfigurations.TryGetValue(UdpGameConfig.Name, out var configuration) &&
               configuration is UdpGameConfig udp
            ? $" (UDP {udp.IPAddress}:{udp.Port})"
            : string.Empty;
    }

    private static string ResolveResourceRoot(string? configuredRoot)
    {
        var root = configuredRoot;
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Environment.GetEnvironmentVariable(ResourceHomeEnvironmentVariable);
        }

        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(AppContext.BaseDirectory, "resources");
        }

        return Path.GetFullPath(root);
    }

    private static bool VerifyWritableDirectory(string directory)
    {
        Directory.CreateDirectory(directory);
        var probePath = Path.Combine(directory, $".write-probe-{Guid.NewGuid():N}");

        try
        {
            File.WriteAllText(probePath, string.Empty);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            try
            {
                File.Delete(probePath);
            }
            catch (IOException)
            {
                // The diagnostic result is already known; cleanup failure is non-fatal.
            }
            catch (UnauthorizedAccessException)
            {
                // The diagnostic result is already known; cleanup failure is non-fatal.
            }
        }
    }

    private static bool TryReadOptions(
        IReadOnlyList<string> args,
        out LinuxHostOptions options,
        out string? error)
    {
        string? dataRoot = null;
        string? resourceRoot = null;
        string? listenGame = null;
        string? playAudio = null;
        string? audioDevice = null;
        var webDashboard = false;
        int? webDashboardPort = null;
        var listenSeconds = 10;
        var listenSecondsSpecified = false;
        error = null;

        for (var index = 0; index < args.Count; index++)
        {
            var option = args[index];
            if (option == WebDashboardOption)
            {
                webDashboard = true;
                continue;
            }

            if (option is DataRootOption or ResourceRootOption or ListenGameOption or
                PlayAudioOption or AudioDeviceOption or WebDashboardPortOption)
            {
                if (++index >= args.Count || string.IsNullOrWhiteSpace(args[index]))
                {
                    options = default!;
                    error = $"{option} requires a value.";
                    return false;
                }

                if (option == DataRootOption)
                {
                    dataRoot = args[index];
                }
                else if (option == ResourceRootOption)
                {
                    resourceRoot = args[index];
                }
                else if (option == ListenGameOption)
                {
                    listenGame = args[index];
                }
                else if (option == PlayAudioOption)
                {
                    playAudio = args[index];
                }
                else if (option == WebDashboardPortOption)
                {
                    if (!int.TryParse(
                            args[index],
                            NumberStyles.None,
                            CultureInfo.InvariantCulture,
                            out var parsedPort) ||
                        parsedPort is < 1024 or > 65535)
                    {
                        options = default!;
                        error = $"{WebDashboardPortOption} requires a port from 1024 to 65535.";
                        return false;
                    }

                    webDashboardPort = parsedPort;
                    webDashboard = true;
                }
                else
                {
                    audioDevice = args[index];
                }

                continue;
            }

            if (option == ListenSecondsOption)
            {
                if (++index >= args.Count ||
                    !int.TryParse(args[index], NumberStyles.None, CultureInfo.InvariantCulture, out listenSeconds) ||
                    listenSeconds is < 1 or > 300)
                {
                    options = default!;
                    error = $"{ListenSecondsOption} requires an integer from 1 to 300.";
                    return false;
                }

                listenSecondsSpecified = true;
                continue;
            }

            options = default!;
            error = $"Unknown option: {option}";
            return false;
        }

        if (listenGame == null && listenSeconds != 10)
        {
            options = default!;
            error = $"{ListenSecondsOption} requires {ListenGameOption}.";
            return false;
        }

        if (playAudio == null && audioDevice != null)
        {
            options = default!;
            error = $"{AudioDeviceOption} requires {PlayAudioOption}.";
            return false;
        }

        options = new LinuxHostOptions(
            dataRoot,
            resourceRoot,
            listenGame,
            listenSeconds,
            listenSecondsSpecified,
            playAudio,
            audioDevice,
            webDashboard,
            webDashboardPort);
        return true;
    }

    private static string FormatAvailability(bool available) => available ? "yes" : "no";

    private static void PrintUsage()
    {
        Console.WriteLine(
            "Usage: ztmz [--data-root <directory>] [--resource-root <directory>] " +
            "[--listen-game <name> [--listen-seconds <1-300>]] " +
            "[--play-audio <file> [--audio-device <id>]] " +
            "[--web-dashboard [--web-dashboard-port <1024-65535>]]");
        Console.WriteLine();
        Console.WriteLine("Loads packaged pacenote data and native game providers on Linux.");
        Console.WriteLine(
            $"Without {DataRootOption}, data is stored under $XDG_DATA_HOME/ztmz " +
            "or ~/.local/share/ztmz.");
    }

    private sealed record LinuxHostOptions(
        string? DataRoot,
        string? ResourceRoot,
        string? ListenGame,
        int ListenSeconds,
        bool ListenSecondsSpecified,
        string? PlayAudio,
        string? AudioDevice,
        bool WebDashboard,
        int? WebDashboardPort);
}
