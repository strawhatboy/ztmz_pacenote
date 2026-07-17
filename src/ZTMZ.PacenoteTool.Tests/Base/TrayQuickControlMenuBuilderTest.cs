using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.Tests.Base;

public class TrayQuickControlMenuBuilderTest
{
    [Fact]
    public void BuildSortsAllPackagesAndChecksTheSelectedOne()
    {
        var state = TrayQuickControlMenuBuilder.Build(
            new[]
            {
                Package("zulu", "Zulu"),
                Package("alpha", "Alpha"),
                Package("bravo", "Bravo")
            },
            "id:zulu",
            isStageActive: false,
            isMuted: false,
            isNativeHudVisible: true,
            isWebDashboardEnabled: true);

        Assert.Equal(new[] { "Alpha", "Bravo", "Zulu" }, state.Codrivers.Select(option => option.Package.name));
        var selected = Assert.Single(state.Codrivers, option => option.IsSelected);
        Assert.Equal("Zulu", selected.Package.name);
        Assert.True(state.CanSelectCodriver);
        Assert.True(state.CanPlayExample);
        Assert.True(state.IsNativeHudVisible);
        Assert.True(state.IsWebDashboardEnabled);
    }

    [Fact]
    public void BuildDisablesSelectionAndPreviewDuringAnActiveStage()
    {
        var state = TrayQuickControlMenuBuilder.Build(
            new[] { Package("alpha", "Alpha") },
            "id:alpha",
            isStageActive: true,
            isMuted: false,
            isNativeHudVisible: true,
            isWebDashboardEnabled: false);

        Assert.False(state.CanSelectCodriver);
        Assert.False(state.CanPlayExample);
    }

    [Fact]
    public void BuildDisablesPreviewWhileMutedButKeepsSelectionAvailable()
    {
        var state = TrayQuickControlMenuBuilder.Build(
            new[] { Package("alpha", "Alpha") },
            "id:alpha",
            isStageActive: false,
            isMuted: true,
            isNativeHudVisible: false,
            isWebDashboardEnabled: false);

        Assert.True(state.CanSelectCodriver);
        Assert.False(state.CanPlayExample);
        Assert.True(state.IsMuted);
    }

    [Fact]
    public void BuildDisablesAudioActionsWhenNoPackagesAreInstalled()
    {
        var state = TrayQuickControlMenuBuilder.Build(
            Array.Empty<CoDriverPackageInfo>(),
            string.Empty,
            isStageActive: false,
            isMuted: false,
            isNativeHudVisible: false,
            isWebDashboardEnabled: false);

        Assert.Empty(state.Codrivers);
        Assert.False(state.CanSelectCodriver);
        Assert.False(state.CanPlayExample);
    }

    private static CoDriverPackageInfo Package(string id, string name)
    {
        return new CoDriverPackageInfo
        {
            id = id,
            name = name,
            language = "en",
            version = "1.0",
            Path = $"C:\\codrivers\\{name}"
        };
    }
}
