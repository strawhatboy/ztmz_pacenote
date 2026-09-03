using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.AC;

public sealed class ACRGamePacenoteReader : BasePacenoteReader
{
    private const string TrackLengthHeader = "# ACR TrackLengthM: ";
    private static readonly Regex NonAlphaNumeric = new("[^\\p{L}\\p{N}]", RegexOptions.Compiled);
    private readonly IGameDataReader _gameDataReader;
    private readonly Lazy<IReadOnlyList<StageAlias>> _stageAliases;

    public ACRGamePacenoteReader(IGameDataReader gameDataReader)
    {
        _gameDataReader = gameDataReader;
        _stageAliases = new Lazy<IReadOnlyList<StageAlias>>(LoadAliases);
    }

    public override string GetScriptFileForReplaying(string profile, IGame game, string track, bool fallbackToDefault = true)
    {
        var resolved = ResolveScriptFile(profile, game, track);
        if (!string.IsNullOrEmpty(resolved))
        {
            return resolved;
        }

        return base.GetScriptFileForReplaying(profile, game, track, fallbackToDefault);
    }

    public override string GetScriptFileForRecording(string profile, IGame game, string track)
    {
        var resolved = ResolveScriptFile(profile, game, track);
        return string.IsNullOrEmpty(resolved)
            ? base.GetScriptFileForRecording(profile, game, track)
            : resolved;
    }

    private string ResolveScriptFile(string profile, IGame game, string track)
    {
        if (string.IsNullOrWhiteSpace(track) || _stageAliases.Value.Count == 0)
        {
            return string.Empty;
        }

        var trackLength = _gameDataReader.CurrentGameData.TrackLength;
        var candidates = new List<ResolvedCandidate>();
        foreach (var alias in _stageAliases.Value)
        {
            var titleScore = alias.Titles.Values.Select(title => GetTitleScore(track, title)).DefaultIfEmpty(0).Max();
            if (titleScore == 0)
            {
                continue;
            }

            var path = GetProfilePath(profile, game, alias.Table);
            if (!File.Exists(path))
            {
                continue;
            }

            var expectedLength = TryReadExpectedLength(path);
            // A generated ACR profile must include its table-derived route length. Without it,
            // a localized start name alone cannot distinguish full and cut variants.
            if (!expectedLength.HasValue || trackLength <= 0)
            {
                continue;
            }

            candidates.Add(new ResolvedCandidate(path, titleScore, Math.Abs(trackLength - expectedLength.Value)));
        }

        return candidates
            .OrderByDescending(candidate => candidate.TitleScore)
            .ThenBy(candidate => candidate.LengthDelta)
            .ThenBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
            .Select(candidate => candidate.Path)
            .FirstOrDefault() ?? string.Empty;
    }

    private static string GetProfilePath(string profile, IGame game, string table) =>
        AppLevelVariables.Instance.GetPath($"profiles\\{profile}\\{game.Name}\\{table}.pacenote");

    private static double? TryReadExpectedLength(string path)
    {
        foreach (var line in File.ReadLines(path).Take(8))
        {
            if (line.StartsWith(TrackLengthHeader, StringComparison.Ordinal) &&
                double.TryParse(line[TrackLengthHeader.Length..], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }
        }
        return null;
    }

    private static int GetTitleScore(string track, string title)
    {
        var normalizedTrack = Normalize(track);
        var normalizedTitle = Normalize(title);
        if (normalizedTrack.Length == 0 || normalizedTitle.Length == 0)
        {
            return 0;
        }
        if (normalizedTrack == normalizedTitle)
        {
            return int.MaxValue;
        }

        var commonLength = LongestCommonSubstringLength(normalizedTrack, normalizedTitle);
        return commonLength >= 3 ? commonLength * commonLength : 0;
    }

    private static string Normalize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }
        return NonAlphaNumeric.Replace(builder.ToString(), string.Empty);
    }

    private static int LongestCommonSubstringLength(string left, string right)
    {
        var previous = new int[right.Length + 1];
        var longest = 0;
        for (var leftIndex = 1; leftIndex <= left.Length; leftIndex++)
        {
            var current = new int[right.Length + 1];
            for (var rightIndex = 1; rightIndex <= right.Length; rightIndex++)
            {
                if (left[leftIndex - 1] != right[rightIndex - 1])
                {
                    continue;
                }
                current[rightIndex] = previous[rightIndex - 1] + 1;
                longest = Math.Max(longest, current[rightIndex]);
            }
            previous = current;
        }
        return longest;
    }

    private static IReadOnlyList<StageAlias> LoadAliases()
    {
        var assembly = typeof(ACRGamePacenoteReader).Assembly;
        using var stream = assembly.GetManifestResourceStream("ZTMZ.PacenoteTool.AC.stage-aliases.json");
        if (stream is null)
        {
            return Array.Empty<StageAlias>();
        }
        return JsonSerializer.Deserialize<List<StageAlias>>(stream) is { } aliases
            ? aliases
            : Array.Empty<StageAlias>();
    }

    private sealed class StageAlias
    {
        public string Key { get; init; } = string.Empty;
        public string Table { get; init; } = string.Empty;
        public Dictionary<string, string> Titles { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record ResolvedCandidate(string Path, int TitleScore, double LengthDelta);
}
