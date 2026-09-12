using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.AC;

internal sealed class ACRPacenoteDisplayAssembler
{
    private readonly List<Pacenote> _notes = new();
    private string? _group;

    internal (PacenoteRecord Record, string GroupId) Append(PacenoteRecord fragment, string? placement)
    {
        var continuation = placement is "Mid" or "End";
        if (!continuation || _group == null)
        {
            _notes.Clear();
            _group = Guid.NewGuid().ToString("N");
        }
        _notes.AddRange(fragment.Pacenotes);
        var result = (new PacenoteRecord { Pacenotes = new List<Pacenote>(_notes) }, _group);
        if (placement != "Start" && placement != "Mid") _group = null;
        return result;
    }
}
