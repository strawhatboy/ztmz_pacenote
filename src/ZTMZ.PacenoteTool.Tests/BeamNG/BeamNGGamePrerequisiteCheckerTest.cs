using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using Xunit;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.BeamNG;

namespace ZTMZ.PacenoteTool.Tests.BeamNG;

public class BeamNGGamePrerequisiteCheckerTest
{
    [Fact]
    public void Check_RequiresTheBridgeForBeamNG()
    {
        using var userFolder = new TemporaryDirectory();
        var game = CreateGame(userFolder.Path);

        var result = new BeamNGGamePrerequisiteChecker().CheckPrerequisites(game);

        Assert.False(result.IsOK);
        Assert.Equal(PrerequisitesCheckResultCode.SETUP_REQUIRED, result.Code);
        Assert.Equal(BeamNGGamePrerequisiteChecker.MissingBridgeReason, Assert.Single(result.Params));
    }

    [Fact]
    public void Check_RequiresAValidUserFolderForBeamNG()
    {
        var game = CreateGame(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

        var result = new BeamNGGamePrerequisiteChecker().CheckPrerequisites(game);

        Assert.False(result.IsOK);
        Assert.Equal(PrerequisitesCheckResultCode.SETUP_REQUIRED, result.Code);
        Assert.Equal(BeamNGGamePrerequisiteChecker.MissingUserFolderReason, Assert.Single(result.Params));
    }

    [Fact]
    public void ForceFix_InstallsTheBridgeIntoTheConfiguredUserFolder()
    {
        using var userFolder = new TemporaryDirectory();
        var game = CreateGame(userFolder.Path);
        var config = Assert.IsType<BeamNGRallyBridgeConfig>(
            game.GameConfigurations[BeamNGRallyBridgeConfig.Name]);

        new BeamNGGamePrerequisiteChecker().ForceFix(game);

        Assert.True(BeamNGBridgeInstaller.IsInstalled(config));
        var installedModPath = Path.Combine(userFolder.Path, "mods", BeamNGBridgeInstaller.ModFileName);
        Assert.True(File.Exists(installedModPath));

        using var archive = ZipFile.OpenRead(installedModPath);
        Assert.All(archive.Entries, entry => Assert.DoesNotContain('\\', entry.FullName));
        Assert.NotNull(archive.GetEntry("scripts/ztmz_pacenote_rally_bridge/modScript.lua"));
        Assert.NotNull(archive.GetEntry("scripts/ztmz_pacenote_rally_bridge/extension.lua"));
        Assert.NotNull(archive.GetEntry("lua/vehicle/extensions/ztmz/pacenote/rally/bridge/damage.lua"));
        Assert.NotNull(archive.GetEntry("lua/vehicle/extensions/ztmz/pacenote/rally/bridge/telemetry.lua"));
        Assert.Null(archive.GetEntry("lua/ge/extensions/core/input/actions/ztmz_pacenote_rally_bridge.json"));
        Assert.Null(archive.GetEntry("mod_info.json"));

        var metadataEntry = archive.GetEntry($"mod_info/{BeamNGBridgeInstaller.ModMetadataId}/info.json");
        Assert.NotNull(metadataEntry);
        using var metadataReader = new StreamReader(metadataEntry!.Open());
        using var metadataDocument = JsonDocument.Parse(metadataReader.ReadToEnd());
        var metadata = metadataDocument.RootElement;
        Assert.Equal("ZTMZ Pacenote Rally Bridge", metadata.GetProperty("title").GetString());
        Assert.Equal("草帽不是猫 with Codex", metadata.GetProperty("username").GetString());
        Assert.True(Version.TryParse(metadata.GetProperty("version_string").GetString(), out _));
        Assert.Equal(
            BeamNGBridgeInstallationState.Current,
            BeamNGBridgeInstaller.GetInstallationInfo(config).State);
    }

    [Fact]
    public void Check_RequiresAnUpdateWhenInstalledBridgeZipIsOlder()
    {
        using var userFolder = new TemporaryDirectory();
        var game = CreateGame(userFolder.Path);
        var config = Assert.IsType<BeamNGRallyBridgeConfig>(
            game.GameConfigurations[BeamNGRallyBridgeConfig.Name]);
        CreateInstalledBridgeArchive(userFolder.Path, "1.0.0");

        var result = new BeamNGGamePrerequisiteChecker().CheckPrerequisites(game);

        Assert.False(result.IsOK);
        Assert.Equal(PrerequisitesCheckResultCode.SETUP_REQUIRED, result.Code);
        Assert.Equal(BeamNGGamePrerequisiteChecker.OutdatedBridgeReason, Assert.Single(result.Params));
        Assert.Equal(
            BeamNGBridgeInstallationState.Outdated,
            BeamNGBridgeInstaller.GetInstallationInfo(config).State);

        new BeamNGGamePrerequisiteChecker().ForceFix(game);

        Assert.Equal(
            BeamNGBridgeInstallationState.Current,
            BeamNGBridgeInstaller.GetInstallationInfo(config).State);
    }

    [Fact]
    public void InstallationInfo_AcceptsACurrentUnpackedBridge()
    {
        using var userFolder = new TemporaryDirectory();
        var config = new BeamNGRallyBridgeConfig { UserFolderPath = userFolder.Path };
        var bundledVersion = GetBundledBridgeVersion();
        var metadataPath = Path.Combine(
            userFolder.Path,
            "mods",
            "unpacked",
            "ztmz_pacenote_rally_bridge",
            "mod_info",
            BeamNGBridgeInstaller.ModMetadataId,
            "info.json");
        Directory.CreateDirectory(Path.GetDirectoryName(metadataPath)!);
        File.WriteAllText(metadataPath, CreateMetadata(bundledVersion));

        var installation = BeamNGBridgeInstaller.GetInstallationInfo(config);

        Assert.Equal(BeamNGBridgeInstallationState.Current, installation.State);
        Assert.Equal(bundledVersion, installation.InstalledVersion?.ToString());
    }

    [Fact]
    public void InstallationInfo_AcceptsWindowsPathSeparatorsInBridgeArchive()
    {
        using var userFolder = new TemporaryDirectory();
        var config = new BeamNGRallyBridgeConfig { UserFolderPath = userFolder.Path };
        var bundledVersion = GetBundledBridgeVersion();
        CreateInstalledBridgeArchive(userFolder.Path, bundledVersion, useWindowsPathSeparators: true);

        var installation = BeamNGBridgeInstaller.GetInstallationInfo(config);

        Assert.Equal(BeamNGBridgeInstallationState.Current, installation.State);
        Assert.Equal(bundledVersion, installation.InstalledVersion?.ToString());
    }

    [Fact]
    public void UserFolderLocator_RejectsBeamNGRetiredRedirectFolder()
    {
        using var userFolder = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(userFolder.Path, "moved here.lnk"), "obsolete redirect");

        Assert.False(BeamNGUserFolderLocator.IsValidUserFolder(userFolder.Path));
    }

