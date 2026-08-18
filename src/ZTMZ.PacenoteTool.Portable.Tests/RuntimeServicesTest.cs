#nullable enable

using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Core;
using Xunit;

namespace ZTMZ.PacenoteTool.Portable.Tests;

public sealed class RuntimeServicesTest : IDisposable
{
    private readonly string _temporaryRoot = Path.Combine(
        Path.GetTempPath(),
        $"ztmz-runtime-tests-{Guid.NewGuid():N}");

    [Fact]
    public void EnsureDataDirectoriesCreatesTheSharedRuntimeLayout()
    {
        var paths = new TestApplicationPaths(_temporaryRoot);

        new ResourceBootstrapService(paths).EnsureDataDirectories();

        Assert.True(Directory.Exists(_temporaryRoot));
        Assert.True(Directory.Exists(paths.GetPath(Constants.PATH_GAMES)));
        Assert.True(Directory.Exists(paths.GetPath(Constants.PATH_PROFILES)));
        Assert.True(Directory.Exists(paths.GetPath(Constants.PATH_CODRIVERS)));
        Assert.True(Directory.Exists(paths.GetPath(Constants.PATH_LANGUAGE)));
    }

    [Fact]
    public void DiscoverLocalizationDirectoriesPreservesRootOverrideOrder()
    {
        var packagedRoot = Directory.CreateDirectory(Path.Combine(_temporaryRoot, "packaged"));
        var userRoot = Directory.CreateDirectory(Path.Combine(_temporaryRoot, "user"));
        var packagedLanguage = Directory.CreateDirectory(
            Path.Combine(packagedRoot.FullName, Constants.PATH_LANGUAGE));
        var userLanguage = Directory.CreateDirectory(
            Path.Combine(userRoot.FullName, Constants.PATH_LANGUAGE));

        var result = ResourceBootstrapService.DiscoverLocalizationDirectories(
            [packagedRoot.FullName, userRoot.FullName, packagedRoot.FullName]);

        Assert.Equal(
            [packagedLanguage.FullName, userLanguage.FullName],
            result);
    }

    [Fact]
    public void ProfileCatalogCreatesDefaultProfile()
    {
        var paths = new TestApplicationPaths(_temporaryRoot);

        var directories = new ProfileCatalogService(paths).GetProfileDirectories();

        Assert.Contains(
            Path.Combine(_temporaryRoot, Constants.PATH_PROFILES, Constants.DEFAULT_PROFILE),
            directories);
    }

    [Fact]
    public void GameCatalogReturnsEmptyWhenPluginDirectoryDoesNotExist()
    {
        var games = new GameCatalogService().Load(
            Path.Combine(_temporaryRoot, "missing-games"));

        Assert.Empty(games);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryRoot))
        {
            Directory.Delete(_temporaryRoot, recursive: true);
        }
    }

    private sealed class TestApplicationPaths(string dataRoot) : IApplicationPaths
    {
        public string DataRoot { get; } = dataRoot;

        public bool IsPortable => true;

        public string GetPath(string path) => Path.Combine(DataRoot, path);

        public void SetDataRootForNextLaunch(string folderPath) =>
            throw new NotSupportedException();
    }
}
