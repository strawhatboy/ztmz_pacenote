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

    [Fact]
    public void CommonConfigMergeRepairsStaleParallelListsBySettingKey()
    {
        var stale = new CommonGameConfigs
        {
            PropertyName = new Dictionary<string, string>
            {
                ["setting.enabled"] = "old.enabled.tooltip",
                ["setting.database"] = "old.database.tooltip"
            },
            PropertyValue = [false, "custom.zdb"],
            PropertyType = [null!],
            ValueRange = []
        };
        var defaults = new CommonGameConfigs
        {
            PropertyName = new Dictionary<string, string>
            {
                ["setting.enabled"] = "new.enabled.tooltip",
                ["setting.database"] = "new.database.tooltip",
                ["setting.auto"] = "new.auto.tooltip"
            },
            PropertyValue = [true, "default.zdb", true],
            PropertyType = ["bool", "file:zdb", "bool"],
            ValueRange = [[], [], []]
        };

        var changed = stale.Merge(defaults, isOverride: false);

        Assert.True(changed);
        Assert.Equal([false, "custom.zdb", true], stale.PropertyValue);
        Assert.Equal(["bool", "file:zdb", "bool"], stale.PropertyType);
        Assert.Equal(3, stale.ValueRange.Count);
        Assert.Equal("new.database.tooltip", stale.PropertyName["setting.database"]);
        Assert.False(stale.Merge(defaults, isOverride: false));

        var provider = Assert.IsAssignableFrom<IGameSettingsProvider>(
            BuiltInGameSettingsProviders.Find(stale));
        Assert.Equal(GameSettingFieldKind.FilePath, provider.GetFields(stale)[1].Kind);
    }

    [Fact]
    public void CommonConfigOverridePreservesCurrentSchemaMetadata()
    {
        var defaults = new CommonGameConfigs
        {
            PropertyName = new Dictionary<string, string> { ["setting.level"] = "new.tooltip" },
            PropertyValue = [5],
            PropertyType = ["number"],
            ValueRange = [[1, 10]]
        };
        var user = new CommonGameConfigs
        {
            PropertyName = new Dictionary<string, string> { ["setting.level"] = "old.tooltip" },
            PropertyValue = [8],
            PropertyType = ["text"],
            ValueRange = [[0, 100]]
        };

        defaults.Merge(user);

        Assert.Equal(8, defaults.PropertyValue[0]);
        Assert.Equal("new.tooltip", defaults.PropertyName["setting.level"]);
        Assert.Equal("number", defaults.PropertyType[0]);
        Assert.Equal([1d, 10d], defaults.ValueRange[0]);
    }

    [Fact]
    public void CommonProviderToleratesNullLegacyMetadata()
    {
        var configuration = new CommonGameConfigs
        {
            PropertyName = new Dictionary<string, string> { ["setting.path"] = "setting.path.tooltip" },
            PropertyValue = ["legacy-value"],
            PropertyType = [null!],
            ValueRange = [null!]
        };
        var provider = Assert.IsAssignableFrom<IGameSettingsProvider>(
            BuiltInGameSettingsProviders.Find(configuration));

        var field = Assert.Single(provider.GetFields(configuration));

        Assert.Equal(GameSettingFieldKind.Text, field.Kind);
        Assert.Null(field.Minimum);
        Assert.Null(field.Maximum);
    }
}
