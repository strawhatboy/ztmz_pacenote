using ZTMZ.PacenoteTool.RBR;

namespace ZTMZ.PacenoteTool.Tests.RBR;

public sealed class RBRPacenoteSelectionTest : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ztmz-selection-" + Guid.NewGuid().ToString("N"));
    private string Stage => Path.Combine(root, "Notes", "Stage");

    public RBRPacenoteSelectionTest()
    {
        Directory.CreateDirectory(Path.Combine(root, "Plugins"));
    }

    private void Configure(string path = "Notes") => File.WriteAllText(Path.Combine(root, "Plugins", "NGPCarMenu.ini"),
        "[Default]\nMyPacenotesPath=" + path);
    private RBRPacenoteSelection Resolve() => RBRPacenoteSelectionResolver.Resolve(root, "Stage");

    [Fact]
    public void DistinguishesMissingConfigDisabledPathAndMissingStage()
    {
        Assert.Equal(RBRPacenoteSelectionStatus.NotConfigured, Resolve().Status);
        Configure("\"DiSaBlEd\"");
        Assert.Equal(RBRPacenoteSelectionStatus.Disabled, Resolve().Status);
        Configure();
        Assert.Equal(RBRPacenoteSelectionStatus.DirectoryMissing, Resolve().Status);
        Directory.CreateDirectory(Path.Combine(root, "Notes"));
        Assert.Equal(RBRPacenoteSelectionStatus.StageMissing, Resolve().Status);
        Directory.CreateDirectory(Stage);
        Assert.Equal(RBRPacenoteSelectionStatus.Unknown, Resolve().Status);
    }

    [Fact]
    public void SelectedCustomFileWinsOverNewerDefaultAndUnrelatedReadme()
    {
        Configure(); Directory.CreateDirectory(Stage);
        File.WriteAllText(Path.Combine(Stage, "readme.txt"), "not a selection");
        File.WriteAllText(Path.Combine(Stage, "MYPACENOTE_Stage LuppisV3.TXT"), "");
        var selected = Path.Combine(Stage, "Stage LuppisV3.ini");
        File.WriteAllText(selected, "[P0]\ntype=21\nflag=0\ndistance=0");
        File.SetLastWriteTimeUtc(selected, DateTime.UtcNow.AddDays(-1));
        File.WriteAllText(Path.Combine(Stage, "Stage_default.ini"), "");
        var selection = Resolve();
        Assert.Equal(RBRPacenoteSelectionStatus.CustomSelected, selection.Status);
        Assert.Equal(selected, selection.FilePath);
        Assert.True(selection.UseCustomDefinition); // no high custom IDs required
        File.Delete(selected);
        Assert.Equal(RBRPacenoteSelectionStatus.Unknown, Resolve().Status);
        Assert.False(Resolve().UseCustomDefinition);
    }

    [Fact]
    public void MissingSectionAndMissingKeyAreNotConfigurationErrors()
    {
        var config = Path.Combine(root, "Plugins", "NGPCarMenu.ini");
        File.WriteAllText(config, "[Other]\nValue=1");
        Assert.Equal(RBRPacenoteSelectionStatus.NotConfigured, Resolve().Status);
        File.WriteAllText(config, "[Default]\nOther=1");
        Assert.Equal(RBRPacenoteSelectionStatus.NotConfigured, Resolve().Status);
    }

    [Fact]
    public void PublicReaderUsesCustomSelectionEvenWithoutTracksIniOrHighIds()
    {
        Configure(); Directory.CreateDirectory(Stage);
        File.WriteAllText(Path.Combine(Stage, "mypacenote_Stage_LuppisV3.txt"), "");
        File.WriteAllText(Path.Combine(Stage, "Stage_LuppisV3.ini"), "[P0]\ntype=21\nflag=0\ndistance=0");
        var exe = Path.Combine(root, "RichardBurnsRally_SSE.exe");
        File.WriteAllText(exe, "");
        var reader = new RBRGamePacenoteReader();
        reader.Configure(new RBRGameInstallationConfig { ExecutablePath = exe });
        Assert.True(reader.IsUsingCustomPacenote("Stage"));
        Configure("disabled");
        Assert.False(reader.IsUsingCustomPacenote("Stage"));
    }

    [Fact]
    public void ExplicitDefaultFileUsesDefaultDefinitionAndInformationalNotice()
    {
        Configure(); Directory.CreateDirectory(Stage);
        var path = Path.Combine(Stage, "Stage_DEFAULT.ini");
        File.WriteAllText(path, "");
        File.WriteAllText(Path.Combine(Stage, "mypacenote_Stage_DEFAULT.txt"), "");
        var selection = Resolve();
        Assert.Equal(path, selection.FilePath);
        Assert.Equal(RBRPacenoteSelectionStatus.DefaultSelected, selection.Status);
        Assert.False(selection.UseCustomDefinition);
        Assert.Equal(0, selection.MessageLevel);
    }

    [Fact]
    public void DefaultAndConflictingMarkersDoNotClaimModIsMissing()
    {
        Configure(); Directory.CreateDirectory(Stage);
        File.WriteAllText(Path.Combine(Stage, "mypacenote_default.txt"), "");
        Assert.Equal(RBRPacenoteSelectionStatus.DefaultSelected, Resolve().Status);
        Assert.False(Resolve().UseCustomDefinition);
        File.WriteAllText(Path.Combine(Stage, "mypacenote_latest.txt"), "");
        Assert.Equal(RBRPacenoteSelectionStatus.Unknown, Resolve().Status);
    }

    [Fact]
    public void LatestSelectionHandlesMissingFilesDefaultCustomAndTies()
    {
        Configure(); Directory.CreateDirectory(Stage);
        File.WriteAllText(Path.Combine(Stage, "mypacenote_latest.txt"), "");
        Assert.Equal(RBRPacenoteSelectionStatus.Unknown, Resolve().Status);
        var standard = Path.Combine(Stage, "Stage_default.ini");
        var custom = Path.Combine(Stage, "Stage_LuppisV3.ini");
        File.WriteAllText(standard, ""); File.WriteAllText(custom, "");
        var time = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(standard, time);
        File.SetLastWriteTimeUtc(custom, time.AddMinutes(1));
        Assert.Equal(RBRPacenoteSelectionStatus.CustomSelected, Resolve().Status);
        File.SetLastWriteTimeUtc(standard, time.AddMinutes(2));
        Assert.Equal(RBRPacenoteSelectionStatus.DefaultSelected, Resolve().Status);
        File.SetLastWriteTimeUtc(custom, time.AddMinutes(2));
        var selection = Resolve();
        Assert.Equal(RBRPacenoteSelectionStatus.Unknown, selection.Status);
        var legacyFile = Directory.GetFiles(Stage, "*.ini").OrderByDescending(File.GetLastWriteTimeUtc).First();
        Assert.Equal(legacyFile, selection.FilePath);
    }

    [Fact]
    public void AbsolutePathAndBtbStageAreSupportedAndSelectionIsNotCached()
    {
        Configure('"' + Path.Combine(root, "Notes") + '"');
        Directory.CreateDirectory(Stage + " BTB");
        File.WriteAllText(Path.Combine(Stage + " BTB", "mypacenote_default.txt"), "");
        Assert.Equal(RBRPacenoteSelectionStatus.DefaultSelected, Resolve().Status);
        Configure("disabled");
        Assert.Equal(RBRPacenoteSelectionStatus.Disabled, Resolve().Status);
    }

    public void Dispose() => Directory.Delete(root, true);
}
