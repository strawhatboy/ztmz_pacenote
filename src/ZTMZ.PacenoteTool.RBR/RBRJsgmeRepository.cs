#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using IniParser.Model;
using IniParser.Parser;

namespace ZTMZ.PacenoteTool.RBR;

internal sealed record RBRJsgmeRepository(
    string ExecutablePath,
    string ModsDirectory,
    IReadOnlySet<string> EnabledMods);

internal static class RBRJsgmeRepositoryLocator
{
    private const string MainIniFileName = "JSGME.ini";

    public static bool TryLocate(string rbrRoot, out RBRJsgmeRepository repository)
    {
        repository = null!;
        if (string.IsNullOrWhiteSpace(rbrRoot) || !Directory.Exists(rbrRoot))
        {
            return false;
        }

        var root = Path.GetFullPath(rbrRoot);
        var iniPath = Path.Combine(root, MainIniFileName);
        if (!File.Exists(iniPath))
        {
            var executable = FindExecutable(root);
            if (string.IsNullOrWhiteSpace(executable))
            {
                return false;
            }

            repository = new RBRJsgmeRepository(
                executable,
                Path.Combine(root, "MODS"),
                ReadEnabledMods(Path.Combine(root, "MODS", MainIniFileName), Encoding.Default));
            return true;
        }

        RBRJsgmeRepository? fallbackRepository = null;
        foreach (var candidate in ReadIniCandidates(iniPath))
        {
            var modsFolderName = candidate.Data["MODS FOLDER"]?["Name"]?.Trim();
            if (string.IsNullOrWhiteSpace(modsFolderName))
            {
                modsFolderName = "MODS";
            }

            if (!TryResolveChildPath(root, modsFolderName, out var modsDirectory))
            {
                continue;
            }

            var executablePath = ResolveExecutable(root, candidate.Data);
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                continue;
            }

            var resolvedRepository = new RBRJsgmeRepository(
                executablePath,
                modsDirectory,
                ReadEnabledMods(Path.Combine(modsDirectory, MainIniFileName), candidate.Encoding));
            if (Directory.Exists(modsDirectory))
            {
                repository = resolvedRepository;
                return true;
            }

            fallbackRepository ??= resolvedRepository;
        }

        if (fallbackRepository is not null)
        {
            repository = fallbackRepository;
            return true;
        }
        return false;
    }

    private static string ResolveExecutable(string root, IniData ini)
    {
        var configuredPath = ini["JSGME DETAILS"]?["FullPath"]?.Trim();
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            try
            {
                var fullPath = Path.GetFullPath(configuredPath);
                if (File.Exists(fullPath) && IsUnderRoot(root, fullPath))
                {
                    return fullPath;
                }
            }
            catch (Exception)
            {
                // Fall back to discovering a renamed JSGME executable in the RBR root.
            }
        }

        return FindExecutable(root);
    }

    private static string FindExecutable(string root) =>
        Directory.EnumerateFiles(root, "*.exe", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(path => Path.GetFileName(path)
                .Contains("JSGME", StringComparison.OrdinalIgnoreCase))
        ?? string.Empty;

    private static IReadOnlySet<string> ReadEnabledMods(string iniPath, Encoding preferredEncoding)
    {
        var enabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(iniPath))
        {
            return enabled;
        }

        var candidates = ReadIniCandidates(iniPath, preferredEncoding);
        foreach (var candidate in candidates)
        {
            var section = candidate.Data.Sections.GetSectionData("MODS");
            if (section is null)
            {
                continue;
            }

            foreach (var key in section.Keys)
            {
                if (string.Equals(key.Value?.Trim(), "1", StringComparison.Ordinal))
                {
                    enabled.Add(key.KeyName);
                }
            }

            if (enabled.Count > 0)
            {
                break;
            }
        }

        return enabled;
    }

    private static IReadOnlyList<(IniData Data, Encoding Encoding)> ReadIniCandidates(
        string path,
        Encoding? preferredEncoding = null)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var bytes = File.ReadAllBytes(path);
        var encodings = new List<Encoding>();
        if (preferredEncoding is not null)
        {
            encodings.Add(preferredEncoding);
        }

        if (bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble))
        {
            encodings.Add(Encoding.UTF8);
        }
        else if (bytes.AsSpan().StartsWith(Encoding.Unicode.Preamble))
        {
            encodings.Add(Encoding.Unicode);
        }
        else
        {
            encodings.Add(Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage));
            encodings.Add(new UTF8Encoding(false, true));
            encodings.Add(Encoding.GetEncoding(936));
            encodings.Add(Encoding.GetEncoding(1252));
        }

        var parser = new IniDataParser();
        var results = new List<(IniData, Encoding)>();
        foreach (var encoding in encodings.DistinctBy(item => item.CodePage))
        {
            try
            {
                results.Add((parser.Parse(encoding.GetString(bytes)), encoding));
            }
            catch (Exception)
            {
                // Try the next plausible legacy encoding.
            }
        }

        return results;
    }

    private static bool TryResolveChildPath(string root, string relativePath, out string fullPath)
    {
        fullPath = string.Empty;
        if (Path.IsPathRooted(relativePath))
        {
            return false;
        }

        try
        {
            var candidate = Path.GetFullPath(Path.Combine(root, relativePath));
            if (!IsUnderRoot(root, candidate))
            {
                return false;
            }

            fullPath = candidate;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool IsUnderRoot(string root, string candidate)
    {
        var rootWithSeparator = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        var fullCandidate = Path.GetFullPath(candidate);
        return fullCandidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
    }
}
