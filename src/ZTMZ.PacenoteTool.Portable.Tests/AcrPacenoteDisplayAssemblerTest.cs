using ZTMZ.PacenoteTool.AC;
using Xunit;

namespace ZTMZ.PacenoteTool.Portable.Tests;

public sealed class AcrPacenoteDisplayAssemblerTest
{
    [Fact]
    public void AccumulatesOnePhraseWithoutChangingAudioAndStartsNextRow()
    {
        var assembler = new ACRPacenoteDisplayAssembler();
        var start = assembler.Append(ACRPacenoteTokenTranslator.Translate(["Junction"]), "Start");
        var fragment = ACRPacenoteTokenTranslator.Translate(["GoRight"]);
        var mid = assembler.Append(fragment, "Mid");
        var end = assembler.Append(ACRPacenoteTokenTranslator.Translate(["Dist100"]), "End");
        Assert.Equal(start.GroupId, mid.GroupId);
        Assert.Equal(start.GroupId, end.GroupId);
        Assert.Equal(new[] { "junction", "right", "100" }, end.Record.Pacenotes.Select(n => n.Note));
        Assert.Single(start.Record.Pacenotes);
        Assert.Equal("right", Assert.Single(fragment.Pacenotes).Note);
        var next = assembler.Append(ACRPacenoteTokenTranslator.Translate(["Left3"]), "Start");
        Assert.NotEqual(end.GroupId, next.GroupId);
        Assert.Single(next.Record.Pacenotes);
    }

    [Fact]
    public void AnyAndMissingStartDoNotLeakPreviousPhrase()
    {
        var assembler = new ACRPacenoteDisplayAssembler();
        var first = assembler.Append(ACRPacenoteTokenTranslator.Translate(["Left3"]), "Mid");
        var any = assembler.Append(ACRPacenoteTokenTranslator.Translate(["Dist40"]), "Any");
        var end = assembler.Append(ACRPacenoteTokenTranslator.Translate(["Right2"]), "End");
        Assert.NotEqual(first.GroupId, any.GroupId);
        Assert.NotEqual(any.GroupId, end.GroupId);
        Assert.Single(any.Record.Pacenotes);
        Assert.Single(end.Record.Pacenotes);
    }
}
