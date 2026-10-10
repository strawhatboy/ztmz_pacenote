using System.Text;
using System.Text.RegularExpressions;

namespace ZTMZ.PacenoteTool.KT;

internal sealed record WrcGenerationsSessionSnapshot(
    string Identity, string Track, string Car, string CarClass,
    bool ResultReady, int RaceMilliseconds, int ResultMilliseconds);

internal interface IWrcGenerationsSessionSource : IDisposable
{
    WrcGenerationsSessionSnapshot? Read();
}

/// <summary>Installed game strings, not redistributed assets or executable Lua.</summary>
internal sealed class WrcGenerationsSessionCatalog
{
    private readonly string[] names;
    private readonly Dictionary<int, string> classes = new();

    internal WrcGenerationsSessionCatalog(byte[] english, string localeIds, string cars)
    {
        if (english.Length < 6 || english.Length > 4 * 1024 * 1024 || english.Length % 2 != 0 ||
            english[^1] != 0 || english[^2] != 0)
            throw new InvalidDataException("Invalid WRCG localization table");
        // PLOC: four-byte header followed by null-terminated UTF-16LE strings in LocalesId order.
        names = new UnicodeEncoding(false, false, true).GetString(english, 4, english.Length - 4).Split('\0');
        if (names.Length > 100_000) throw new InvalidDataException("Oversized WRCG localization table");
        var ids = Matches(localeIds, @"(?m)^\s*(\w+)\s*=\s*(\d+)\s*,")
            .ToDictionary(m => m.Groups[1].Value, m => int.Parse(m.Groups[2].Value), StringComparer.Ordinal);
        foreach (Match table in Matches(cars, @"(?ms)^\w+\s*=\s*\{([^{}]*)"))
        {
            var name = Matches(table.Groups[1].Value, @"(?m)^\s*Name\s*=\s*LocalesId\.(\w+)").Cast<Match>().FirstOrDefault();
            var category = Matches(table.Groups[1].Value, @"(?m)^\s*Category\s*=\s*WrcChampionships\.(\w+)").Cast<Match>().FirstOrDefault();
            if (name is not null && category is not null && ids.TryGetValue(name.Groups[1].Value, out var id))
                classes[id] = category.Groups[1].Value;
        }
    }

    internal static WrcGenerationsSessionCatalog Load(string directory)
    {
        var data = WrcGenerationsArchive.ReadResources(directory,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "LOCALISATION_EN.PLOC", "LOCALISATION.LUA", "CARS.CFG" });
        return new(data["LOCALISATION_EN.PLOC"], Encoding.UTF8.GetString(data["LOCALISATION.LUA"]), Encoding.UTF8.GetString(data["CARS.CFG"]));
    }

    internal string Name(int id) => id >= 0 && id < names.Length - 1 && names[id].Length is > 0 and <= 256
        ? names[id] : string.Empty;
    internal string Class(int carNameId) => classes.GetValueOrDefault(carNameId, string.Empty);
    private static MatchCollection Matches(string text, string pattern) =>
        Regex.Matches(text, pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
}
