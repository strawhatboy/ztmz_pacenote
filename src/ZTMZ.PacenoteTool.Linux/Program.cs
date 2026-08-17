using System.Globalization;
using System.Runtime.InteropServices;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.Core;
using PacenoteDatabase = ZTMZ.PacenoteTool.Base.Script.ScriptResource;

namespace ZTMZ.PacenoteTool.Linux;

public static class Program
{
    private const string DataRootOption = "--data-root";
    private const string ResourceRootOption = "--resource-root";
    private const string ListenGameOption = "--listen-game";
    private const string ListenSecondsOption = "--listen-seconds";
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
        Directory.CreateDirectory(paths.GetPath(Constants.PATH_GAMES));
        I18NLoader.Instance.Initialize(
        [
            Path.Combine(resourceRoot, Constants.PATH_LANGUAGE),
            paths.GetPath(Constants.PATH_LANGUAGE)
        ]);

        var dataLoaded = TryProvisionAndLoadPacenoteData(
            resourceRoot,
            paths,
            out var provisioningStatus,
            out var pacenoteCount,
            out var dataError);

        var gamesDirectory = Path.Combine(AppContext.BaseDirectory, Constants.PATH_GAMES);
        var games = LoadGames(gamesDirectory);

        Console.WriteLine("ZTMZ native Linux host bootstrap");
        Console.WriteLine($"OS: {RuntimeInformation.OSDescription}");
        Console.WriteLine($"Architecture: {RuntimeInformation.OSArchitecture}");
        Console.WriteLine($"Data root: {dataRoot}");
        Console.WriteLine($"Data root writable: {FormatAvailability(dataRootWritable)}");
        Console.WriteLine($"Resource root: {resourceRoot}");
        Console.WriteLine(
            dataLoaded
                ? $"Official pacenote data: {provisioningStatus}; {pacenoteCount} tokens loaded"
                : $"Official pacenote data: unavailable ({dataError})");
        Console.WriteLine($"Game plugin directory: {gamesDirectory}");
        Console.WriteLine($"Game providers loaded: {games.Count}");
        foreach (var game in games)
        {
            Console.WriteLine($"  - {game.Name}{FormatUdpConfiguration(game)}");
        }
        Console.WriteLine("Linux audio output: unavailable (backend not implemented)");
        Console.WriteLine("Native overlay: unavailable on Linux");

        if (!dataRootWritable)
        {
            return 2;
        }

        if (!dataLoaded)
        {
            return 3;
        }

        if (games.Count == 0)
        {
            return 4;
        }

        if (!string.IsNullOrWhiteSpace(options.ListenGame) &&
            !await ListenForGameTelemetryAsync(games, options.ListenGame, options.ListenSeconds))
        {
            return 5;
        }

        return 0;
    }

    private static bool TryProvisionAndLoadPacenoteData(
        string resourceRoot,
        IApplicationPaths paths,
        out string provisioningStatus,
        out int pacenoteCount,
        out string error)
    {
        provisioningStatus = string.Empty;
        pacenoteCount = 0;
        error = string.Empty;

        try
        {
            var result = PackagedFileProvisioner.EnsureCurrent(
                Path.Combine(resourceRoot, PacenoteDatabase.DBNAME),
                paths.GetPath(PacenoteDatabase.DBNAME));
            provisioningStatus = result.Status.ToString().ToLowerInvariant();
            PacenoteDatabase.Instance.LoadData().GetAwaiter().GetResult();
            pacenoteCount = PacenoteDatabase.Instance.Pacenotes.Count;
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
    }

    private static IReadOnlyList<IGame> LoadGames(string gamesDirectory)
    {
        if (!Directory.Exists(gamesDirectory))
        {
            return [];
        }

        var games = GamePluginLoader.Load(
                Directory.EnumerateFiles(gamesDirectory, "*.dll"),
                NLog.LogManager.GetCurrentClassLogger())
            .OrderBy(game => game.Order)
            .ThenBy(game => game.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var game in games)
        {
            game.GameConfigurations = Config.Instance.LoadGameConfig(game);
        }

        return games;
    }

    private static async Task<bool> ListenForGameTelemetryAsync(
        IReadOnlyList<IGame> games,
        string gameName,
        int listenSeconds)
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
        udpReader.onNewGameData += (_, _) => Interlocked.Increment(ref sampleCount);
        try
        {
            if (!udpReader.Initialize(game))
            {
                Console.Error.WriteLine($"UDP reader did not initialize for {game.Name}.");
                return false;
            }

            var listener = udpReader.GetUdpListenerDiagnostics().First();
            Console.WriteLine(
                $"Listening for {game.Name} telemetry on " +
                $"{listener.Status.ListenAddress}:{listener.Status.Port} for {listenSeconds} seconds...");
            await Task.Delay(TimeSpan.FromSeconds(listenSeconds));

            var finalStatus = udpReader.UdpStatus;
            Console.WriteLine(
                $"UDP telemetry result: {finalStatus.PacketCount} packets, " +
                $"{sampleCount} decoded samples, last packet {finalStatus.LastPacketSize} bytes");
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
        var listenSeconds = 10;
        error = null;

        for (var index = 0; index < args.Count; index++)
        {
            var option = args[index];
            if (option is DataRootOption or ResourceRootOption or ListenGameOption)
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
                else
                {
                    listenGame = args[index];
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

        options = new LinuxHostOptions(dataRoot, resourceRoot, listenGame, listenSeconds);
        return true;
    }

    private static string FormatAvailability(bool available) => available ? "yes" : "no";

    private static void PrintUsage()
    {
        Console.WriteLine(
            "Usage: ztmz [--data-root <directory>] [--resource-root <directory>] " +
            "[--listen-game <name> [--listen-seconds <1-300>]]");
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
        int ListenSeconds);
}
