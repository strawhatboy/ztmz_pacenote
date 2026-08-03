using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
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
        Assert.NotNull(archive.GetEntry("scripts/ztmz_pacenote_rally_bridge/modScript.lua"));
        Assert.NotNull(archive.GetEntry("scripts/ztmz_pacenote_rally_bridge/extension.lua"));
        Assert.NotNull(archive.GetEntry("lua/vehicle/extensions/ztmz_pacenote_rally_bridge_damage.lua"));
        Assert.Null(archive.GetEntry("lua/ge/extensions/core/input/actions/ztmz_pacenote_rally_bridge.json"));
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
