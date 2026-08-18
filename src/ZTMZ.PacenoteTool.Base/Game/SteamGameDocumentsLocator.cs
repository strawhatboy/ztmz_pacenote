#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ZTMZ.PacenoteTool.Base.Game;

public interface IGameDocumentsLocator
{
    IReadOnlyList<string> FindDocumentsDirectories(uint steamAppId);
}

public interface ISteamGameInstallationLocator
{
    bool IsInstalled(uint steamAppId);
}

public static class GameDocumentsLocatorFactory
{
    public static IGameDocumentsLocator CreateDefault()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsGameDocumentsLocator(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        }

        if (OperatingSystem.IsLinux())
        {
            return new ProtonGameDocumentsLocator(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        }

        return EmptyGameDocumentsLocator.Instance;
    }
}

public sealed class WindowsGameDocumentsLocator : IGameDocumentsLocator
{
    private readonly string _documentsDirectory;

    public WindowsGameDocumentsLocator(string documentsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentsDirectory);
        _documentsDirectory = Path.GetFullPath(documentsDirectory);
    }

    public IReadOnlyList<string> FindDocumentsDirectories(uint steamAppId)
    {
        return [_documentsDirectory];
    }
}

public sealed class ProtonGameDocumentsLocator : IGameDocumentsLocator, ISteamGameInstallationLocator
{
    public const string AdditionalSteamRootsEnvironmentVariable = "ZTMZ_STEAM_ROOTS";

    private readonly string _userProfileDirectory;
    private readonly IReadOnlyList<string>? _steamRoots;

