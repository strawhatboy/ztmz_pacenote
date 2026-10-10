using Xunit;
using ZTMZ.PacenoteTool.KT;

public sealed class Wrc7PacenoteCatalogTests
{
    private const string Notes = """
        PaceNotes = {
          --[[
          COMMENTED = { DetailedSound = "PN_Left4", AccessibleSound = "PN_Left4" },
          ]]--
          LEFT_4 = { DetailedSound = "PN_Left4", AccessibleSound = "PN_Left3" },
          LEFT_5 = { DetailedSound = "PN_Left5", AccessibleSound = "PN_Left4" },
          LEFT_6 = { DetailedSound = "PN_Left6", AccessibleSound = "PN_Left4" },
          EMPTY = { DetailedSound = "", AccessibleSound = "" },
        }
        AdditionalInformation = {
          VISUAL = { DetailedSound = "PN_Left4", AccessibleSound = "PN_Left4" },
        }
        """;

    [Fact]
    public void PreservesAccessibleAmbiguityWithoutChangingDetailedGrade()
    {
        var catalog = Wrc7PacenoteCatalog.Parse(Notes);
        Assert.Equal(4, catalog.AtomCount);
        Assert.Equal(new[] { "LEFT_4" }, catalog.Candidates("MALE/PN_Left4", false));
        Assert.Equal(new[] { "LEFT_5", "LEFT_6" }, catalog.Candidates("FEMALE/PN_Left4", true));
        Assert.Empty(catalog.Candidates("", false));
        Assert.Empty(catalog.Candidates("Unknown", true));
    }

    [Fact]
    public void DoesNotTreatCommentMarkersInsideStringsAsComments()
    {
        var catalog = Wrc7PacenoteCatalog.Parse(Notes.Replace("PN_Left5", "PN_--Left5"));
        Assert.Equal(new[] { "LEFT_5" }, catalog.Candidates("PN_--Left5", false));
    }

    [Fact]
    public void ResolvesRecordedMergedPathsUsingGameDefinitions()
    {
        var notes = Notes.Replace("EMPTY =", "_into = { DetailedSound = \"PN_Into\", AccessibleSound = \"PN_Into\" },\n  EMPTY =") + """

            PaceNotesMerging = {
              PN_Into = {
                PN_Left4 = "PN_IntoLeft4",
              },
            }
            """;
        var catalog = Wrc7PacenoteCatalog.Parse(notes);
        var detailed = catalog.ResolveGroups("DATA:/SOUNDS/VOICE/EN/PACENOTES/MALE/PN_INTOLEFT4.PAF", false);
        Assert.Equal(2, detailed.Count);
        Assert.Equal(new[] { "_into" }, detailed[0]);
        Assert.Equal(new[] { "LEFT_4" }, detailed[1]);
        var accessible = catalog.ResolveGroups("FEMALE/PN_INTOLEFT4.PAF", true);
        Assert.Equal(new[] { "LEFT_5", "LEFT_6" }, accessible[1]);
        Assert.Empty(catalog.ResolveGroups("PN_IntoMadeUp.PAF", false));
    }
}
