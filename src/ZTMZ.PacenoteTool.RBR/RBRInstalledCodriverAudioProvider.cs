#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using IniParser;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.RBR;

internal sealed class RBRInstalledCodriverAudioProvider : IGamePacenoteAudioProvider
{
    public const string ProviderId = "rbr-installed-codriver";

    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    private readonly object _sync = new();
    private RBRGameInstallationConfig? _installationConfiguration;
    private bool _enabled;
    private Snapshot? _snapshot;
    private string _snapshotSignature = string.Empty;

    internal string PackageName => _snapshot?.PackageName ?? string.Empty;
    internal string Language => _snapshot?.Language ?? string.Empty;
    internal int ResolvedPacenoteCount => _snapshot?.PacenotePaths.Count ?? 0;

    public void Configure(RBRGameInstallationConfig? installationConfiguration, bool enabled)
    {
        lock (_sync)
        {
            _installationConfiguration = installationConfiguration;
            _enabled = enabled;
            _snapshot = null;
            _snapshotSignature = string.Empty;
        }
    }

    public void PreparePacenoteAudio()
    {
        lock (_sync)
        {
            if (!_enabled)
            {
                _snapshot = null;
                _snapshotSignature = string.Empty;
                return;
            }

            if (!RBRGameInstallPathResolver.TryResolveRoot(
                    _installationConfiguration,
                    out var rootDirectory,
                    out _))
            {
                _snapshot = null;
                _snapshotSignature = string.Empty;
                Logger.Warn("Installed RBR co-driver audio is enabled, but the RBR installation could not be resolved.");
                return;
            }

            var settingsPath = Path.Combine(rootDirectory, "Plugins", "Pacenote", "PaceNote.ini");
            var signature = CreateSignature(rootDirectory, settingsPath);
            if (_snapshot != null && string.Equals(signature, _snapshotSignature, StringComparison.Ordinal))
            {
                return;
            }

            _snapshot = LoadSnapshot(rootDirectory, settingsPath);
            _snapshotSignature = signature;
            if (_snapshot != null)
            {
                Logger.Info(
                    "Loaded installed RBR co-driver package {0} ({1}); resolved {2} pacenote IDs, {3} modifier names, and {4} system sounds.",
                    _snapshot.PackageName,
                    _snapshot.Language,
                    _snapshot.PacenotePaths.Count,
                    _snapshot.ModifierPaths.Count,
                    _snapshot.SystemSoundPaths.Count);
            }
        }
    }

    public bool TryResolvePacenoteAudio(
        GamePacenoteAudioCue cue,
        out GamePacenoteAudioResolution resolution)
    {
        resolution = new GamePacenoteAudioResolution([]);
        if (!string.Equals(cue.ProviderId, ProviderId, StringComparison.Ordinal))
        {
            return false;
        }

        Snapshot? snapshot;
        lock (_sync)
        {
            snapshot = _snapshot;
        }
        if (snapshot == null || !TryResolvePacenote(snapshot, cue.PacenoteType, out var pacenotePaths))
        {
            return false;
        }

        var segments = new List<GamePacenoteAudioSegment>
        {
            new(pacenotePaths)
        };
        foreach (var modifierType in cue.ModifierTypes)
        {
            if (!TryResolveModifier(snapshot, modifierType, out var modifierPaths))
            {
                return false;
            }
            segments.Add(new GamePacenoteAudioSegment(modifierPaths));
        }

        resolution = new GamePacenoteAudioResolution(segments);
        return true;
    }

    public bool TryResolveSystemAudio(
        string systemSoundToken,
        out GamePacenoteAudioSegment segment)
    {
        segment = new GamePacenoteAudioSegment([]);
        Snapshot? snapshot;
        lock (_sync)
        {
            snapshot = _snapshot;
        }

        if (snapshot == null ||
            !snapshot.SystemSoundPaths.TryGetValue(systemSoundToken, out var paths) ||
            paths.Count == 0)
        {
            return false;
        }

        segment = new GamePacenoteAudioSegment(paths);
        return true;
    }

