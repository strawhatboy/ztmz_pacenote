using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.RBR;

namespace ZTMZ.PacenoteTool.Tests.RBR;

public class RBRGameInstallPathResolverTest
{
    [Theory]
    [InlineData("RichardBurnsRally_SSE.exe")]
    [InlineData("RichardBurnsRally.exe")]
    public void ConfiguredRbrExecutableResolvesItsPortableInstallationRoot(string executableName)
    {
        using var installation = new TemporaryRbrInstallation(executableName);
        var configuration = new RBRGameInstallationConfig { ExecutablePath = installation.ExecutablePath };

        var resolved = RBRGameInstallPathResolver.TryResolveRoot(
            configuration,
            out var rootDirectory,
            out var source);

        Assert.True(resolved);
        Assert.Equal(RBRInstallationPathSource.ConfiguredExecutable, source);
        Assert.True(string.Equals(installation.RootDirectory, rootDirectory, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UnrelatedExecutableIsNotAcceptedAsAnRbrInstallation()
    {
        using var installation = new TemporaryRbrInstallation("not-rbr.exe");

        var resolved = RBRGameInstallPathResolver.TryGetConfiguredRoot(
            installation.ExecutablePath,
            out _);

        Assert.False(resolved);
    }

    [Fact]
    public void PrerequisiteCheckUsesConfiguredPortableInstallationWithoutRegistry()
    {
        using var installation = new TemporaryRbrInstallation("RichardBurnsRally_SSE.exe");
        File.WriteAllText(Path.Combine(installation.RootDirectory, "RichardBurnsRally.ini"),
            "[NGP]\r\nudpTelemetry=1\r\nudpTelemetryEndpoints=127.0.0.1:30001\r\n");
        File.WriteAllText(Path.Combine(installation.RootDirectory, "Maps", "Tracks.ini"), "[General]\r\n");
        var game = new TestGame(installation.ExecutablePath);

        var result = new RBRGamePrerequisiteChecker().CheckPrerequisites(game);

        Assert.Equal(PrerequisitesCheckResultCode.OK, result.Code);
    }

    private sealed class TemporaryRbrInstallation : IDisposable
    {
        public TemporaryRbrInstallation(string executableName)
        {
            RootDirectory = Path.Combine(Path.GetTempPath(), $"ztmz-rbr-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path.Combine(RootDirectory, "Maps"));
            ExecutablePath = Path.Combine(RootDirectory, executableName);
            File.WriteAllText(ExecutablePath, string.Empty);
        }

        public string RootDirectory { get; }

        public string ExecutablePath { get; }

        public void Dispose()
        {
            if (Directory.Exists(RootDirectory))
            {
                Directory.Delete(RootDirectory, recursive: true);
            }
        }
    }

    private sealed class TestGame : IGame
    {
        public TestGame(string executablePath)
        {
            GameConfigurations = new Dictionary<string, IGameConfig>
            {
                [RBRGameInstallationConfig.Name] = new RBRGameInstallationConfig { ExecutablePath = executablePath },
                [UdpGameConfig.Name] = new UdpGameConfig { IPAddress = "127.0.0.1", Port = 30001 }
            };
        }

        public string WindowTitle => string.Empty;
        public string Name => RBR.GameName;
        public string Description => string.Empty;
        public string ImageUri => string.Empty;
        public string Executable => "RichardBurnsRally_SSE";
        public IGamePacenoteReader GamePacenoteReader => null!;
        public IGameDataReader GameDataReader => null!;
        public IGamePrerequisiteChecker GamePrerequisiteChecker => null!;
        public Dictionary<string, IGameConfig> GameConfigurations { get; set; }
        public Dictionary<string, IGameConfig> DefaultGameConfigurations { get; } = new();
        public event Action<int, string>? onCustomMessage;
        public bool IsRunning { get; set; }
        public bool IsInitialized { get; set; }
        public int Order => 0;

        public void OnCustomMessage(int level, string message)
        {
            onCustomMessage?.Invoke(level, message);
        }
    }
}
