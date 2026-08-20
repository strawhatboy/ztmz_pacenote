using Xunit;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.Portable.Tests;

public sealed class BuiltInGameSettingsProvidersTest
{
    [Fact]
    public void UdpProviderDescribesAndUpdatesBothFields()
    {
        var configuration = new UdpGameConfig { IPAddress = "127.0.0.1", Port = 20777 };
        var provider = Assert.IsAssignableFrom<IGameSettingsProvider>(
            BuiltInGameSettingsProviders.Find(configuration));

        var fields = provider.GetFields(configuration);
        Assert.Collection(fields,
            address =>
            {
                Assert.Equal("address", address.Id);
                Assert.Equal(GameSettingFieldKind.Text, address.Kind);
                Assert.True(address.RequiresRestart);
            },
            port =>
            {
                Assert.Equal("port", port.Id);
                Assert.Equal(1, port.Minimum);
                Assert.Equal(65535, port.Maximum);
            });

        provider.SetFieldValue(configuration, "address", "0.0.0.0", null!);
        provider.SetFieldValue(configuration, "port", 30001d, null!);

        Assert.Equal("0.0.0.0", configuration.IPAddress);
        Assert.Equal(30001, configuration.Port);
        Assert.True(Config.Instance.WarnIfPortMismatch);
    }

    [Fact]
    public void MemoryProviderPreservesFloatConfigurationType()
    {
        var configuration = new MemoryGameConfig { RefreshRate = 20f };
        var provider = Assert.IsAssignableFrom<IGameSettingsProvider>(
            BuiltInGameSettingsProviders.Find(configuration));

        provider.SetFieldValue(configuration, "refresh-rate", 59.5d, null!);

        Assert.Equal(59.5f, configuration.RefreshRate);
    }

    [Fact]
    public void CommonProviderBuildsDynamicMetadataAndPreservesValueTypes()
    {
        var configuration = new CommonGameConfigs
        {
            PropertyName = new Dictionary<string, string>
            {
                ["bool.label"] = "bool.tip",
                ["number.label"] = "number.tip",
                ["file.label"] = "file.tip"
            },
            PropertyValue = [true, 12, "old.zdb"],
            PropertyType = ["bool", "number", "file:zdb"],
            ValueRange = [[], [1, 30], []]
        };
        var provider = Assert.IsAssignableFrom<IGameSettingsProvider>(
            BuiltInGameSettingsProviders.Find(configuration));

        var fields = provider.GetFields(configuration);

        Assert.Equal(GameSettingFieldKind.Boolean, fields[0].Kind);
        Assert.Equal(GameSettingFieldKind.Number, fields[1].Kind);
        Assert.Equal(1, fields[1].Minimum);
        Assert.Equal(30, fields[1].Maximum);
        Assert.Equal(GameSettingFieldKind.FilePath, fields[2].Kind);
        Assert.Equal("zdb", fields[2].FileExtension);

        provider.SetFieldValue(configuration, "1", 18m, null!);
        provider.SetFieldValue(configuration, "2", "new.zdb", null!);

        Assert.IsType<int>(configuration.PropertyValue[1]);
        Assert.Equal(18, configuration.PropertyValue[1]);
        Assert.Equal("new.zdb", configuration.PropertyValue[2]);
    }
}
