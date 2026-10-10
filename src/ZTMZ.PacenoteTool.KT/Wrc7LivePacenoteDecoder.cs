using System.Text.RegularExpressions;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.Base.Script;

namespace ZTMZ.PacenoteTool.KT;

/// <summary>Converts observed WRC7 voice resources into shared core events.</summary>
internal sealed class Wrc7LivePacenoteDecoder(Wrc7PacenoteCatalog catalog, IReadOnlySet<string> vocabulary)
{
    public bool TryDecode(int pid, long tick, string resource, bool? accessibleMode,
        out LivePacenoteEvent? result)
        => TryDecodeWithModes(pid, tick, resource, new(accessibleMode, accessibleMode, accessibleMode), out result);

    public bool TryDecodeWithModes(int pid, long tick, string resource, Wrc7PacenoteModes modes,
        out LivePacenoteEvent? result)
    {
        result = null;
        if (!TryResolve(resource, modes, out var tokens)) return false;
        result = new LivePacenoteEvent
        {
            Id = $"{pid}:{tick}:{resource}", DisplayGroupId = $"{pid}:{tick}:{resource}",
            SourceResourceName = resource,
            Record = new PacenoteRecord
            {
                RawText = string.Join('/', tokens),
                Pacenotes = tokens.Select(token => new Pacenote { Note = token }).ToList()
            }
        };
        return true;
    }

    private bool TryResolve(string resource, Wrc7PacenoteModes modes, out string[] tokens)
    {
        tokens = [];
        var groups = catalog.ResolveGroups(resource, modes);
        if (groups.Count == 0) return false;
        var combined = new List<string>();
        foreach (var group in groups)
        {
            string[]? resolved = null;
            foreach (var atom in group)
            {
                if (!TryMap(atom, modes.ForCategory(catalog.Category(atom)), out var candidate)) return false;
                if (resolved is not null && !resolved.SequenceEqual(candidate)) return false;
                resolved = candidate;
            }
            if (resolved is null) return false;
            combined.AddRange(resolved);
        }
        tokens = combined.ToArray();
        return tokens.Length > 0;
    }

    private bool TryMap(string atom, bool? accessibleMode, out string[] tokens)
    {
        tokens = [];
        // WRC7 FLS frames 41/42 select XL_Chicane_L/R: viewed bottom-to-top,
        // RL enters on the left; LR enters on the right. ZTMZ names the entrance.
        if (atom is "CHICANE_LEFT" or "CHICANE_RIGHT")
        {
            var token = atom == "CHICANE_LEFT" ? "right_entry_chicane" : "left_entry_chicane";
            if (!vocabulary.Contains(token)) return false;
            tokens = [token];
            return true;
        }
        // Simplified hairpins deliberately discard open/acute detail. Do not
        // reconstruct a detail that the observed sound no longer carries.
        if (accessibleMode == true && catalog.Category(atom) == "HAIRPIN")
        {
            var sound = catalog.AccessibleSound(atom);
            if (sound == "PN_HairpinLeft") atom = "HAIRPIN_LEFT";
            else if (sound == "PN_HairpinRight") atom = "HAIRPIN_RIGHT";
        }
        if (Regex.IsMatch(atom, @"^(LEFT|RIGHT)_[1-9]$", RegexOptions.CultureInvariant))
        {
            var grade = Regex.Match(catalog.AccessibleSound(atom), @"^PN_(Left|Right)([1-6])$", RegexOptions.CultureInvariant);
            if (!grade.Success) return false;
            var token = grade.Groups[2].Value + "_" + grade.Groups[1].Value.ToLowerInvariant();
            if (!vocabulary.Contains(token)) return false;
            tokens = [token];
            return true;
        }
        // These source-specific direction/grade cases require an independent
        // WRC7 audit. Do not silently reuse WRCG's icon-based conclusions.
        if (atom.Contains("CHICANE", StringComparison.Ordinal) || atom == "DONUT" ||
            atom.StartsWith("LEFT_RIGHT_", StringComparison.Ordinal) || atom.StartsWith("RIGHT_LEFT_", StringComparison.Ordinal) ||
            atom.StartsWith("tightens_", StringComparison.Ordinal) && atom is not "tightens_1" and not "tightens_2")
            return false;
        return WrcGenerationsTokenMapper.TryMap(atom, vocabulary, out tokens);
    }
}
