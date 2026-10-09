using System.Text.RegularExpressions;

namespace ZTMZ.PacenoteTool.KT;

/// <summary>Reads WRCG data, never evaluates configuration as executable Lua.</summary>
public sealed class WrcGenerationsPacenoteCatalog
{
    private readonly Dictionary<string, IReadOnlyList<string>> sounds = new(StringComparer.Ordinal);
    public int AtomCount { get; private set; }
    public int BlockCount { get; private set; }

    public static WrcGenerationsPacenoteCatalog Parse(string notes, string blocks)
    {
        var catalog = new WrcGenerationsPacenoteCatalog();
        var atoms = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match entry in Matches(Table(notes, "PaceNotes"), @"(?m)^\s*(\w+)\s*=\s*\{([^{}]*)\}\s*,?"))
        {
            var atom = entry.Groups[1].Value;
            if (!atoms.Add(atom)) throw new InvalidDataException($"Duplicate atom: {atom}");
            foreach (Match sound in Matches(entry.Groups[2].Value, "\\b(?:DetailedSound|AccessibleSound)\\s*=\\s*\"([^\"]+)\""))
            {
                var name = sound.Groups[1].Value;
                if (catalog.sounds.TryGetValue(name, out var prior) && prior[0] != atom)
                    throw new InvalidDataException($"Ambiguous sound: {name}");
                catalog.sounds[name] = Array.AsReadOnly(new[] { atom });
            }
        }
        foreach (Match entry in Matches(Table(blocks, "PaceNoteBlocks"), @"(?m)^\s*(Block_\d+)\s*=\s*\{([^{}]*)\}\s*,?"))
        {
            var tokens = Matches(entry.Groups[2].Value, "\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToArray();
            if (tokens.Length == 0 || tokens.Any(t => !atoms.Contains(t)))
                throw new InvalidDataException($"Empty block or undefined atom: {entry.Groups[1].Value}");
            if (!catalog.sounds.TryAdd(entry.Groups[1].Value, Array.AsReadOnly(tokens)))
                throw new InvalidDataException($"Duplicate block: {entry.Groups[1].Value}");
            catalog.BlockCount++;
        }
        catalog.AtomCount = atoms.Count;
        if (catalog.AtomCount == 0 || catalog.BlockCount == 0) throw new InvalidDataException("Empty pacenote catalog");
        return catalog;
    }

    /// <summary>Resolves a sound lookup, not evidence that playback has occurred.</summary>
    public bool TryResolve(string resource, out IReadOnlyList<string> atoms)
    {
        ArgumentNullException.ThrowIfNull(resource);
        var name = resource[(resource.LastIndexOf('/') + 1)..];
        if (sounds.TryGetValue(name, out var value)) { atoms = value; return true; }
        atoms = Array.Empty<string>();
        return false;
    }

    private static MatchCollection Matches(string input, string pattern) =>
        Regex.Matches(input, pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    private static string Table(string source, string name)
    {
        // Anchor the outer closing brace: AdditionalInformation has duplicate visual-only keys.
        var matches = Matches(source, @"(?ms)^" + Regex.Escape(name) + @"\s*=\s*\{(.*?)^\}");
        if (matches.Count != 1) throw new InvalidDataException($"Missing or duplicate table: {name}");
        return matches[0].Groups[1].Value;
    }
}
