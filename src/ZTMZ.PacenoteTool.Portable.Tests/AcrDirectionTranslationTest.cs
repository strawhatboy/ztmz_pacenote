using ZTMZ.PacenoteTool.AC;
using Xunit;

namespace ZTMZ.PacenoteTool.Portable.Tests;

public sealed class AcrDirectionTranslationTest
{
    [Theory]
    [InlineData("GoRight", "right")]
    [InlineData("GoLeft", "left")]
    public void JunctionDirectionDoesNotEmitRaceStartToken(string source, string direction)
    {
        var record = ACRPacenoteTokenTranslator.Translate(["Junction", source, "Dist100"]);
        Assert.Equal(new[] { "junction", direction, "100" }, record.Pacenotes.Select(note => note.Note));
    }

    [Theory]
    [InlineData("Junction-GoRight", "right")]
    [InlineData("Junction-GoLeft", "left")]
    public void CompoundDirectionUsesSameMapping(string source, string direction)
    {
        var record = ACRPacenoteTokenTranslator.Translate([source]);
        Assert.Equal(new[] { "junction", direction }, record.Pacenotes.Select(note => note.Note));
    }

    [Fact]
    public void GoStraightRetainsItsDirectionToken()
    {
        var record = ACRPacenoteTokenTranslator.Translate(["GoStraight"]);
        Assert.Equal("go_straight", Assert.Single(record.Pacenotes).Note);
    }
}
