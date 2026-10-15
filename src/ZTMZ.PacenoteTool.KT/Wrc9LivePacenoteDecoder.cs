using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.Base.Script;

namespace ZTMZ.PacenoteTool.KT;

internal sealed class Wrc9LivePacenoteDecoder(WrcGenerationsPacenoteCatalog catalog, IReadOnlySet<string> vocabulary)
{
    public bool TryDecode(int pid, long tick, string resource, out LivePacenoteEvent? result)
    {
        result = null;
        // The driver reports uppercase PAF paths; the installed tables use
        // Block_#### and Atom_<case-sensitive atom>. Resolve from the tables.
        var path = resource.Replace('\\', '/');
        if (!path.StartsWith("DATA:/SOUNDS/VOICE/", StringComparison.OrdinalIgnoreCase) ||
            !path.Contains("/PACENOTES/", StringComparison.OrdinalIgnoreCase)) return false;
        var leaf = path.Split('/').Last();
        if (!leaf.EndsWith(".PAF", StringComparison.OrdinalIgnoreCase)) return false;
        var name = leaf[..^4];
        if (!catalog.TryResolveIgnoringCase(name, out var atoms)) return false;
        var tokens = new List<string>();
        foreach (var atom in atoms)
        {
            // These directional/icon conventions still need a WRC9-specific
            // audit. Keep original game audio instead of guessing their meaning.
            if (atom.Contains("CHICANE", StringComparison.Ordinal) || atom == "DONUT" ||
                atom.StartsWith("LEFT_RIGHT_", StringComparison.Ordinal) || atom.StartsWith("RIGHT_LEFT_", StringComparison.Ordinal)) return false;
            if (!WrcGenerationsTokenMapper.TryMap(atom, vocabulary, out var mapped)) return false;
            tokens.AddRange(mapped);
        }
        if (tokens.Count == 0) return false;
        result = new()
        {
            Id = $"{pid}:{tick}:{resource}", DisplayGroupId = $"{pid}:{tick}:{resource}", SourceResourceName = resource,
            Record = new PacenoteRecord { RawText = string.Join('/', tokens), Pacenotes = tokens.Select(t => new Pacenote { Note = t }).ToList() }
        };
        return true;
    }
}
