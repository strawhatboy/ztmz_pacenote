using System;
using System.Collections.Generic;
using System.IO;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.Codemasters;

namespace ZTMZ.PacenoteTool.Tests.Codemasters;

public class DirtGamePrerequisiteCheckerTest
{
    [Fact]
    public void Check_AcceptsMatchingEntryWhenOtherToolsAddPartialOrDuplicateEntries()
    {
        var result = CheckXml("""
            <hardware_settings_config>
              <motion_platform>
                <udp enabled="true" port="20777" />
                <udp enabled="true" extradata="3" ip="127.0.0.1" port="20777" delay="1" />
                <udp enabled="true" extradata="3" ip="127.0.0.1" port="20777" delay="1" />
                <custom_udp enabled="true" port="20777" />
                <udp enabled="true" extradata="3" ip="127.0.0.1" port="59996" delay="1" />
              </motion_platform>
            </hardware_settings_config>
            """);

        Assert.True(result.IsOK);
        Assert.Equal(PrerequisitesCheckResultCode.OK, result.Code);
    }

    [Fact]
    public void Check_AcceptsNamespacedXmlAndEquivalentBooleanFormatting()
    {
        var result = CheckXml("""
            <hardware_settings_config xmlns="urn:codemasters:hardware-settings">
              <motion_platform>
                <udp enabled="True" extradata="3" ip="127.0.0.1" port=" 59996 " delay="1" />
              </motion_platform>
            </hardware_settings_config>
            """);

        Assert.True(result.IsOK);
        Assert.Equal(PrerequisitesCheckResultCode.OK, result.Code);
    }

    [Fact]
    public void Check_ReportsMissingConfiguredPortWithoutTreatingOtherTargetsAsAnError()
    {
        var result = CheckXml("""
            <hardware_settings_config>
              <motion_platform>
                <udp enabled="true" extradata="3" ip="127.0.0.1" port="20777" delay="1" />
              </motion_platform>
            </hardware_settings_config>
            """);

        Assert.False(result.IsOK);
        Assert.Equal(PrerequisitesCheckResultCode.PORT_NOT_OPEN, result.Code);
    }

    [Fact]
    public void Check_ReportsTrulyMalformedXmlAsCorrupted()
    {
        var result = CheckXml("<hardware_settings_config><motion_platform>");

        Assert.False(result.IsOK);
        Assert.Equal(PrerequisitesCheckResultCode.CONFIG_FILE_CORRUPTED, result.Code);
    }

    private static PrerequisitesCheckResult CheckXml(string xml)
    {
        var file = Path.Combine(Path.GetTempPath(), $"ztmz-dirt-prerequisite-{Guid.NewGuid():N}.xml");

        try
        {
            File.WriteAllText(file, xml);
            return new DirtGamePrerequisiteChecker().Check(new TestGame(), file);
        }
        finally
        {
            File.Delete(file);
        }
    }

    private sealed class TestGame : IGame
    {
        public string WindowTitle => string.Empty;
        public string Name => DirtRally.GameName;
        public string Description => string.Empty;
        public string ImageUri => string.Empty;
        public string Executable => string.Empty;
        public IGamePacenoteReader GamePacenoteReader => null!;
        public IGameDataReader GameDataReader => null!;
        public IGamePrerequisiteChecker GamePrerequisiteChecker => null!;
        public Dictionary<string, IGameConfig> GameConfigurations { get; set; } = new()
        {
            [UdpGameConfig.Name] = new UdpGameConfig
            {
                IPAddress = "127.0.0.1",
                Port = 59996
            }
        };
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
}
