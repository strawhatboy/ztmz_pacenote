using System.Text.Json.Nodes;
using Xunit;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.Portable.Tests;

public sealed class GameConfigUpgradeTest
{
    [Fact]
    public void MissingConfigFileIsCreatedFromCurrentDefaults()
    {
        WithTemporaryDirectory((game, path) =>
        {
            var writes = 0;
            void WriteFile(string destination, string content)
            {
                writes++;
                File.WriteAllText(destination, content);
            }

            var loaded = Config.LoadGameConfigFromPath(game, path, WriteFile);

            Assert.Same(game.GameConfigurations, loaded);
            Assert.Equal(1, writes);
            Assert.True(File.Exists(path));

            var persisted = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            Assert.True(persisted.ContainsKey(UdpGameConfig.Name));
            Assert.True(persisted.ContainsKey(MemoryGameConfig.Name));
            Assert.True(persisted.ContainsKey(CommonGameConfigs.Name));
        });
    }

    [Fact]
    public void HistoricalRbrConfigIsRepairedPersistedAndThenRemainsStable()
    {
        WithTemporaryConfig(HistoricalRbrConfig, (game, path) =>
        {
            var writes = 0;
            void WriteFile(string destination, string content)
            {
                writes++;
                File.WriteAllText(destination, content);
            }

            var loaded = Config.LoadGameConfigFromPath(game, path, WriteFile);

            var common = Assert.IsType<CommonGameConfigs>(loaded[CommonGameConfigs.Name]);
            Assert.Equal(4, common.PropertyName.Count);
            Assert.Equal([true, "D:\\RBR\\jannemod_v3.zdb", true, false], common.PropertyValue);
            Assert.Equal(["bool", "file:zdb", "bool", "bool"], common.PropertyType);
            Assert.Equal(4, common.ValueRange.Count);
            Assert.IsType<MemoryGameConfig>(loaded[MemoryGameConfig.Name]);
            Assert.Equal(1, writes);

            var persisted = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            var persistedCommon = persisted[CommonGameConfigs.Name]!.AsObject();
            Assert.Equal(4, persistedCommon["PropertyValue"]!.AsArray().Count);
            Assert.Equal(4, persistedCommon["PropertyType"]!.AsArray().Count);
            Assert.Equal(4, persistedCommon["ValueRange"]!.AsArray().Count);

            Config.LoadGameConfigFromPath(game, path, WriteFile);

            Assert.Equal(1, writes);
        });
    }

    [Fact]
    public void NullLegacyCollectionsAreRestoredFromCurrentDefaults()
    {
        const string json = """
        {
          "common_config": {
            "PropertyName": null,
            "PropertyValue": null,
            "PropertyType": null,
            "ValueRange": null
          }
        }
        """;

        WithTemporaryConfig(json, (game, path) =>
        {
            var loaded = Config.LoadGameConfigFromPath(game, path);
            var common = Assert.IsType<CommonGameConfigs>(loaded[CommonGameConfigs.Name]);

            Assert.Equal(game.CurrentCommon.PropertyName, common.PropertyName);
            Assert.Equal(game.CurrentCommon.PropertyValue, common.PropertyValue);
            Assert.Equal(game.CurrentCommon.PropertyType, common.PropertyType);
            Assert.Equal(4, common.ValueRange.Count);
        });
    }

    [Fact]
    public void MisalignedLegacyListsPreserveKnownValuesAndUnknownSettings()
    {
        const string json = """
        {
          "common_config": {
            "PropertyName": {
              "game.rbr.additional_settings.playPacenotesWhenReplaying": "old.tooltip",
              "third.party.setting": "third.party.tooltip"
            },
            "PropertyValue": [false],
            "PropertyType": ["bool", null, "orphaned-type"],
            "ValueRange": []
          }
        }
        """;

        WithTemporaryConfig(json, (game, path) =>
        {
            var loaded = Config.LoadGameConfigFromPath(game, path);
            var common = Assert.IsType<CommonGameConfigs>(loaded[CommonGameConfigs.Name]);

            Assert.Equal(5, common.PropertyName.Count);
            Assert.Equal(5, common.PropertyValue.Count);
            Assert.Equal(5, common.PropertyType.Count);
            Assert.Equal(5, common.ValueRange.Count);
            Assert.False(Assert.IsType<bool>(common.PropertyValue[0]));
            Assert.Null(common.PropertyValue[1]);
            Assert.Contains("third.party.setting", common.PropertyName.Keys);

            var provider = Assert.IsAssignableFrom<IGameSettingsProvider>(
                BuiltInGameSettingsProviders.Find(common));
            Assert.Equal(5, provider.GetFields(common).Count);
        });
    }

