using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.Codemasters;
using Xunit;

namespace ZTMZ.PacenoteTool.Portable.Tests;

public sealed class SteamGameDocumentsLocatorTest
{
    [Fact]
    public void ProtonLocator_FindsGameInConfiguredSteamLibrary()
    {
        using var directory = new TemporaryDirectory();
        var steamRoot = Path.Combine(directory.Path, "Steam");
        var gameLibrary = Path.Combine(directory.Path, "External Library");
        WriteLibraryFolders(steamRoot, gameLibrary);
        var documents = CreateInstalledProtonGame(gameLibrary, DirtRally2.SteamAppId);

        var locator = new ProtonGameDocumentsLocator(directory.Path, [steamRoot]);

        Assert.Equal(documents, Assert.Single(locator.FindDocumentsDirectories(DirtRally2.SteamAppId)));
    }

    [Fact]
    public void ProtonLocator_FindsFlatpakSteamDefaultLocation()
    {
        using var directory = new TemporaryDirectory();
        var steamRoot = Path.Combine(
            directory.Path,
            ".var",
            "app",
            "com.valvesoftware.Steam",
            ".local",
            "share",
            "Steam");
        var documents = CreateInstalledProtonGame(steamRoot, DirtRally2.SteamAppId);

        var locator = new ProtonGameDocumentsLocator(directory.Path);

        Assert.Equal(documents, Assert.Single(locator.FindDocumentsDirectories(DirtRally2.SteamAppId)));
    }

    [Fact]
    public void ProtonLocator_DoesNotUseStalePrefixWhenInstalledLibraryIsKnown()
    {
        using var directory = new TemporaryDirectory();
        var staleLibrary = Path.Combine(directory.Path, "Stale");
        var installedLibrary = Path.Combine(directory.Path, "Installed");
        Directory.CreateDirectory(staleLibrary);
        Directory.CreateDirectory(installedLibrary);
        CreateProtonDocuments(staleLibrary, DirtRally2.SteamAppId);
        var expected = CreateInstalledProtonGame(installedLibrary, DirtRally2.SteamAppId);

        var locator = new ProtonGameDocumentsLocator(
            directory.Path,
            [staleLibrary, installedLibrary]);

        Assert.Equal(expected, Assert.Single(locator.FindDocumentsDirectories(DirtRally2.SteamAppId)));
    }

    [Fact]
    public void DirtPrerequisiteChecker_UsesLocatedProtonDocuments()
    {
        using var directory = new TemporaryDirectory();
        var documents = Path.Combine(directory.Path, "Proton Documents");
        var settingsDirectory = Path.Combine(
            documents,
            "My Games",
            "DiRT Rally 2.0",
            "hardwaresettings");
        Directory.CreateDirectory(settingsDirectory);
        File.WriteAllText(
            Path.Combine(settingsDirectory, "hardware_settings_config.xml"),
            """
            <hardware_settings_config>
              <motion_platform>
                <udp enabled="true" extradata="3" ip="127.0.0.1" port="30000" delay="1" />
              </motion_platform>
            </hardware_settings_config>
            """);
        var checker = new DirtGamePrerequisiteChecker(
            new FixedGameDocumentsLocator(documents));

        var result = checker.CheckPrerequisites(new DirtRally2());

        Assert.True(result.IsOK);
        Assert.Equal(PrerequisitesCheckResultCode.OK, result.Code);
    }

    [Fact]
    public void WrcPrerequisiteChecker_UsesLocatedProtonDocuments()
    {
        using var directory = new TemporaryDirectory();
        var telemetryDirectory = CreateWrcTelemetryDirectory(directory.Path);
        File.WriteAllText(
            Path.Combine(telemetryDirectory, "config.json"),
            """
            {
              "udp": {
                "packets": [
                  {
                    "structure": "ztmz",
                    "packet": "session_update",
                    "port": 30002
                  }
                ]
              }
            }
            """);
        var channelDirectory = Path.Combine(telemetryDirectory, "udp");
        Directory.CreateDirectory(channelDirectory);
        File.WriteAllText(
            Path.Combine(channelDirectory, "ztmz.json"),
            """
            {
              "versions": {
                "schema": 1,
                "data": 8
              }
            }
            """);
        var checker = new WRCGamePrerequisiteChecker(
            new FixedGameDocumentsLocator(directory.Path));

        var result = checker.CheckPrerequisites(new WRC());

        Assert.Equal(PrerequisitesCheckResultCode.OK, result.Code);
    }

    [Fact]
    public void WrcPrerequisiteChecker_ReportsMissingChannelAsRepairable()
    {
        using var directory = new TemporaryDirectory();
        var telemetryDirectory = CreateWrcTelemetryDirectory(directory.Path);
        File.WriteAllText(
            Path.Combine(telemetryDirectory, "config.json"),
            """
            {
              "udp": {
                "packets": [
                  {
                    "structure": "ztmz",
                    "packet": "session_update",
                    "port": 30002
                  }
                ]
              }
            }
            """);
        var checker = new WRCGamePrerequisiteChecker(
            new FixedGameDocumentsLocator(directory.Path));

        var result = checker.CheckPrerequisites(new WRC());

        Assert.Equal(PrerequisitesCheckResultCode.PORT_NOT_OPEN, result.Code);
    }

    private static void WriteLibraryFolders(string steamRoot, string library)
    {
        var steamApps = Path.Combine(steamRoot, "steamapps");
        Directory.CreateDirectory(steamApps);
        var escapedPath = library.Replace("\\", "\\\\", StringComparison.Ordinal);
        File.WriteAllText(
            Path.Combine(steamApps, "libraryfolders.vdf"),
            $$"""
            "libraryfolders"
            {
                "1"
                {
                    "path" "{{escapedPath}}"
                    "apps"
                    {
                        "{{DirtRally2.SteamAppId}}" "1"
                    }
                }
            }
            """);
    }

    private static string CreateInstalledProtonGame(string library, uint appId)
    {
        var steamApps = Path.Combine(library, "steamapps");
        Directory.CreateDirectory(steamApps);
        File.WriteAllText(Path.Combine(steamApps, $"appmanifest_{appId}.acf"), string.Empty);
        return CreateProtonDocuments(library, appId);
    }

    private static string CreateWrcTelemetryDirectory(string documents)
    {
        var telemetryDirectory = Path.Combine(
            documents,
            "My Games",
            "WRC",
            "telemetry");
        Directory.CreateDirectory(telemetryDirectory);
        return telemetryDirectory;
    }

    private static string CreateProtonDocuments(string library, uint appId)
    {
        var documents = Path.Combine(
            library,
            "steamapps",
            "compatdata",
            appId.ToString(),
            "pfx",
            "drive_c",
            "users",
            "steamuser",
            "Documents");
        Directory.CreateDirectory(documents);
        return documents;
    }

    private sealed class FixedGameDocumentsLocator(string documents) : IGameDocumentsLocator
    {
        public IReadOnlyList<string> FindDocumentsDirectories(uint steamAppId)
        {
            return [documents];
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"ztmz-steam-path-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
