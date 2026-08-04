using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ZTMZ.PacenoteTool.Base;
using DatabaseScriptResource = ZTMZ.PacenoteTool.Base.Script.ScriptResource;

namespace ZTMZ.PacenoteTool.BeamNG;

public sealed record BeamNGPacenoteTranslation(
    PacenoteRecord Record,
    IReadOnlyList<string> UnrecognizedWords);

/// <summary>
/// Converts BeamNG Rally notes to ZTMZ sound-token names. Structured notebook data is
/// preferred because it is independent of BeamNG's rendered note text; legacy freeform
/// notebooks fall back to the stage-authored source text.
/// </summary>
public static class BeamNGPacenoteTranslator
{
    private static readonly Regex DirectionBeforeDegree = new(
        @"\b(left|right)\s+([1-6])\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex TightensOrOpensDegree = new(
        @"\b(tightens|opens)\s+(?=(?:[1-6]|acute|flat|hairpin|square)\b)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex SingularBump = new(
        @"\bbump\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex SingularDrop = new(
        @"\bdrop\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Downhill = new(
        @"\bdown\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Uphill = new(
        @"\bup\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Bumpy = new(
        @"\bbumpy\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Braking = new(
        @"\bbraking\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Slippy = new(
        @"\bslippy\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Watersplash = new(
        @"\bwatersplash\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex KeepDirection = new(
        @"\bkeep\s+(left|right)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex OnCrest = new(
        @"\bon\s+crest\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex TightOrOpenHairpin = new(
        @"\b(?:tight|open)\s+hairpin\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ToStop = new(
        @"\bto\s+stop\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex DontBeforeCaution = new(
        @"\bdont\s+(?=(?:care|caution|brake)\b)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Mid = new(
        @"\bmid\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Tar = new(
        @"\btar\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex NumberWord = new(
        @"\b(?:one|two|three|four|five|six)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex NonWord = new(
        @"[^a-z0-9]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // This is BeamNG's current standard English modifier taxonomy. It deliberately
    // uses source phrases so the installed ZTMZ co-driver vocabulary remains the
    // final authority on which audio tokens can actually be played.
    internal static readonly IReadOnlyDictionary<string, string> StandardModifierPhrases =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["slowing"] = "slowing",
            ["brake"] = "brake",
            ["brakeOverBumps"] = "brake over bumps",
            ["brakeOverCrest"] = "brake over crest",
            ["braking"] = "brake",
            ["stop"] = "stop",
            ["beBrave"] = "brave",
            ["getsFasterNow"] = "fast",
            ["intoTurn"] = "into turn",
            ["turn"] = "turn",
            ["narrows"] = "narrows",
            ["dontGoWide"] = "keep in",
            ["early"] = "early",
            ["goWide"] = "outside",
            ["late"] = "late",
            ["middle"] = "keep middle",
            ["middleOverCrest"] = "keep middle over crest",
            ["nips"] = "tightens",
            ["twisty"] = "twisty",
            ["wide"] = "widens",
            ["bigCut"] = "big cut",
            ["cut"] = "cut",
            ["dont"] = "dont cut",
            ["dontCut"] = "dont cut",
            ["dontCutLate"] = "dont cut late",
            ["earlyCut"] = "early cut",
            ["lateCut"] = "late cut",
            ["smallCut"] = "small cut",
            ["keepIn"] = "keep in",
            ["keepLeft"] = "keep left",
            ["keepLeftOverCrest"] = "keep left over crest",
            ["keepMiddle"] = "keep middle",
            ["keepMiddleOverCrest"] = "keep middle over crest",
            ["keepRight"] = "keep right",
            ["keepRightOverCrest"] = "keep right over crest",
            ["chicaneLeftEntry"] = "left entry chicane",
            ["chicaneRightEntry"] = "right entry chicane",
            ["overJump"] = "over jump",
            ["jump"] = "jump",
            ["badDipJump"] = "bad dip jump",
            ["badJump"] = "bad jump",
            ["bigJump"] = "big jump",
            ["overCrest"] = "over crest",
            ["crest"] = "crest",
            ["intoCrest"] = "into crest",
            ["longCrest"] = "long crest",
            ["bigCrest"] = "big crest",
            ["smallCrest"] = "small crest",
            ["badDip"] = "bad dip",
            ["dip"] = "dip",
            ["overDip"] = "over dip",
            ["throughDip"] = "through dip",
            ["bump"] = "over bump",
            ["bumpy"] = "bumpy",
            ["badDrop"] = "bad drop",
            ["badKick"] = "over bumps",
            ["kick"] = "over bumps",
            ["drop"] = "drop",
            ["down"] = "down",
            ["up"] = "up",
            ["ontoGravel"] = "onto gravel",
            ["ontoIce"] = "onto ice",
            ["ontoTarmac"] = "onto tarmac",
            ["ontoMud"] = "slippery",
            ["slippy"] = "slippy",
            ["watersplash"] = "watersplash",
            ["water"] = "ford",
            ["intoWater"] = "into ford",
            ["atJunction"] = "at junction",
            ["atRock"] = "at rocks",
            ["atTree"] = "at tree",
            ["intoBridge"] = "into bridge",
            ["narrowBridge"] = "narrow bridge",
            ["overBridge"] = "over bridge",
            ["pastFence"] = "fence",
            ["pastHouse"] = "house",
            ["pastJunction"] = "junction",
            ["pastRock"] = "rocks",
            ["pastTree"] = "tree",
            ["pastWall"] = "wall",
            ["throughBridge"] = "through bridge",
            ["throughGate"] = "through gate",
            ["throughTunnel"] = "through tunnel",
            ["underBridge"] = "bridge",
            ["deceptive"] = "deceptive",
            ["sudden"] = "sudden",
            ["toStop"] = "to stop",
            ["finish"] = "finish"
        };

