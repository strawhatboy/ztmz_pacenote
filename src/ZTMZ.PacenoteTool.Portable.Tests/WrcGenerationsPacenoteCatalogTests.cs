using Xunit;
using ZTMZ.PacenoteTool.KT;

public class WrcGenerationsPacenoteCatalogTests
{
    private const string Notes = """
        PaceNotes = {
          LEFT_3 = { DetailedSound = "Atom_LEFT_3", AccessibleSound = "Atom_LEFT_3" },
          tightens = { DetailedSound = "Atom_tightens" },
          --unused = { DetailedSound = "Atom_unused" },
        }
        AdditionalInformation = {
          tightens = { DetailedSound = "" },
        }
        """;
    private const string Blocks = """
        PaceNoteBlocks = {
          Block_0000 = { "LEFT_3", "tightens", "LEFT_3" },
        }
        """;

    [Fact]
    public void PreservesOrderAndRepetitionAndSeparatesVisualDefinitions()
    {
        var catalog = WrcGenerationsPacenoteCatalog.Parse(Notes, Blocks);
        Assert.Equal(2, catalog.AtomCount);
        Assert.Equal(1, catalog.BlockCount);
        Assert.True(catalog.TryResolve("MALE/Block_0000", out var tokens));
        Assert.Equal(new[] { "LEFT_3", "tightens", "LEFT_3" }, tokens);
        Assert.True(catalog.TryResolve("FEMALE/Atom_tightens", out tokens));
        Assert.Equal(new[] { "tightens" }, tokens);
    }

    [Theory]
    [InlineData("MALE/IMMERSION_START_02")]
    [InlineData("MALE/Block_9999")]
    [InlineData("MALE/Atom_left_3")]
    public void UnknownResourcesAreNotGuessed(string name)
    {
        Assert.False(WrcGenerationsPacenoteCatalog.Parse(Notes, Blocks).TryResolve(name, out var tokens));
        Assert.Empty(tokens);
    }

    [Fact]
    public void RejectsUndefinedBlockAtoms() => Assert.Throws<InvalidDataException>(() =>
        WrcGenerationsPacenoteCatalog.Parse(Notes, Blocks.Replace("LEFT_3", "UNKNOWN")));

    [Fact]
    public void RejectsAmbiguousSoundNames() => Assert.Throws<InvalidDataException>(() =>
        WrcGenerationsPacenoteCatalog.Parse(Notes.Replace("Atom_tightens", "Atom_LEFT_3"), Blocks));
}