    [Fact]
    public void RepairStillReturnsUsableConfigurationWhenPersistenceFails()
    {
        WithTemporaryConfig(HistoricalRbrConfig, (game, path) =>
        {
            var loaded = Config.LoadGameConfigFromPath(
                game,
                path,
                (_, _) => throw new IOException("Simulated locked config file."));

            var common = Assert.IsType<CommonGameConfigs>(loaded[CommonGameConfigs.Name]);
            Assert.Equal(4, common.PropertyValue.Count);
            Assert.False((bool)common.PropertyValue[3]);
        });
    }

    private static void WithTemporaryConfig(string content, Action<TestGame, string> test)
    {
        WithTemporaryDirectory((game, path) =>
        {
            File.WriteAllText(path, content);
            test(game, path);
        });
    }

    private static void WithTemporaryDirectory(Action<TestGame, string> test)
    {
        var directory = Path.Combine(Path.GetTempPath(), "ztmz-game-config-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "Richard Burns Rally - RSF.json");

        try
        {
            test(new TestGame(), path);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private const string HistoricalRbrConfig = """
    {
      "udp": {
        "IPAddress": "127.0.0.1",
        "Port": 30001
      },
      "common_config": {
        "PropertyName": {
          "game.rbr.additional_settings.playPacenotesWhenReplaying": "game.rbr.additional_settings.tooltip.playPacenotesWhenReplaying",
          "game.rbr.additional_settings.additional_pacenote_def": "game.rbr.additional_settings.tooltip.additional_pacenote_def",
          "game.rbr.additional_settings.autoSwitchPacenoteDef": "game.rbr.additional_settings.tooltip.autoSwitchPacenoteDef"
        },
        "PropertyValue": [
          true,
          "D:\\RBR\\jannemod_v3.zdb",
          true
        ],
        "ValueRange": [],
        "PropertyType": [
          "bool",
          "file:zdb",
          "bool"
        ]
      }
    }
    """;

    private sealed class TestGame : IGame
    {
        public TestGame()
        {
            CurrentCommon = new CommonGameConfigs
            {
                PropertyName = new Dictionary<string, string>
                {
                    ["game.rbr.additional_settings.playPacenotesWhenReplaying"] = "current.replay.tooltip",
                    ["game.rbr.additional_settings.additional_pacenote_def"] = "current.database.tooltip",
                    ["game.rbr.additional_settings.autoSwitchPacenoteDef"] = "current.auto.tooltip",
                    ["game.rbr.additional_settings.useInstalledCodriverAudio"] = "current.audio.tooltip"
                },
                PropertyValue = [true, "default.zdb", true, false],
                PropertyType = ["bool", "file:zdb", "bool", "bool"]
            };
            GameConfigurations = new Dictionary<string, IGameConfig>
            {
                [UdpGameConfig.Name] = new UdpGameConfig { IPAddress = "127.0.0.1", Port = 30001 },
                [MemoryGameConfig.Name] = new MemoryGameConfig { RefreshRate = 60 },
                [CommonGameConfigs.Name] = CurrentCommon
            };
        }

        public CommonGameConfigs CurrentCommon { get; }
        public string WindowTitle => "Test";
        public string Name => "Richard Burns Rally - RSF";
        public string Description => string.Empty;
        public string ImageUri => string.Empty;
        public string Executable => "RichardBurnsRally_SSE";
        public IGamePacenoteReader GamePacenoteReader => null!;
        public IGameDataReader GameDataReader => null!;
        public IGamePrerequisiteChecker GamePrerequisiteChecker => null!;
        public Dictionary<string, IGameConfig> GameConfigurations { get; set; }
        public Dictionary<string, IGameConfig> DefaultGameConfigurations => GameConfigurations;
        public event Action<int, string>? onCustomMessage;
        public bool IsRunning { get; set; }
        public bool IsInitialized { get; set; }
        public int Order => 0;

        public void OnCustomMessage(int level, string message) => onCustomMessage?.Invoke(level, message);
    }
}