    private static readonly string[] CornerLengthBuckets = ["short", "standard", "half_long", "long", "extra_long"];

    public static BeamNGPacenoteTranslation Translate(
        BeamNGRallyPacenote pacenote,
        IEnumerable<string>? availableTokens = null)
    {
        ArgumentNullException.ThrowIfNull(pacenote);

        var sourceText = GetSourceText(pacenote);
        if (HasStructuredItems(pacenote.Structured))
        {
            var structuredTranslation = TranslateStructured(pacenote.Structured!, sourceText, availableTokens);
            if (structuredTranslation.Record.Pacenotes.Count > 0 || string.IsNullOrWhiteSpace(sourceText))
            {
                return structuredTranslation;
            }
        }

        if (!IsEnglishOrUnspecified(pacenote.SourceLanguage))
        {
            return new BeamNGPacenoteTranslation(
                new PacenoteRecord { RawText = sourceText },
                [$"source-language:{pacenote.SourceLanguage}"]);
        }

        return Translate(sourceText, availableTokens);
    }

    public static BeamNGPacenoteTranslation Translate(
        string? text,
        IEnumerable<string>? availableTokens = null)
    {
        var record = new PacenoteRecord { RawText = text?.Trim() ?? string.Empty };
        var tokenPhrases = BuildTokenPhrases(availableTokens ?? DatabaseScriptResource.Instance.FilenameToIdDict.Keys);
        var unrecognizedWords = new List<string>();
        AppendText(record, tokenPhrases, record.RawText, unrecognizedWords);
        return new BeamNGPacenoteTranslation(record, unrecognizedWords);
    }

    private static BeamNGPacenoteTranslation TranslateStructured(
        BeamNGRallyPacenoteStructured structured,
        string sourceText,
        IEnumerable<string>? availableTokens)
    {
        var record = new PacenoteRecord { RawText = sourceText };
        var tokenPhrases = BuildTokenPhrases(availableTokens ?? DatabaseScriptResource.Instance.FilenameToIdDict.Keys);
        var unrecognizedWords = new List<string>();

        var items = structured.Items
            .OrderBy(pair => GetSlotOrder(pair.Key))
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Value)
            .ToArray();
        for (var index = 0; index < items.Length; index++)
        {
            var item = items[index];
            if (string.IsNullOrWhiteSpace(item.Type))
            {
                continue;
            }

            var phrase = string.Equals(item.Type, "dont", StringComparison.Ordinal) &&
                         index + 1 < items.Length &&
                         string.Equals(items[index + 1].Type, "cut", StringComparison.Ordinal)
                ? "dont cut"
                : GetStructuredItemPhrase(item, unrecognizedWords);
            if (string.Equals(item.Type, "dont", StringComparison.Ordinal) &&
                index + 1 < items.Length &&
                string.Equals(items[index + 1].Type, "cut", StringComparison.Ordinal))
            {
                index++;
            }
            if (string.IsNullOrWhiteSpace(phrase))
            {
                unrecognizedWords.Add($"type:{item.Type}");
                continue;
            }

            AppendText(record, tokenPhrases, phrase, unrecognizedWords);
        }

