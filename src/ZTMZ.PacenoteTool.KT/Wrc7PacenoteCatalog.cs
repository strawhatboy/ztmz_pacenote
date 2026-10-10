using System.Text.RegularExpressions;

namespace ZTMZ.PacenoteTool.KT;

/// <summary>
/// WRC7's sound names are not unique atom identifiers. In particular, accessible
/// calls collapse several detailed grades. Preserve all candidates rather than
/// selecting an arbitrary corner or reusing WRCG's six-grade mapping.
/// </summary>
internal sealed class Wrc7PacenoteCatalog
{
    private readonly Dictionary<string, List<string>> detailed = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string>> accessible = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (string First, string Second)> merged = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> accessibleSounds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> categories = new(StringComparer.Ordinal);
    public int AtomCount { get; private set; }

    public static Wrc7PacenoteCatalog Parse(string source)
    {
        // Match quoted strings before comments so '--' within a sound is preserved.
        var clean = Regex.Replace(source,
            "\"(?:\\\\.|[^\"\\\\])*\"|'(?:\\\\.|[^'\\\\])*'|--\\[(=*)\\[[\\s\\S]*?\\]\\1\\]|--[^\\r\\n]*",
            match => match.Value.StartsWith("--", StringComparison.Ordinal)
                ? new string(match.Value.Select(c => c is '\r' or '\n' ? c : ' ').ToArray()) : match.Value,
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
        var tables = Matches(clean, @"(?ms)^PaceNotes\s*=\s*\{(.*?)^\}");
        if (tables.Count != 1) throw new InvalidDataException("Missing or duplicate WRC7 PaceNotes table");
        var catalog = new Wrc7PacenoteCatalog();
        var atoms = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match entry in Matches(tables[0].Groups[1].Value, @"(?m)^\s*(\w+)\s*=\s*\{([^{}]*)\}\s*,?"))
        {
            var atom = entry.Groups[1].Value;
            if (!atoms.Add(atom)) throw new InvalidDataException("Duplicate WRC7 atom: " + atom);
            var category = Matches(entry.Groups[2].Value, "\\bCategory\\s*=\\s*\"([^\"]*)\"");
            catalog.categories[atom] = category.Count == 1 ? category[0].Groups[1].Value : string.Empty;
            Add("DetailedSound", catalog.detailed);
            Add("AccessibleSound", catalog.accessible);

            void Add(string field, Dictionary<string, List<string>> target)
            {
                var values = Matches(entry.Groups[2].Value, "\\b" + field + "\\s*=\\s*\"([^\"]*)\"");
                if (values.Count != 1) throw new InvalidDataException("Missing or duplicate WRC7 sound field: " + atom + "." + field);
                var sound = values[0].Groups[1].Value;
                if (field == "AccessibleSound") catalog.accessibleSounds[atom] = sound;
                if (sound.Length == 0) return;
                if (!target.TryGetValue(sound, out var list)) target[sound] = list = [];
                list.Add(atom);
            }
        }
        catalog.AtomCount = atoms.Count;
        if (catalog.AtomCount == 0) throw new InvalidDataException("Empty WRC7 pacenote catalog");
        var merging = Matches(clean, @"(?ms)^PaceNotesMerging\s*=\s*\{(.*?)^\}");
        if (merging.Count > 1) throw new InvalidDataException("Duplicate WRC7 merging table");
        if (merging.Count == 1)
            foreach (Match group in Matches(merging[0].Groups[1].Value, @"(?m)^\s*(\w+)\s*=\s*\{([^{}]*)\}\s*,?"))
                foreach (Match entry in Matches(group.Groups[2].Value, "(?m)^\\s*(\\w+)\\s*=\\s*\"([^\"]+)\""))
                    if (!catalog.merged.TryAdd(entry.Groups[2].Value, (group.Groups[1].Value, entry.Groups[1].Value)))
                        throw new InvalidDataException("Duplicate WRC7 merged sound: " + entry.Groups[2].Value);
        return catalog;
    }

    public IReadOnlyList<string> Candidates(string resource, bool accessibleMode)
    {
        var name = Normalize(resource);
        return (accessibleMode ? accessible : detailed).TryGetValue(name, out var atoms)
            ? atoms.AsReadOnly() : Array.Empty<string>();
    }

    /// <summary>Returns ordered candidate groups; ambiguity within a group must be resolved by the caller.</summary>
    public IReadOnlyList<IReadOnlyList<string>> ResolveGroups(string resource, bool accessibleMode)
    {
        var name = Normalize(resource);
        if (merged.TryGetValue(name, out var pair))
        {
            var first = Candidates(pair.First, accessibleMode);
            var second = Candidates(pair.Second, accessibleMode);
            return first.Count > 0 && second.Count > 0 ? new[] { first, second } : [];
        }
        var single = Candidates(name, accessibleMode);
        return single.Count > 0 ? new[] { single } : [];
    }

    // Use the game's own nine-to-six grade conversion, not a guessed numeric
    // scale. The voice mode still matters when a sound name has two meanings.
    internal string AccessibleSound(string atom) => accessibleSounds.GetValueOrDefault(atom, string.Empty);
    internal string Category(string atom) => categories.GetValueOrDefault(atom, string.Empty);

    internal IReadOnlyList<IReadOnlyList<string>> ResolveGroups(string resource, Wrc7PacenoteModes modes)
    {
        string[] CandidatesForModes(string name) => Candidates(name, false)
            .Where(atom => modes.ForCategory(Category(atom)) != true)
            .Concat(Candidates(name, true).Where(atom => modes.ForCategory(Category(atom)) != false))
            .Distinct(StringComparer.Ordinal).ToArray();
        var name = Normalize(resource);
        if (merged.TryGetValue(name, out var pair))
        {
            var first = CandidatesForModes(pair.First);
            var second = CandidatesForModes(pair.Second);
            return first.Length > 0 && second.Length > 0 ? new[] { first, second } : [];
        }
        var single = CandidatesForModes(name);
        return single.Length > 0 ? new[] { single } : [];
    }

    private static string Normalize(string resource)
    {
        var leaf = resource[(Math.Max(resource.LastIndexOf('/'), resource.LastIndexOf('\\')) + 1)..];
        return leaf.EndsWith(".PAF", StringComparison.OrdinalIgnoreCase) ? leaf[..^4] : leaf;
    }

    private static MatchCollection Matches(string source, string pattern) =>
        Regex.Matches(source, pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
}
