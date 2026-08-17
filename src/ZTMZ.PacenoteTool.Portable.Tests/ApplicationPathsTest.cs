using ZTMZ.PacenoteTool.Base;
using Xunit;

namespace ZTMZ.PacenoteTool.Portable.Tests;

public class ApplicationPathsTest
{
    [Fact]
    public void ResolveLinux_UsesXdgDataHomeWhenConfigured()
    {
        var result = ApplicationDataPathDefaults.ResolveLinux(
            "/mnt/data",
            "/home/deck");

        Assert.Equal(Path.Combine("/mnt/data", "ztmz"), result);
    }

    [Fact]
    public void ResolveLinux_FallsBackToUserLocalShare()
    {
        var result = ApplicationDataPathDefaults.ResolveLinux(
            null,
            "/home/deck");

        Assert.Equal(Path.Combine("/home/deck", ".local", "share", "ztmz"), result);
    }

    [Fact]
    public void AppLevelVariables_UsesConfiguredLocationAndSanitizesRelativePath()
    {
        using var directory = new TemporaryDirectory();
        var location = new TestConfigurationLocation(directory.Path);
        var paths = new AppLevelVariables(location);

        var result = paths.GetPath("profiles/bad?name.json");

        Assert.Equal(directory.Path, paths.DataRoot);
        Assert.Equal(
            Path.Combine(directory.Path, "profiles", "bad_name.json"),
            result);
    }

    [Fact]
    public void SetDataRootForNextLaunch_NormalizesAndPersistsExistingDirectory()
    {
        using var directory = new TemporaryDirectory();
        var location = new TestConfigurationLocation(null);
        var paths = new AppLevelVariables(location);

        paths.SetDataRootForNextLaunch(directory.Path + Path.DirectorySeparatorChar);

        Assert.Equal(directory.Path, location.WrittenDataRoot);
    }

    private sealed class TestConfigurationLocation(string? configuredDataRoot) : IConfigurationLocation
    {
        public string? WrittenDataRoot { get; private set; }

        public string? GetConfiguredDataRoot() => configuredDataRoot;

        public void SetConfiguredDataRoot(string path)
        {
            WrittenDataRoot = path;
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"ZTMZApplicationPathsTests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
