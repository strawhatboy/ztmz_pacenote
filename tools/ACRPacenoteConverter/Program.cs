using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

return args.FirstOrDefault()?.Equals("aliases", StringComparison.OrdinalIgnoreCase) == true
    ? await AliasExporter.RunAsync(args.Skip(1).ToArray())
    : await Converter.RunAsync(args);

internal static class Converter
{
    private static readonly IReadOnlyDictionary<string, string> TokenMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Left1"] = "1_left", ["Left2"] = "2_left", ["Left3"] = "3_left",
            ["Left4"] = "4_left", ["Left5"] = "5_left", ["Left6"] = "6_left",
            ["Right1"] = "1_right", ["Right2"] = "2_right", ["Right3"] = "3_right",
            ["Right4"] = "4_right", ["Right5"] = "5_right", ["Right6"] = "6_right",
            ["LeftHP"] = "hairpin_left", ["RightHP"] = "hairpin_right",
            ["Flat"] = "flat", ["FlatLeft"] = "flat_left", ["FlatRight"] = "flat_right",
            ["Into"] = "into", ["And"] = "and", ["Long"] = "long", ["VeryLong"] = "very_long",
            ["Short"] = "short", ["Crest"] = "crest", ["OverCrest"] = "over_crest",
            ["OverBumps"] = "over_bumps", ["Dip"] = "dip", ["Opens"] = "opens",
            ["OpensLate"] = "opens_late", ["OpensToFlat"] = "opens_to_flat",
            ["Tightens"] = "tightens", ["TightensLate"] = "tightens_late",
            ["Tighten2"] = "tightens_to_2", ["Tighten3"] = "tightens_to_3",
            ["DontCut"] = "dont_cut", ["Caution"] = "caution", ["SlowDown"] = "slow",
            ["Sudden"] = "sudden", ["Narrows"] = "narrows", ["KeepLeft"] = "keep_left",
            ["KeepIn"] = "keep_in", ["KeepOut"] = "keep_out", ["Finish"] = "finish",
            ["Inside"] = "inside", ["Late"] = "late",
            ["AfterEnd"] = "after", ["AfterFinish"] = "after,finish",
            ["AfterJunction"] = "after,junction", ["AtJunction"] = "at_junction",
            ["AtTheJunction"] = "at_junction", ["BadCamber"] = "bad_camber",
            ["BigCut"] = "big_cut", ["Blind"] = "unseen", ["Brake"] = "brake",
            ["Bumps"] = "bumps", ["Cut"] = "cut", ["DitchInside"] = "ditches_inside",
            ["DitchOutside"] = "ditches_outside", ["GoLeft"] = "go,left",
            ["GoRight"] = "go,right", ["GoStraight"] = "go_straight",
            ["HeavyBrake"] = "hard,brake", ["IntoDip"] = "into,dip",
            ["JumpMaybe"] = "jump_maybe", ["Junction"] = "junction",
            ["KeepMiddle"] = "keep_middle", ["KeepRight"] = "keep_in,right",
            ["Kinks"] = "kinks", ["KinksStartingLeft"] = "kinks,slight_left",
            ["KinksStartingRight"] = "kinks,slight_right", ["LeftAcuteHP"] = "hairpin_left",
            ["LeftFlat"] = "flat_left", ["LeftKink"] = "kinks,slight_left",
            ["LeftOpenHP"] = "hairpin_left,opens", ["LeftSquare"] = "square_left",
            ["LogsInside"] = "logs_inside", ["LogsOutside"] = "logs_outside",
            ["OnAsphalt"] = "tarmac", ["OnGravel"] = "gravel", ["Over"] = "over",
            ["OverBridge"] = "over_bridge", ["OverBump"] = "over_bumps",
            ["OverJumpSmall"] = "over_small_jump", ["RightAcuteHP"] = "hairpin_right",
            ["RightChicane"] = "chicane", ["RightChicaneEntry"] = "right_entry_chicane",
            ["RightFlat"] = "flat_right", ["RightKink"] = "kinks,slight_right",
            ["RightOpenHP"] = "hairpin_right,opens", ["RightSquare"] = "square_right",
            ["SmallCut"] = "small_cut", ["StopAtMarshals"] = "stop,marshalls",
            ["ThroughGate"] = "through_gate", ["ThroughTunnel"] = "through_tunnel",
            ["Tighten1"] = "tightens_to_1", ["Tighten4"] = "tightens_to_4",
            ["Tighten5"] = "tightens_to_5", ["UpHill"] = "uphill", ["Widens"] = "widens",
            ["Pause0"] = "", ["Pause1"] = "", ["sPause0"] = "", ["sPause1"] = "",
            ["s_ResetPause0"] = ""
        };

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length < 2 || args.Contains("--help", StringComparer.OrdinalIgnoreCase))
        {
            PrintUsage();
            return args.Length == 0 ? 1 : 0;
        }

        var input = Path.GetFullPath(args[0]);
        var outputDirectory = Path.GetFullPath(args[1]);
        var dryRun = args.Contains("--dry-run", StringComparer.OrdinalIgnoreCase);
        var stageName = ReadOption(args, "--stage-name");
        var inputs = ResolveInputs(input);
        if (inputs.Count == 0)
        {
            Console.Error.WriteLine($"No JSON files found at: {input}");
            return 1;
        }

        if (!dryRun)
        {
            Directory.CreateDirectory(outputDirectory);
        }

        var allUnmapped = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(stageName) && inputs.Count != 1)
        {
            Console.Error.WriteLine("--stage-name can only be used when converting one JSON table.");
            return 1;
        }

        var fullRouteStarts = await ReadFullRouteStartsAsync(inputs);
        foreach (var source in inputs)
        {
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(source));
            var rows = FindRows(document.RootElement)
                .OrderBy(row => row.DistanceM)
                .ThenBy(row => string.Join(',', row.Tokens), StringComparer.Ordinal)
                .ToList();

            if (rows.Count == 0)
            {
                Console.Error.WriteLine($"No SplineDistanceM rows found in {Path.GetFileName(source)}.");
                continue;
            }

            var distanceOffset = GetDistanceOffset(source, rows, fullRouteStarts);

            var output = new StringBuilder();
            output.AppendLine("# ACR Table: " + Path.GetFileNameWithoutExtension(source));
            output.AppendLine("# ACR TrackLengthM: " + rows.Max(row => row.DistanceM - distanceOffset + Math.Max(row.LengthM, 0)).ToString("F3", CultureInfo.InvariantCulture));
            output.AppendLine("# ACR SourceDistanceOffsetM: " + distanceOffset.ToString("F3", CultureInfo.InvariantCulture));
            output.AppendLine("# Source: " + Path.GetFileName(source));
            foreach (var row in rows)
            {
                var translated = new List<string>();
                var unmapped = new List<string>();
                foreach (var token in row.Tokens)
                {
                    if (TryMapToken(token, out var mapped))
                    {
                        if (!string.IsNullOrEmpty(mapped))
                        {
                            translated.Add(mapped);
                        }
                    }
                    else
                    {
                        unmapped.Add(token);
                        allUnmapped.Add(token);
                    }
                }

                if (translated.Count > 0)
                {
                    output.Append((int)Math.Round(Math.Max(0, row.DistanceM - distanceOffset), MidpointRounding.AwayFromZero));
                    foreach (var token in translated)
                    {
                        output.Append(',').Append(token);
                    }
                    if (unmapped.Count > 0)
                    {
                        output.Append(" # ACR unmapped: ").Append(string.Join(", ", unmapped));
                    }
                    output.AppendLine();
                }
                else
                {
                    output.Append("# ").Append((int)Math.Round(row.DistanceM, MidpointRounding.AwayFromZero))
                        .Append("m has no mapped ZTMZ tokens: ").AppendLine(string.Join(", ", row.Tokens));
                }
            }

            var outputName = string.IsNullOrWhiteSpace(stageName)
                ? Path.GetFileNameWithoutExtension(source)
                : stageName;
            var destination = Path.Combine(outputDirectory, outputName + ".pacenote");
            if (dryRun)
            {
                Console.WriteLine($"Would write {rows.Count} rows: {destination}");
            }
            else
            {
                await File.WriteAllTextAsync(destination, output.ToString(), new UTF8Encoding(false));
                Console.WriteLine($"Wrote {rows.Count} rows: {destination}");
            }
        }

        if (allUnmapped.Count > 0)
        {
            var report = Path.Combine(outputDirectory, "ACR-unmapped-tokens.txt");
            var content = string.Join(Environment.NewLine, allUnmapped) + Environment.NewLine;
            if (dryRun)
            {
                Console.WriteLine($"Would report {allUnmapped.Count} unmapped ACR tokens: {report}");
            }
            else
            {
                await File.WriteAllTextAsync(report, content, new UTF8Encoding(false));
                Console.WriteLine($"Reported {allUnmapped.Count} unmapped ACR tokens: {report}");
            }
        }

        return 0;
    }

    private static List<string> ResolveInputs(string input) =>
        File.Exists(input)
            ? new List<string> { input }
            : Directory.Exists(input)
                ? Directory.EnumerateFiles(input, "DT_Pacenote*.json", SearchOption.AllDirectories)
                    .OrderBy(path => path).ToList()
                : new List<string>();

    private static async Task<Dictionary<string, double>> ReadFullRouteStartsAsync(IEnumerable<string> inputs)
    {
        var starts = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in inputs.Where(IsFullRouteTable))
        {
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(source));
            var firstDistance = FindRows(document.RootElement).Select(row => row.DistanceM).DefaultIfEmpty().Min();
            if (firstDistance > 0 && GetRouteKey(source) is { } routeKey)
            {
                starts[routeKey] = firstDistance;
            }
        }

        return starts;
    }

    private static double GetDistanceOffset(string source, IReadOnlyCollection<Row> rows, IReadOnlyDictionary<string, double> fullRouteStarts)
    {
        if (IsFullRouteTable(source) || GetRouteKey(source) is not { } routeKey ||
            !fullRouteStarts.TryGetValue(routeKey, out var fullRouteStart))
        {
            return 0;
        }

        return Math.Max(0, rows.Min(row => row.DistanceM) - fullRouteStart);
    }

    private static bool IsFullRouteTable(string source)
    {
        var table = Path.GetFileNameWithoutExtension(source);
        return !Regex.IsMatch(table, "(?:Cut|Short)\\d+", RegexOptions.IgnoreCase);
    }

    private static string? GetRouteKey(string source)
    {
        var table = Path.GetFileNameWithoutExtension(source);
        const string prefix = "DT_Pacenote";
        if (!table.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var direction = table.Contains("Forward", StringComparison.OrdinalIgnoreCase) ? "Forward"
            : table.Contains("Reverse", StringComparison.OrdinalIgnoreCase) ? "Reverse"
            : null;
        if (direction is null)
        {
            return null;
        }

        var route = table[prefix.Length..];
        route = Regex.Replace(route, "Forward|Reverse|Full|Cut\\d+|Short\\d+", "", RegexOptions.IgnoreCase);
        return route + ":" + direction;
    }

    private static string? ReadOption(IReadOnlyList<string> args, string option)
    {
        var index = args.ToList().FindIndex(argument => argument.Equals(option, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Count ? args[index + 1] : null;
    }

    private static IEnumerable<Row> FindRows(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (TryGetProperty(element, "SplineDistanceM", out var distanceElement) &&
                TryReadNumber(distanceElement, out var distance) &&
                TryGetProperty(element, "TokenList", out var tokenList))
            {
                var tokens = ExtractNames(tokenList).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (tokens.Count > 0)
                {
                var length = TryGetProperty(element, "LengthM", out var lengthElement) && TryReadNumber(lengthElement, out var parsedLength)
                    ? parsedLength
                    : 0;
                yield return new Row(distance, length, tokens);
                }
            }

            foreach (var property in element.EnumerateObject())
            {
                foreach (var row in FindRows(property.Value))
                {
                    yield return row;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                foreach (var row in FindRows(child))
                {
                    yield return row;
                }
            }
        }
    }

    private static IEnumerable<string> ExtractNames(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            yield return element.GetString()!;
            yield break;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                foreach (var name in ExtractNames(item))
                {
                    yield return name;
                }
            }
            yield break;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals("Tokens", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Equals("Value", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Equals("Name", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Equals("Text", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var name in ExtractNames(property.Value))
                {
                    yield return name;
                }
            }
        }
    }

    private static bool TryMapToken(string token, out string mapped)
    {
        if (token.StartsWith("Pause", StringComparison.OrdinalIgnoreCase) ||
            token.StartsWith("sPause", StringComparison.OrdinalIgnoreCase))
        {
            mapped = string.Empty;
            return true;
        }

        if (TokenMap.TryGetValue(token, out mapped!))
        {
            return true;
        }

        if (token.StartsWith("Dist", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(token[4..], NumberStyles.None, CultureInfo.InvariantCulture, out var distance))
        {
            mapped = distance.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        mapped = string.Empty;
        return false;
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    private static bool TryReadNumber(JsonElement element, out double value)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out value))
        {
            return true;
        }
        if (element.ValueKind == JsonValueKind.String &&
            double.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }
        value = 0;
        return false;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage: ACRPacenoteConverter <input-json-file-or-directory> <output-profile-directory> [--stage-name <runtime-title>] [--dry-run]");
        Console.WriteLine("Input JSON must be exported from the ACR DataTable and contain SplineDistanceM and TokenList.");
    }

    private sealed record Row(double DistanceM, double LengthM, IReadOnlyList<string> Tokens);
}

internal static class AliasExporter
{
    private static readonly Regex StageKeyPattern = new(
        "^CH_(BOLLENE|ELATIA|HAFREN_NORTH|HAFREN_SOUTH|LOUTRAKI|MUNSTER|SAVERNE|SISTERON)_(FULL|PART1|PART2)_([FR])$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex TrackKeyPattern = new(
        "^TRACK_(BOLLENE|ELATIA|HAFRENNORTH|HAFRENSOUTH|LOUTRAKI|MUNSTER|SAVERNE|SISTERON)_(FULL|CUT1|CUT2|CUT3|SHORT1|SHORT2)_(FORWARD|REVERSE)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length < 2 || args.Contains("--help", StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine("Usage: ACRPacenoteConverter aliases <output-json> <locres-or-json-file-or-directory> [...]");
            return args.Length == 0 ? 1 : 0;
        }

        var outputPath = Path.GetFullPath(args[0]);
        var files = args.Skip(1)
            .SelectMany(ResolveLocalizationInputs)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (files.Count == 0)
        {
            Console.Error.WriteLine("No .locres or FModel Game.json files found.");
            return 1;
        }

        var stages = new Dictionary<string, StageAlias>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            var language = Directory.GetParent(file)?.Name ?? "unknown";
            foreach (var (key, value) in ReadLocalization(file))
            {
                if (!TryGetTableName(key, out var table) || string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                if (!stages.TryGetValue(key, out var stage))
                {
                    stage = new StageAlias(key, table);
                    stages.Add(key, stage);
                }
                stage.Titles[language] = value;
            }
        }

        var model = stages.Values.OrderBy(stage => stage.Key, StringComparer.Ordinal).ToList();
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await using var stream = File.Create(outputPath);
        await JsonSerializer.SerializeAsync(stream, model, new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine($"Wrote {model.Count} stage aliases: {outputPath}");
        return 0;
    }

    private static IReadOnlyDictionary<string, string> ReadLocalization(string path) =>
        Path.GetExtension(path).Equals(".locres", StringComparison.OrdinalIgnoreCase)
            ? LocResReader.Read(path)
            : ReadFModelLocalization(path);

    private static IEnumerable<string> ResolveLocalizationInputs(string input)
    {
        if (File.Exists(input) && IsLocalizationFile(input))
        {
            return new[] { Path.GetFullPath(input) };
        }
        return Directory.Exists(input)
            ? Directory.EnumerateFiles(input, "*.locres", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(input, "Game.json", SearchOption.AllDirectories))
            : Array.Empty<string>();
    }

    private static bool IsLocalizationFile(string path) =>
        Path.GetExtension(path).Equals(".locres", StringComparison.OrdinalIgnoreCase) ||
        Path.GetFileName(path).Equals("Game.json", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyDictionary<string, string> ReadFModelLocalization(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var entries = document.RootElement;
        if (entries.ValueKind == JsonValueKind.Object && entries.TryGetProperty(string.Empty, out var defaultNamespace))
        {
            entries = defaultNamespace;
        }

        if (entries.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        return entries.EnumerateObject()
            .Where(property => property.Value.ValueKind == JsonValueKind.String)
            .ToDictionary(property => property.Name, property => property.Value.GetString() ?? string.Empty, StringComparer.Ordinal);
    }

    private static bool TryGetTableName(string key, out string table)
    {
        var stageMatch = StageKeyPattern.Match(key);
        if (stageMatch.Success)
        {
            table = ToTableName(stageMatch.Groups[1].Value, stageMatch.Groups[2].Value, stageMatch.Groups[3].Value == "F" ? "Forward" : "Reverse");
            return true;
        }

        var trackMatch = TrackKeyPattern.Match(key);
        if (trackMatch.Success)
        {
            table = ToTableName(trackMatch.Groups[1].Value switch
            {
                "HAFRENNORTH" => "HAFREN_NORTH",
                "HAFRENSOUTH" => "HAFREN_SOUTH",
                var family => family
            }, trackMatch.Groups[2].Value, trackMatch.Groups[3].Value[..1] + trackMatch.Groups[3].Value[1..].ToLowerInvariant());
            return true;
        }

        table = string.Empty;
        return false;
    }

    private static string ToTableName(string family, string variant, string direction)
    {
        var part = variant.Replace("PART", "CUT", StringComparison.Ordinal);
        return family switch
        {
            "HAFREN_NORTH" => part switch
            {
                "FULL" => $"DT_PacenoteHafrenNorthFull{direction}",
                "CUT1" or "SHORT1" => $"DT_PacenoteHafrenNorth{direction}Cut1",
                "CUT2" or "SHORT2" => $"DT_PacenoteHafrenNorth{direction}Cut2",
                _ => throw new InvalidOperationException()
            },
            "HAFREN_SOUTH" => $"DT_PacenoteHafrenSouth{direction}",
            "MUNSTER" => part switch
            {
                "FULL" => $"DT_PacenoteMunsterFull{direction}",
                "CUT1" or "SHORT1" => $"DT_PacenoteMunsterShort1{direction}",
                "CUT2" or "SHORT2" => $"DT_PacenoteMunsterShort2{direction}",
                _ => throw new InvalidOperationException()
            },
            _ => $"DT_Pacenote{ToPascalCase(family)}{(part == "FULL" ? "Full" : part.Replace("SHORT", "CUT", StringComparison.Ordinal).Replace("CUT", "Cut", StringComparison.Ordinal))}{direction}"
        };
    }

    private static string ToPascalCase(string value) => string.Concat(value.Split('_').Select(part =>
        char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant()));

    private sealed class StageAlias(string key, string table)
    {
        public string Key { get; } = key;
        public string Table { get; } = table;
        public SortedDictionary<string, string> Titles { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}

internal static class LocResReader
{
    private const byte CompactVersion = 1;
    private const byte OptimizedVersion = 2;
    private const byte Crc32Version = 3;

    public static IReadOnlyDictionary<string, string> Read(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
        if (stream.Length < 17)
        {
            throw new InvalidDataException($"'{path}' is not a valid localization resource.");
        }

        stream.Position = 16; // LocRes magic GUID.
        var version = reader.ReadByte();
        if (version < CompactVersion || version > Crc32Version)
        {
            throw new InvalidDataException($"'{path}' uses unsupported LocRes version {version}.");
        }

        var stringArrayOffset = reader.ReadInt64();
        var localizedStrings = ReadLocalizedStrings(reader, stringArrayOffset, version);

        if (version >= Crc32Version)
        {
            _ = reader.ReadUInt32(); // Total entry count.
        }

        var namespaceCount = reader.ReadUInt32();
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var namespaceIndex = 0; namespaceIndex < namespaceCount; namespaceIndex++)
        {
            _ = ReadTextKey(reader, version);
            var keyCount = reader.ReadUInt32();
            for (var keyIndex = 0; keyIndex < keyCount; keyIndex++)
            {
                var key = ReadTextKey(reader, version);
                _ = reader.ReadUInt32(); // Source-string hash.
                var localizedStringIndex = reader.ReadInt32();
                if ((uint)localizedStringIndex < (uint)localizedStrings.Count)
                {
                    result[key] = localizedStrings[localizedStringIndex];
                }
            }
        }

        return result;
    }

    private static List<string> ReadLocalizedStrings(BinaryReader reader, long offset, byte version)
    {
        if (offset < 0)
        {
            return new List<string>();
        }

        var entryPosition = reader.BaseStream.Position;
        reader.BaseStream.Position = offset;
        var count = reader.ReadInt32();
        var strings = new List<string>(count);
        for (var index = 0; index < count; index++)
        {
            strings.Add(ReadFString(reader));
            _ = reader.ReadInt32(); // Reference count.
        }
        reader.BaseStream.Position = entryPosition;
        return strings;
    }

    private static string ReadTextKey(BinaryReader reader, byte version)
    {
        if (version >= OptimizedVersion)
        {
            _ = reader.ReadUInt32(); // Text-key hash.
        }
        return ReadFString(reader);
    }

    private static string ReadFString(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        if (length == 0)
        {
            return string.Empty;
        }

        if (length > 0)
        {
            var bytes = reader.ReadBytes(length);
            return Encoding.UTF8.GetString(bytes).TrimEnd('\0');
        }

        var characters = reader.ReadBytes(checked(-length * sizeof(char)));
        return Encoding.Unicode.GetString(characters).TrimEnd('\0');
    }
}