    private static TestGame CreateGame(string userFolderPath)
    {
        return new TestGame
        {
            GameConfigurations = new Dictionary<string, IGameConfig>
            {
                [BeamNGRallyBridgeConfig.Name] = new BeamNGRallyBridgeConfig
                {
                    UserFolderPath = userFolderPath
                }
            }
        };
    }

    private static void CreateInstalledBridgeArchive(
        string userFolderPath,
        string version,
        bool useWindowsPathSeparators = false)
    {
        var modsDirectory = Path.Combine(userFolderPath, "mods");
        Directory.CreateDirectory(modsDirectory);
        var archivePath = Path.Combine(modsDirectory, BeamNGBridgeInstaller.ModFileName);
        using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
        var separator = useWindowsPathSeparators ? "\\" : "/";
        var metadataEntry = archive.CreateEntry($"mod_info{separator}{BeamNGBridgeInstaller.ModMetadataId}{separator}info.json");
        using var writer = new StreamWriter(metadataEntry.Open());
        writer.Write(CreateMetadata(version));
    }

    private static string GetBundledBridgeVersion()
    {
        var assemblyDirectory = Path.GetDirectoryName(typeof(BeamNGBridgeInstaller).Assembly.Location);
        Assert.False(string.IsNullOrWhiteSpace(assemblyDirectory));
        var metadataPath = Path.Combine(
            assemblyDirectory!,
            "BeamNGMod",
            "mod_info",
            BeamNGBridgeInstaller.ModMetadataId,
            "info.json");
        using var document = JsonDocument.Parse(File.ReadAllText(metadataPath));
        return document.RootElement.GetProperty("version_string").GetString()!;
    }

    private static string CreateMetadata(string version)
    {
        return JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["tagid"] = BeamNGBridgeInstaller.ModMetadataId,
            ["version_string"] = version
        });
    }

    private sealed class TestGame : IGame
    {
        public string WindowTitle => string.Empty;
        public string Name => BeamNGRally.GameName;
        public string Description => string.Empty;
        public string ImageUri => string.Empty;
        public string Executable => string.Empty;
        public IGamePacenoteReader GamePacenoteReader => null!;
        public IGameDataReader GameDataReader { get; } = new BeamNGGameDataReader();
        public IGamePrerequisiteChecker GamePrerequisiteChecker => null!;
        public Dictionary<string, IGameConfig> GameConfigurations { get; set; } = new();
        public Dictionary<string, IGameConfig> DefaultGameConfigurations => GameConfigurations;
        public event Action<int, string>? onCustomMessage;
        public bool IsRunning { get; set; }
        public bool IsInitialized { get; set; }
        public int Order => 0;

        public void OnCustomMessage(int level, string message)
        {
            onCustomMessage?.Invoke(level, message);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "ZTMZPacenoteBeamNGTests",
            Guid.NewGuid().ToString("N"));

        public TemporaryDirectory()
        {
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