    internal static Snapshot? LoadSnapshot(string rootDirectory, string? settingsPath = null)
    {
        settingsPath ??= Path.Combine(rootDirectory, "Plugins", "Pacenote", "PaceNote.ini");
        if (!File.Exists(settingsPath))
        {
            Logger.Warn("RBR Pacenote plugin settings were not found at {0}.", settingsPath);
            return null;
        }

        var parser = new FileIniDataParser();
        var settings = parser.ReadFile(settingsPath);
        var packageName = settings["SETTINGS"]?["sounds"]?.Trim();
        var language = settings["SETTINGS"]?["language"]?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(packageName))
        {
            Logger.Warn("RBR Pacenote.ini does not select a sound package.");
            return null;
        }

        var pluginRoot = Path.Combine(rootDirectory, "Plugins", "Pacenote");
        var soundRoot = Path.GetFullPath(Path.Combine(pluginRoot, "sounds"));
        var packageDirectory = Path.GetFullPath(Path.Combine(soundRoot, packageName));
        if (!IsDescendant(packageDirectory, soundRoot) || !Directory.Exists(packageDirectory))
        {
            Logger.Warn("The selected RBR co-driver package path is invalid or missing: {0}.", packageDirectory);
            return null;
        }

        var availableFiles = Directory.EnumerateFiles(packageDirectory, "*", SearchOption.TopDirectoryOnly)
            .Where(IsSupportedAudioFile)
            .Select(path => new { Name = Path.GetFileName(path), Path = path })
            .Where(file => !string.IsNullOrEmpty(file.Name))
            .ToDictionary(file => file.Name!, file => file.Path, StringComparer.OrdinalIgnoreCase);
        var packagesDirectory = Path.Combine(pluginRoot, "config", "pacenotes", "packages");
        var pacenoteCandidates = new Dictionary<int, List<Candidate>>();
        var modifierCandidates = new Dictionary<string, List<Candidate>>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(packagesDirectory))
        {
            foreach (var definitionPath in Directory.EnumerateFiles(
                         packagesDirectory,
                         "*.ini",
                         SearchOption.AllDirectories))
            {
                ParseDefinition(
                    definitionPath,
                    packagesDirectory,
                    availableFiles,
                    pacenoteCandidates,
                    modifierCandidates);
            }
        }

        return new Snapshot(
            packageName,
            language,
            packageDirectory,
            ChoosePacenoteCandidates(pacenoteCandidates),
            ChooseNamedCandidates(modifierCandidates),
            ResolveSystemSoundPaths(rootDirectory, language, availableFiles),
            availableFiles);
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> ResolveSystemSoundPaths(
        string rootDirectory,
        string language,
        IReadOnlyDictionary<string, string> packageFiles)
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        AddSystemSound(result, Constants.SYSTEM_COUNTDOWN_1, ResolveStem(packageFiles, "_one"));
        AddSystemSound(result, Constants.SYSTEM_COUNTDOWN_2, ResolveStem(packageFiles, "_two"));
        AddSystemSound(result, Constants.SYSTEM_COUNTDOWN_3, ResolveStem(packageFiles, "_three"));
        AddSystemSound(result, Constants.SYSTEM_COUNTDOWN_4, ResolveStem(packageFiles, "_four"));
        AddSystemSound(result, Constants.SYSTEM_COUNTDOWN_5, ResolveStem(packageFiles, "_five"));
        AddSystemSound(result, Constants.SYSTEM_GO, ResolveStem(packageFiles, "go"));
        AddSystemSound(result, Constants.SYSTEM_END_STAGE, ResolveStem(packageFiles, "finish"));

        var speechDirectoryName = ResolveBrokenSpeechDirectoryName(language);
        if (speechDirectoryName != null)
        {
            var brokenSpeechDirectory = Path.Combine(
                rootDirectory,
                "Audio",
                "Speech",
                "Broken",
                speechDirectoryName);
            AddSystemSound(
                result,
                Constants.SYSTEM_ENGINE_DAMAGE,
                ResolveAudioFiles(brokenSpeechDirectory, "problem_engine"));
            var coolingWarning = ResolveAudioFiles(brokenSpeechDirectory, "water_temp");
            AddSystemSound(result, Constants.SYSTEM_RADIATOR_DAMAGE, coolingWarning);
            AddSystemSound(result, Constants.SYSTEM_WATER_PUMP_DAMAGE, coolingWarning);
        }

        return result;
    }

    private static void AddSystemSound(
        IDictionary<string, IReadOnlyList<string>> destination,
        string token,
        IReadOnlyList<string> paths)
    {
        if (paths.Count > 0)
        {
            destination[token] = paths;
        }
    }

    private static IReadOnlyList<string> ResolveAudioFiles(string directory, string stem)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var files = Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
            .Where(IsSupportedAudioFile)
            .Select(path => new { Name = Path.GetFileName(path), Path = path })
            .Where(file => !string.IsNullOrEmpty(file.Name))
            .ToDictionary(file => file.Name!, file => file.Path, StringComparer.OrdinalIgnoreCase);
        return ResolveStem(files, stem);
    }

    private static string? ResolveBrokenSpeechDirectoryName(string language)
    {
        return language.Trim().ToLowerInvariant() switch
        {
            "english" or "eng" => "Eng",
            "czech" or "cz" or "cze" => "Czh",
            "french" or "fr" or "fre" => "Fre",
            "german" or "de" or "ger" => "Ger",
            "italian" or "it" or "ita" => "Ita",
            "polish" or "pl" or "pol" => "Pol",
            "spanish" or "es" or "spa" => "Spa",
            _ => null
        };
    }

    private static void ParseDefinition(
        string definitionPath,
        string packagesDirectory,
        IReadOnlyDictionary<string, string> availableFiles,
        IDictionary<int, List<Candidate>> pacenoteCandidates,
        IDictionary<string, List<Candidate>> modifierCandidates)
    {
        try
        {
            var data = ReadDefinition(definitionPath);
            var relativePath = Path.GetRelativePath(packagesDirectory, definitionPath);
            var isModifierDefinition = relativePath.Split(
                    [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                    StringSplitOptions.RemoveEmptyEntries)
                .Any(part => string.Equals(part, "modifier", StringComparison.OrdinalIgnoreCase));

            foreach (var section in data.Sections)
            {
                const string prefix = "PACENOTE::";
                if (!section.SectionName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var name = section.SectionName[prefix.Length..].Trim();
                var paths = section.Keys
                    .Where(key => key.KeyName.StartsWith("Snd", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(key => ParseSoundIndex(key.KeyName))
                    .SelectMany(key => ResolveSoundPaths(key.Value, availableFiles))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (paths.Count == 0)
                {
                    continue;
                }

                var candidate = new Candidate(definitionPath, paths);
                if (isModifierDefinition)
                {
                    AddCandidate(modifierCandidates, name, candidate);
                }
                else if (section.Keys.ContainsKey("id") &&
                         int.TryParse(section.Keys["id"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                {
                    AddCandidate(pacenoteCandidates, id, candidate);
                }
            }
        }
        catch (Exception exception)
        {
            Logger.Debug(exception, "Ignoring unreadable RBR pacenote definition {0}.", definitionPath);
        }
    }

    private static IniParser.Model.IniData ReadDefinition(string definitionPath)
    {
        var content = File.ReadAllText(definitionPath);
        content = Regex.Replace(
            content,
            @"(?m)^(\s*\[[^\]\r\n]+\])\s*[;#].*$",
            "$1",
            RegexOptions.CultureInvariant);
        return new IniParser.Parser.IniDataParser().Parse(content);
    }

    private static IEnumerable<string> ResolveSoundPaths(
        string configuredValue,
        IReadOnlyDictionary<string, string> availableFiles)
    {
        var separatorIndex = configuredValue.LastIndexOf("::", StringComparison.Ordinal);
        var configuredName = (separatorIndex >= 0 ? configuredValue[(separatorIndex + 2)..] : configuredValue).Trim();
        if (string.IsNullOrWhiteSpace(configuredName) ||
            Path.IsPathRooted(configuredName) ||
            configuredName.Contains('/') ||
            configuredName.Contains('\\'))
        {
            yield break;
        }

        if (availableFiles.TryGetValue(configuredName, out var exactPath))
        {
            yield return exactPath;
        }

        var stem = Path.GetFileNameWithoutExtension(configuredName);
        var extension = Path.GetExtension(configuredName);
        var variantPattern = new Regex(
            $"^{Regex.Escape(stem)}_?[0-9]+{Regex.Escape(extension)}$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        foreach (var pair in availableFiles
                     .Where(pair => variantPattern.IsMatch(pair.Key))
                     .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            yield return pair.Value;
        }
    }

    private static bool TryResolvePacenote(Snapshot snapshot, int pacenoteType, out IReadOnlyList<string> paths)
    {
        if (snapshot.PacenotePaths.TryGetValue(pacenoteType, out paths!))
        {
            return true;
        }

        return TryResolveFromMetadata(snapshot, pacenoteType, category: null, ztmzIds: null, out paths);
    }

    private static bool TryResolveModifier(Snapshot snapshot, int modifierType, out IReadOnlyList<string> paths)
    {
        var metadata = RBRScriptResource.Instance.PacenotesDict.TryGetValue(modifierType, out var entries)
            ? entries.Where(entry => string.Equals(entry.category, "MODIFIER", StringComparison.OrdinalIgnoreCase))
                .ToList()
            : [];
        foreach (var entry in metadata)
        {
            if (snapshot.ModifierPaths.TryGetValue(entry.name, out paths!))
            {
                return true;
            }
        }

        RBRScriptResource.Instance.ModiferId2ZTMZids.TryGetValue(modifierType, out var ztmzIds);
        return TryResolveFromMetadata(snapshot, modifierType, "MODIFIER", ztmzIds, out paths);
    }

    private static bool TryResolveFromMetadata(
        Snapshot snapshot,
        int sourceId,
        string? category,
        IReadOnlyList<int>? ztmzIds,
        out IReadOnlyList<string> paths)
    {
        var stems = new List<string>();
        if (RBRScriptResource.Instance.PacenotesDict.TryGetValue(sourceId, out var metadata))
        {
            foreach (var entry in metadata.Where(entry => category == null ||
                         string.Equals(entry.category, category, StringComparison.OrdinalIgnoreCase)))
            {
                stems.AddRange(CreateStemCandidates(entry.name));
                stems.AddRange(CreateStemCandidates(entry.description));
            }
        }

        if (ztmzIds != null)
        {
            foreach (var ztmzId in ztmzIds)
            {
                if (Base.Script.ScriptResource.Instance.FilenameDict.TryGetValue(ztmzId, out var aliases))
                {
                    stems.AddRange(aliases.SelectMany(CreateStemCandidates));
                }
            }
        }

        foreach (var stem in stems.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var resolved = ResolveStem(snapshot.AvailableFiles, stem);
            if (resolved.Count > 0)
            {
                paths = resolved;
                return true;
            }
        }

        paths = [];
        return false;
    }

    private static IEnumerable<string> CreateStemCandidates(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            yield break;
        }

        var builder = new StringBuilder();
        foreach (var character in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
            }
            else if (builder.Length > 0 && builder[^1] != '_')
            {
                builder.Append('_');
            }
        }
        var normalized = builder.ToString().Trim('_');
        if (normalized.Length == 0)
        {
            yield break;
        }

        yield return normalized;
        var compact = normalized.Replace("_", string.Empty, StringComparison.Ordinal);
        if (!string.Equals(compact, normalized, StringComparison.Ordinal))
        {
            yield return compact;
        }
        if (normalized.StartsWith("detail_", StringComparison.Ordinal))
        {
            yield return normalized[7..];
        }
    }

    private static IReadOnlyList<string> ResolveStem(
        IReadOnlyDictionary<string, string> availableFiles,
        string stem)
    {
        var pattern = new Regex(
            $"^{Regex.Escape(stem)}_?[0-9]+\\.(?:ogg|wav|mp3|aiff|wma|aac|mp4|m4a)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var numbered = availableFiles
            .Where(pair => pattern.IsMatch(pair.Key))
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => pair.Value)
            .ToList();
        if (numbered.Count > 0)
        {
            return numbered;
        }

        foreach (var extension in new[] { ".ogg", ".wav", ".mp3", ".aiff", ".wma", ".aac", ".mp4", ".m4a" })
        {
            if (availableFiles.TryGetValue(stem + extension, out var exactPath))
            {
                return [exactPath];
            }
        }
        return [];
    }

    private static Dictionary<int, IReadOnlyList<string>> ChoosePacenoteCandidates(
        IReadOnlyDictionary<int, List<Candidate>> candidates)
    {
        return candidates.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)ChooseCandidate(pair.Value).Paths);
    }

    private static Dictionary<string, IReadOnlyList<string>> ChooseNamedCandidates(
        IReadOnlyDictionary<string, List<Candidate>> candidates)
    {
        return candidates.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)ChooseCandidate(pair.Value).Paths,
            StringComparer.OrdinalIgnoreCase);
    }

    private static Candidate ChooseCandidate(IEnumerable<Candidate> candidates)
    {
        return candidates
            .OrderByDescending(candidate => candidate.Paths.Count)
            .ThenBy(candidate => candidate.SourcePath, StringComparer.OrdinalIgnoreCase)
            .First();
    }

    private static void AddCandidate<TKey>(
        IDictionary<TKey, List<Candidate>> candidates,
        TKey key,
        Candidate candidate) where TKey : notnull
    {
        if (!candidates.TryGetValue(key, out var values))
        {
            values = [];
            candidates[key] = values;
        }
        values.Add(candidate);
    }

    private static int ParseSoundIndex(string key)
    {
        return int.TryParse(key.AsSpan(3), NumberStyles.None, CultureInfo.InvariantCulture, out var index)
            ? index
            : int.MaxValue;
    }

    private static bool IsSupportedAudioFile(string path)
    {
        return Path.GetExtension(path) is var extension &&
               new[] { ".ogg", ".wav", ".mp3", ".aiff", ".wma", ".aac", ".mp4", ".m4a" }
                   .Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsDescendant(string candidate, string root)
    {
        var relative = Path.GetRelativePath(root, candidate);
        return !Path.IsPathRooted(relative) &&
               !relative.Equals("..", StringComparison.Ordinal) &&
               !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static string CreateSignature(string rootDirectory, string settingsPath)
    {
        var lastWrite = File.Exists(settingsPath) ? File.GetLastWriteTimeUtc(settingsPath).Ticks : 0;
        return string.Concat(rootDirectory, "|", settingsPath, "|", lastWrite.ToString(CultureInfo.InvariantCulture));
    }

    internal sealed record Snapshot(
        string PackageName,
        string Language,
        string PackageDirectory,
        IReadOnlyDictionary<int, IReadOnlyList<string>> PacenotePaths,
        IReadOnlyDictionary<string, IReadOnlyList<string>> ModifierPaths,
        IReadOnlyDictionary<string, IReadOnlyList<string>> SystemSoundPaths,
        IReadOnlyDictionary<string, string> AvailableFiles);

    private sealed record Candidate(string SourcePath, List<string> Paths);
}