        return new BeamNGPacenoteTranslation(record, unrecognizedWords);
    }

    private static void AppendText(
        PacenoteRecord record,
        IReadOnlyList<TokenPhrase> tokenPhrases,
        string text,
        List<string> unrecognizedWords)
    {
        var words = NormalizeWords(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < words.Length;)
        {
            var match = tokenPhrases.FirstOrDefault(candidate => Matches(candidate, words, index));
            if (match is null)
            {
                unrecognizedWords.Add(words[index]);
                index++;
                continue;
            }

            record.Pacenotes.Add(new Pacenote { Note = match.Token });
            index += match.Words.Length;
        }
    }

    private static bool HasStructuredItems(BeamNGRallyPacenoteStructured? structured)
    {
        return structured?.Items.Values.Any(item => !string.IsNullOrWhiteSpace(item.Type)) == true;
    }

    private static string GetSourceText(BeamNGRallyPacenote pacenote)
    {
        return !string.IsNullOrWhiteSpace(pacenote.SourceText)
            ? pacenote.SourceText
            : pacenote.Text;
    }

    private static bool IsEnglishOrUnspecified(string? language)
    {
        return string.IsNullOrWhiteSpace(language) ||
               language.StartsWith("en", StringComparison.OrdinalIgnoreCase);
    }

    private static int GetSlotOrder(string slot)
    {
        return int.TryParse(slot, out var result) ? result : int.MaxValue;
    }

    private static string? GetStructuredItemPhrase(
        BeamNGRallyPacenoteStructuredItem item,
        List<string> unrecognizedWords)
    {
        if (string.Equals(item.Type, "caution", StringComparison.Ordinal))
        {
            return item.Level switch
            {
                1 => "care",
                2 => "caution",
                3 => "double caution",
                _ => null
            };
        }

        if (string.Equals(item.Type, "corner", StringComparison.Ordinal))
        {
            return GetCornerPhrase(item, unrecognizedWords);
        }

        return StandardModifierPhrases.TryGetValue(item.Type, out var phrase) ? phrase : null;
    }

    private static string? GetCornerPhrase(
        BeamNGRallyPacenoteStructuredItem item,
        List<string> unrecognizedWords)
    {
        var parts = new List<string>();
        var direction = item.Direction switch
        {
            -1 => "left",
            1 => "right",
            _ => string.Empty
        };
        var grade = GetCornerGrade(item.Intensity, item.RiskIntensity);
        var descriptor = item.Descriptor ?? string.Empty;

        switch (descriptor)
        {
            case "":
            case "bare":
                if (grade.HasValue && !string.IsNullOrEmpty(direction))
                {
                    parts.Add($"{grade.Value} {direction}");
                }
                else if (grade.HasValue)
                {
                    parts.Add(grade.Value.ToString());
                }
                else if (!string.IsNullOrEmpty(direction))
                {
                    parts.Add(direction);
                }
                break;

            case "flat":
            case "square":
                parts.Add(string.IsNullOrEmpty(direction) ? descriptor : $"{descriptor} {direction}");
                break;

            case "hairpin":
                parts.Add(string.IsNullOrEmpty(direction) ? "hairpin" : $"hairpin {direction}");
                break;

            case "openHairpin":
                parts.Add(string.IsNullOrEmpty(direction) ? "opens hairpin" : $"opens hairpin {direction}");
                break;

            case "tightHairpin":
                parts.Add(string.IsNullOrEmpty(direction) ? "tightens hairpin" : $"tightens hairpin {direction}");
                break;

            default:
                unrecognizedWords.Add($"descriptor:{descriptor}");
                if (grade.HasValue && !string.IsNullOrEmpty(direction))
                {
                    parts.Add($"{grade.Value} {direction}");
                }
                break;
        }

        var length = GetCornerLengthPhrase(grade, item.ArcMeters, item.LengthMode);
        if (!string.IsNullOrEmpty(length))
        {
            parts.Add(length);
        }

        var shape = GetCornerShapePhrase(item.Shape);
        if (!string.IsNullOrEmpty(shape))
        {
            parts.Add(shape);
        }
        else if (!string.IsNullOrWhiteSpace(item.Shape))
        {
            unrecognizedWords.Add($"shape:{item.Shape}");
        }

        return parts.Count == 0 ? null : string.Join(' ', parts);
    }

    private static int? GetCornerGrade(float? intensity, int? riskIntensity)
    {
        if (!intensity.HasValue || intensity.Value < 0)
        {
            return null;
        }

        var grade = intensity.Value < 35f ? 1
            : intensity.Value < 65f ? 2
            : intensity.Value < 116f ? 3
            : intensity.Value < 190f ? 4
            : intensity.Value < 260f ? 5
            : 6;

        // BeamNG's positive risk adjustment moves toward the more severe end of
        // its one-to-six numbering ladder.
        return Math.Clamp(grade - (riskIntensity ?? 0), 1, 6);
    }

    private static string? GetCornerLengthPhrase(int? grade, float? arcMeters, string? lengthMode)
    {
        if (!grade.HasValue || !arcMeters.HasValue || arcMeters.Value < 0 ||
            string.Equals(lengthMode, "skip", StringComparison.Ordinal))
        {
            return null;
        }

        var bucket = GetCornerLengthBucket(grade.Value, arcMeters.Value);
        if (bucket is null)
        {
            return null;
        }

        var index = Array.IndexOf(CornerLengthBuckets, bucket);
        if (string.Equals(lengthMode, "shorter", StringComparison.Ordinal))
        {
            index = Math.Max(0, index - 1);
        }
        else if (string.Equals(lengthMode, "longer", StringComparison.Ordinal))
        {
            index = Math.Min(CornerLengthBuckets.Length - 1, index + 1);
        }

        return CornerLengthBuckets[index] == "standard"
            ? null
            : CornerLengthBuckets[index].Replace("_", " ", StringComparison.Ordinal);
    }

    private static string? GetCornerLengthBucket(int grade, float arcMeters)
    {
        var boundaries = grade switch
        {
            1 => new[] { 6f, 35f, 47f, 60f },
            2 => new[] { 6f, 55f, 65f, 75f },
            3 => new[] { 28f, 60f, 77f, 95f },
            4 => new[] { 37f, 85f, 107f, 130f },
            5 => new[] { 42f, 100f, 125f, 150f },
            6 => new[] { 50f, 100f, 140f, 180f },
            _ => null
        };
        if (boundaries is null)
        {
            return null;
        }

        for (var index = 0; index < boundaries.Length; index++)
        {
            if (arcMeters < boundaries[index])
            {
                return CornerLengthBuckets[index];
            }
        }

        return CornerLengthBuckets[^1];
    }

    private static string? GetCornerShapePhrase(string? shape)
    {
        return shape switch
        {
            null or "" => null,
            "opens" => "opens",
            "tightens" => "tightens",
            "opensOverCrest" => "opens over crest",
            "opensAndTightens" => "opens and tightens",
            "overCrest" => "over crest",
            "tightensDown" => "tightens downhill",
            "tightensOverCrest" => "tightens over crest",
            "tightensAndOpens" => "tightens and opens",
            _ => null
        };
    }

    private static List<TokenPhrase> BuildTokenPhrases(IEnumerable<string> availableTokens)
    {
        return availableTokens
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .Select(token => new TokenPhrase(token, NormalizeTokenPhrase(token)))
            .Where(candidate => candidate.Words.Length > 0)
            .GroupBy(candidate => string.Join(' ', candidate.Words), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(candidate => candidate.Token, StringComparer.OrdinalIgnoreCase).First())
            .OrderByDescending(candidate => candidate.Words.Length)
            .ThenBy(candidate => candidate.Token, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool Matches(TokenPhrase candidate, IReadOnlyList<string> words, int startIndex)
    {
        if (startIndex + candidate.Words.Length > words.Count)
        {
            return false;
        }

        for (var index = 0; index < candidate.Words.Length; index++)
        {
            if (!string.Equals(candidate.Words[index], words[startIndex + index], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static string[] NormalizeTokenPhrase(string token)
    {
        var phrase = NormalizeWords(token.Replace('_', ' '));
        phrase = phrase.Replace("squre", "square", StringComparison.Ordinal);
        phrase = phrase.Replace("junctioin", "junction", StringComparison.Ordinal);
        return phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    private static string NormalizeWords(string text)
    {
        text = text
            .Replace('\u2019', '\'')
            .Replace("don't", "dont", StringComparison.OrdinalIgnoreCase)
            .Replace("do not", "dont", StringComparison.OrdinalIgnoreCase)
            .Replace("|", " and ", StringComparison.Ordinal)
            .ToLowerInvariant();
        text = KeepDirection.Replace(text, "$1");
        text = OnCrest.Replace(text, "over crest");
        text = TightOrOpenHairpin.Replace(text, "hairpin");
        text = ToStop.Replace(text, "stop");
        text = DontBeforeCaution.Replace(text, string.Empty);
        text = Mid.Replace(text, "keep middle");
        text = Tar.Replace(text, "tarmac");
        text = NumberWord.Replace(text, match => match.Value switch
        {
            "one" => "1",
            "two" => "2",
            "three" => "3",
            "four" => "4",
            "five" => "5",
            "six" => "6",
            _ => match.Value
        });
        text = DirectionBeforeDegree.Replace(text, "$2 $1");
        text = TightensOrOpensDegree.Replace(text, "$1 to ");
        text = SingularBump.Replace(text, "bumps");
        text = SingularDrop.Replace(text, "drops");
        text = Downhill.Replace(text, "downhill");
        text = Uphill.Replace(text, "uphill");
        text = Bumpy.Replace(text, "bumps");
        text = Braking.Replace(text, "brake");
        text = Slippy.Replace(text, "slippery");
        text = Watersplash.Replace(text, "ford");
        return NonWord.Replace(text, " ").Trim();
    }

    private sealed record TokenPhrase(string Token, string[] Words);
}
