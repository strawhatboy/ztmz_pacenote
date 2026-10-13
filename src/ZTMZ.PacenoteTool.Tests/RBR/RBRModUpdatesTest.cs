using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.RBR;

namespace ZTMZ.PacenoteTool.Tests.RBR;

public sealed class RBRModUpdatesTest : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ztmz-mod-updates-{Guid.NewGuid():N}");
    private readonly GameModDefinition _mod = new(
        "test", new Dictionary<string, string> { ["en-us"] = "Test" },
        new Dictionary<string, string>(), "Author", "1.10",
        new Uri("https://example.com/mod.zip"), new Uri("https://example.com/releases"),
        new string('A', 64), "", ExternalFolderName: "Test v1.10", ExternalFolderPrefix: "Test v");

    public RBRModUpdatesTest() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData("0.6.9", "0.6.10")]
    [InlineData("2026.8.27+1.069", "2026.10.1+1.069")]
    [InlineData("2026.8.9+1.069", "2026.8.27+1.069")]
    [InlineData("2026.8.27+1.069", "2026.8.27+1.070")]
    [InlineData("2026.8.27+1.9", "2026.8.27+1.10")]
    public void FullVersionsCompareNumerically(string oldVersion, string newVersion)
    {
        Assert.True(RBRModUpdateDetection.CompareVersions(oldVersion, newVersion) < 0);
        Assert.True(RBRModUpdateDetection.CompareVersions(newVersion, oldVersion) > 0);
    }

    [Theory]
    [InlineData("Test v1.9", true)]
    [InlineData("Test v1.10", false)]
    [InlineData("Test v1.11", false)]
    [InlineData("Test vcustom", false)]
    [InlineData("Test v1.9 backup", false)]
    [InlineData("Unrelated v1.9", false)]
    public void ExternalVersionsRequireRecognizableFolderNames(string folder, bool expected) =>
        Assert.Equal(expected, RBRModUpdateDetection.IsOlderExternalFolder(_mod, folder));

    [Fact]
    public void LuppisFolderComparisonIncludesDatesAndAudioRevision()
    {
        var mod = _mod with
        {
            ExternalFolderPrefix = "Luppis-v3-",
            ExternalFolderName = "Luppis-v3-2026.10.1（修订至2026.9.16）-老王中文语音1.070"
        };
        Assert.True(RBRModUpdateDetection.IsOlderExternalFolder(mod,
            "Luppis-v3-2026.8.27（修订至2026.8.16）-老王中文语音1.069"));
    }

    [Fact]
    public void FreshCatalogEntryIsNotAnInstalledUpdate()
    {
        var status = Status();
        Assert.Equal(GameModInstallState.NotInstalled, status.State);
        Assert.False(status.IsUpdateAvailable);
    }

    [Fact]
    public void ManagedUpdateClearsOnlyAfterNewPackageIsInstalled()
    {
        Install(_mod with { Version = "1.9", Sha256 = new string('B', 64) });
        Assert.True(Status().IsUpdateAvailable);
        Assert.True(Status().IsUpdateAvailable); // Viewing/rechecking does not acknowledge an update.
        Assert.Contains(GameModActionKind.Update, Status().EnabledActions!);
        Install(_mod);
        Assert.False(Status().IsUpdateAvailable);
        Assert.DoesNotContain(GameModActionKind.Update, Status().EnabledActions!);
    }

    [Fact]
    public void SameVersionReplacementIsAnUpdateButNewerInstalledVersionIsNot()
    {
        Install(_mod with { Sha256 = new string('B', 64) });
        Assert.True(Status().IsUpdateAvailable);
        Install(_mod with { Version = "1.11", Sha256 = new string('C', 64) });
        Assert.False(Status().IsUpdateAvailable);
    }

    [Fact]
    public void ModifiedFilesKeepUpdateNoticeWithoutAllowingUnsafeOverwrite()
    {
        Install(_mod with { Version = "1.9" });
        File.WriteAllText(Path.Combine(_root, "Plugins", "Test.dll"), "user modified");
        var status = Status();
        Assert.Equal(GameModInstallState.Conflict, status.State);
        Assert.True(status.IsUpdateAvailable);
        Assert.DoesNotContain(GameModActionKind.Update, status.EnabledActions!);
    }

    [Fact]
    public void RunningGameKeepsNoticeButDisablesChanges()
    {
        Install(_mod with { Version = "1.9" });
        var status = RBRModManager.GetStatus(_root, _mod, [_mod], running: true);
        Assert.True(status.IsUpdateAvailable);
        Assert.Equal(GameModActionKind.OpenReleasePage, Assert.Single(status.EnabledActions!));
    }

    [Fact]
    public void JsgmeUpdateRequiresDisableThenStageAndEnable()
    {
        CreateJsgme("Test v1.9");
        SetEnabled("Test v1.9");
        var status = Status();
        Assert.True(status.IsUpdateAvailable);
        Assert.Equal(GameModInstallState.ExternalManagerEnabled, status.State);
        Assert.DoesNotContain(GameModActionKind.Update, status.EnabledActions!);
        Assert.DoesNotContain(GameModActionKind.AddToExternalManager, status.EnabledActions!);

        SetEnabled(null);
        status = Status();
        Assert.True(status.IsUpdateAvailable);
        Assert.Contains(GameModActionKind.AddToExternalManager, status.EnabledActions!);
        Assert.DoesNotContain(GameModActionKind.Install, status.EnabledActions!);

        Assert.True(RBRJsgmeRepositoryLocator.TryLocate(_root, out var repository));
        RBRModPackageInstaller.AddToJsgme(CreatePackage(), repository, _mod);
        Assert.False(Status().IsUpdateAvailable);
        Assert.True(Directory.Exists(Path.Combine(_root, "MODS", "Test v1.9")));
        SetEnabled("Test v1.10");
        Assert.False(Status().IsUpdateAvailable);
        SetEnabled("Test v1.9"); // Having the new package staged does not update the active copy.
        Assert.True(Status().IsUpdateAvailable);
    }

    [Fact]
    public void NewerJsgmeVersionDoesNotOfferADowngrade()
    {
        CreateJsgme("Test v1.11");
        SetEnabled("Test v1.11");
        Assert.False(Status().IsUpdateAvailable);
    }

    private GameModStatus Status() => RBRModManager.GetStatus(_root, _mod, [_mod], running: false);
    private void Install(GameModDefinition mod) => RBRModPackageInstaller.InstallManaged(CreatePackage(), _root, mod);
    private string CreatePackage()
    {
        var package = Path.Combine(_root, "package");
        Directory.CreateDirectory(Path.Combine(package, "Plugins"));
        File.WriteAllText(Path.Combine(package, "Plugins", "Test.dll"), "binary");
        return package;
    }
    private void CreateJsgme(string folder)
    {
        File.WriteAllText(Path.Combine(_root, "JSGME.exe"), "");
        Directory.CreateDirectory(Path.Combine(_root, "MODS", folder));
    }
    private void SetEnabled(string? folder) => File.WriteAllText(Path.Combine(_root, "MODS", "JSGME.ini"),
        "[MODS]\r\n" + (folder is null ? "" : folder + "=1\r\n"));
    public void Dispose() => Directory.Delete(_root, recursive: true);
}
