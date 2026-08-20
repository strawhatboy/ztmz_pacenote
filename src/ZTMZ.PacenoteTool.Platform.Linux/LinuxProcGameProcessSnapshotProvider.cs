using System.Text;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.Platform.Linux;

public sealed class LinuxProcGameProcessSnapshotProvider : IGameProcessSnapshotProvider
{
    private readonly string _procRoot;

    public LinuxProcGameProcessSnapshotProvider(string procRoot = "/proc")
    {
        _procRoot = procRoot;
    }

    public IReadOnlyList<GameProcessSnapshot> Capture()
    {
        var result = new List<GameProcessSnapshot>();
        if (!Directory.Exists(_procRoot))
        {
            return result;
        }

        foreach (var directory in Directory.EnumerateDirectories(_procRoot))
        {
            if (!int.TryParse(Path.GetFileName(directory), out var processId))
            {
                continue;
            }

            try
            {
                var processName = ReadText(Path.Combine(directory, "comm")).Trim();
                var arguments = ReadNullSeparated(Path.Combine(directory, "cmdline"));
                var environment = ReadEnvironment(Path.Combine(directory, "environ"));
                result.Add(new GameProcessSnapshot(
                    processId,
                    processName,
                    arguments,
                    environment));
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return result;
    }

    private static string ReadText(string path) =>
        File.Exists(path) ? File.ReadAllText(path) : string.Empty;

    private static IReadOnlyList<string> ReadNullSeparated(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        return Encoding.UTF8.GetString(File.ReadAllBytes(path))
            .Split('\0', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static IReadOnlyDictionary<string, string> ReadEnvironment(string path)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in ReadNullSeparated(path))
        {
            var separator = entry.IndexOf('=');
            if (separator > 0)
            {
                result[entry[..separator]] = entry[(separator + 1)..];
            }
        }

        return result;
    }
}