    public ProtonGameDocumentsLocator(
        string userProfileDirectory,
        IEnumerable<string>? steamRoots = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userProfileDirectory);
        _userProfileDirectory = Path.GetFullPath(userProfileDirectory);
        _steamRoots = steamRoots?.Where(path => !string.IsNullOrWhiteSpace(path)).ToArray();
    }

    public IReadOnlyList<string> FindDocumentsDirectories(uint steamAppId)
    {
        var libraries = FindSteamLibraries();
        var installedLibraries = libraries
            .Where(library => SafeFileExists(Path.Combine(
                library,
                "steamapps",
                $"appmanifest_{steamAppId}.acf")))
            .ToList();

        var candidateLibraries = installedLibraries.Count > 0
            ? installedLibraries
            : libraries.Where(library => SafeDirectoryExists(Path.Combine(
                library,
                "steamapps",
                "compatdata",
                steamAppId.ToString()))).ToList();

        var result = new List<string>();
        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var seen = new HashSet<string>(comparer);

        if (installedLibraries.Count > 0)
        {
            AddNativeLinuxDocumentsDirectory(steamAppId, result, seen);
        }

        foreach (var library in candidateLibraries)
        {
            var prefix = Path.Combine(
                library,
                "steamapps",
                "compatdata",
                steamAppId.ToString(),
                "pfx");
            AddPrefixDocumentsDirectories(prefix, result, seen);
        }

        return result;
    }

    public bool IsInstalled(uint steamAppId)
    {
        return FindSteamLibraries().Any(library => SafeFileExists(Path.Combine(
            library,
            "steamapps",
            $"appmanifest_{steamAppId}.acf")));
    }

    private void AddNativeLinuxDocumentsDirectory(
        uint steamAppId,
        List<string> result,
        HashSet<string> seen)
    {
        if (steamAppId != 310560)
        {
            return;
        }

        var xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        var dataHome = string.IsNullOrWhiteSpace(xdgDataHome)
            ? Path.Combine(_userProfileDirectory, ".local", "share")
            : xdgDataHome;
        var feralDocuments = Path.Combine(
            dataHome,
            "feral-interactive",
            "DiRT Rally",
            "VFS",
            "User",
            "AppData",
            "Roaming");
        if (SafeDirectoryExists(feralDocuments))
        {
            AddPath(feralDocuments, result, seen);
        }
    }

    private IReadOnlyList<string> FindSteamLibraries()
    {
        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var libraries = new List<string>();
        var seen = new HashSet<string>(comparer);

        foreach (var steamRoot in GetSteamRoots())
        {
            AddDirectory(steamRoot, libraries, seen);

            var libraryFoldersFile = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            foreach (var configuredLibrary in ReadConfiguredLibraries(libraryFoldersFile))
            {
                AddDirectory(configuredLibrary, libraries, seen);
            }
        }

        return libraries;
    }

    private IEnumerable<string> GetSteamRoots()
    {
        if (_steamRoots != null)
        {
            return _steamRoots;
        }

        var roots = new List<string>
        {
            Path.Combine(_userProfileDirectory, ".local", "share", "Steam"),
            Path.Combine(_userProfileDirectory, ".steam", "steam"),
            Path.Combine(_userProfileDirectory, ".steam", "root"),
            Path.Combine(_userProfileDirectory, ".steam", "debian-installation"),
            Path.Combine(
                _userProfileDirectory,
                ".var",
                "app",
                "com.valvesoftware.Steam",
                ".local",
                "share",
                "Steam")
        };

        AddXdgSteamRoot(roots, Environment.GetEnvironmentVariable("HOST_XDG_DATA_HOME"));
        AddXdgSteamRoot(roots, Environment.GetEnvironmentVariable("XDG_DATA_HOME"));

        var additionalRoots = Environment.GetEnvironmentVariable(
            AdditionalSteamRootsEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(additionalRoots))
        {
            roots.AddRange(additionalRoots.Split(
                Path.PathSeparator,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        return roots;
    }

    private static void AddXdgSteamRoot(List<string> roots, string? dataHome)
    {
        if (!string.IsNullOrWhiteSpace(dataHome))
        {
            roots.Add(Path.Combine(dataHome, "Steam"));
        }
    }

    private static IReadOnlyList<string> ReadConfiguredLibraries(string file)
    {
        try
        {
            return SafeFileExists(file)
                ? SteamLibraryFoldersVdf.Parse(File.ReadAllText(file))
                : [];
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static void AddPrefixDocumentsDirectories(
        string prefix,
        List<string> result,
        HashSet<string> seen)
    {
        var usersDirectory = Path.Combine(prefix, "drive_c", "users");
        var userDirectories = new List<string>
        {
            Path.Combine(usersDirectory, "steamuser")
        };

        if (SafeDirectoryExists(usersDirectory))
        {
            try
            {
                userDirectories.AddRange(Directory.EnumerateDirectories(usersDirectory)
                    .Where(directory => !IsSharedWineUser(Path.GetFileName(directory))));
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
            }
        }

        var foundExistingDirectory = false;
        foreach (var userDirectory in userDirectories.Distinct(seen.Comparer))
        {
            foreach (var name in new[] { "Documents", "My Documents" })
            {
                var documentsDirectory = Path.Combine(userDirectory, name);
                if (SafeDirectoryExists(documentsDirectory))
                {
                    foundExistingDirectory = true;
                    AddPath(documentsDirectory, result, seen);
                }
            }
        }

        if (!foundExistingDirectory)
        {
            AddPath(
                Path.Combine(usersDirectory, "steamuser", "Documents"),
                result,
                seen);
        }
    }

    private static bool IsSharedWineUser(string userName)
    {
        return string.Equals(userName, "Public", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(userName, "Default", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(userName, "All Users", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddDirectory(
        string path,
        List<string> result,
        HashSet<string> seen)
    {
        if (SafeDirectoryExists(path))
        {
            AddPath(path, result, seen);
        }
    }

    private static void AddPath(
        string path,
        List<string> result,
        HashSet<string> seen)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            if (seen.Add(fullPath))
            {
                result.Add(fullPath);
            }
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
        }
    }

    private static bool SafeDirectoryExists(string path)
    {
        try
        {
            return Directory.Exists(path);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool SafeFileExists(string path)
    {
        try
        {
            return File.Exists(path);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

public sealed class EmptyGameDocumentsLocator : IGameDocumentsLocator
{
    public static EmptyGameDocumentsLocator Instance { get; } = new();

    private EmptyGameDocumentsLocator()
    {
    }

    public IReadOnlyList<string> FindDocumentsDirectories(uint steamAppId)
    {
        return [];
    }
}

internal static class SteamLibraryFoldersVdf
{
    public static IReadOnlyList<string> Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var tokens = Tokenize(content);
        var result = new List<string>();
        for (var index = 0; index + 1 < tokens.Count; index++)
        {
            if (tokens[index].Kind != VdfTokenKind.String ||
                tokens[index + 1].Kind != VdfTokenKind.String)
            {
                continue;
            }

            var key = tokens[index].Value;
            var value = tokens[index + 1].Value;
            if ((string.Equals(key, "path", StringComparison.OrdinalIgnoreCase) ||
                 (uint.TryParse(key, out _) && LooksLikePath(value))) &&
                LooksLikePath(value))
            {
                result.Add(value);
            }

            index++;
        }

        return result;
    }

    private static bool LooksLikePath(string value)
    {
        return !string.IsNullOrWhiteSpace(value) && Path.IsPathRooted(value);
    }

    private static List<VdfToken> Tokenize(string content)
    {
        var tokens = new List<VdfToken>();
        var index = 0;
        while (index < content.Length)
        {
            if (char.IsWhiteSpace(content[index]))
            {
                index++;
                continue;
            }

            if (content[index] == '/' &&
                index + 1 < content.Length &&
                content[index + 1] == '/')
            {
                index += 2;
                while (index < content.Length && content[index] != '\n')
                {
                    index++;
                }

                continue;
            }

            if (content[index] == '{')
            {
                tokens.Add(new VdfToken(VdfTokenKind.OpenBrace, string.Empty));
                index++;
                continue;
            }

            if (content[index] == '}')
            {
                tokens.Add(new VdfToken(VdfTokenKind.CloseBrace, string.Empty));
                index++;
                continue;
            }

            tokens.Add(new VdfToken(VdfTokenKind.String, ReadString(content, ref index)));
        }

        return tokens;
    }

    private static string ReadString(string content, ref int index)
    {
        if (content[index] != '"')
        {
            var start = index;
            while (index < content.Length &&
                   !char.IsWhiteSpace(content[index]) &&
                   content[index] is not '{' and not '}')
            {
                index++;
            }

            return content[start..index];
        }

        index++;
        var characters = new List<char>();
        while (index < content.Length)
        {
            var character = content[index++];
            if (character == '"')
            {
                break;
            }

            if (character == '\\' && index < content.Length)
            {
                var escaped = content[index++];
                characters.Add(escaped switch
                {
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    _ => escaped
                });
                continue;
            }

            characters.Add(character);
        }

        return new string(characters.ToArray());
    }

    private enum VdfTokenKind
    {
        String,
        OpenBrace,
        CloseBrace
    }

    private sealed record VdfToken(VdfTokenKind Kind, string Value);
}
