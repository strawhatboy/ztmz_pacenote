using System.Text.Json;
using System.Reflection;
using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.Tests.Base;

public class ConfigConcurrencyTest
{
    [Fact]
    public async Task SerializeUserConfigSnapshotAllowsConcurrentSettingChanges()
    {
        var config = (Config)Activator.CreateInstance(typeof(Config), nonPublic: true)!;
        var serializeSnapshot = typeof(Config).GetMethod(
            "SerializeUserConfigSnapshot",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        var operations = Enumerable.Range(0, 64)
            .Select(index => Task.Run(() =>
            {
                config.UI_SelectedAudioPackage = index;
                config.UI_SelectedAudioPackageIdentity = $"id:codriver-{index}";
                config.UI_Mute = index % 2 == 0;
                return (string)serializeSnapshot.Invoke(config, null)!;
            }));

        var serializedSnapshots = await Task.WhenAll(operations);
        foreach (var serializedSnapshot in serializedSnapshots)
        {
            var savedValues = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(serializedSnapshot);
            Assert.NotNull(savedValues);
            Assert.True(savedValues.ContainsKey(nameof(Config.UI_SelectedAudioPackage)));
            Assert.True(savedValues.ContainsKey(nameof(Config.UI_SelectedAudioPackageIdentity)));
            Assert.True(savedValues.ContainsKey(nameof(Config.UI_Mute)));
        }
    }
}
