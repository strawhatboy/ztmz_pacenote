using System.Reflection;
using NAudio.Wave;
using ZTMZ.PacenoteTool.Base;
using ScriptResource = ZTMZ.PacenoteTool.Base.Script.ScriptResource;
using ZTMZ.PacenoteTool.WpfGUI.ViewModels;

namespace ZTMZ.PacenoteTool.Tests.Base;

[CollectionDefinition("CoDriverPackageFiles", DisableParallelization = true)]
public sealed class CoDriverPackageFilesCollection;

[Collection("CoDriverPackageFiles")]
public sealed class CoDriverPackageFilesTest
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DetailsExposePlayableFilesWithEitherPreloadSetting(bool preload)
    {
        using var fixture = new PackageFixture(preload);
        await ScriptResource.Instance.LoadData();
        var resource = ScriptResource.Instance;
        var direct = fixture.WriteAudio("1_left.wav");
        var first = fixture.WriteAudio("1_right/first.wav");
        var second = fixture.WriteAudio("1_right/second.wav");
        var alias = resource.FilenameDict.First(item => item.Value.Count > 1 &&
            !item.Value.Contains("1_left") && !item.Value.Contains("1_right") && !item.Value.Contains("2_left"));
        var aliasFile = fixture.WriteAudio(alias.Value.Last() + ".wav");

        var package = await CoDriverPackage.Load(fixture.PackageDirectory);

        // Both desktop detail pages consume this index, including when playback is cached.
        Assert.Equal(direct, Assert.Single(package.id2tokensPath[resource.FilenameToIdDict["1_left"]]));
        Assert.Equal(new[] { first, second }.Order(),
            package.id2tokensPath[resource.FilenameToIdDict["1_right"]].Order());
        Assert.Contains(aliasFile, package.id2tokensPath[alias.Key]);
        Assert.False(package.id2tokensPath.ContainsKey(resource.FilenameToIdDict["2_left"]));

        if (preload)
        {
            Assert.False(Assert.Single(package.id2tokens[resource.FilenameToIdDict["1_left"]]).IsEmpty);
            Assert.Equal(2, package.id2tokens[resource.FilenameToIdDict["1_right"]].Count);
        }
        else
        {
            Assert.Empty(package.id2tokens);
        }

        var buildDetails = typeof(VoicePackagePageVM).GetMethod("BuildDataContent",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        var rows = Assert.IsType<List<object>>(buildDetails.Invoke(null, [package]));
        var row = rows.Single(item => (string)Property(item, "Token") == "1_right");
        Assert.Equal(2, Property(row, "FilesCount"));
        var previewPaths = Assert.IsType<List<object>>(Property(row, "Files"))
            .Select(item => (string)Property(item, "FilePath")).Order().ToArray();
        Assert.Equal(new[] { first, second }.Order(), previewPaths);
        Assert.All(previewPaths, path => Assert.False(new AutoResampledCachedSound(path).IsEmpty));
        var missing = rows.Single(item => (string)Property(item, "Token") == "2_left");
        Assert.Equal(0, Property(missing, "FilesCount"));
        Assert.Empty(Assert.IsType<List<object>>(Property(missing, "Files")));

        // Reloading after replacing package contents must not keep obsolete preview paths.
        File.Delete(second);
        var replacement = fixture.WriteAudio("1_right/replacement.wav");
        var reloaded = await CoDriverPackage.Load(fixture.PackageDirectory);
        Assert.Equal(new[] { first, replacement }.Order(),
            reloaded.id2tokensPath[resource.FilenameToIdDict["1_right"]].Order());
    }

    private static object Property(object target, string name) =>
        target.GetType().GetProperty(name)!.GetValue(target)!;

    private sealed class PackageFixture : IDisposable, IConfigurationLocation
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "ztmz-package-files-" + Guid.NewGuid());
        private readonly List<(FieldInfo Field, object? Previous)> singletons = [];
        private readonly IList<string> previousAudioTypes;
        public string PackageDirectory => Path.Combine(root, "codrivers", "test");

        public PackageFixture(bool preload)
        {
            Directory.CreateDirectory(PackageDirectory);
            var repository = new DirectoryInfo(AppContext.BaseDirectory);
            while (repository != null && !File.Exists(Path.Combine(repository.FullName,
                       "src", "ZTMZ.PacenoteTool.Base", "data.zdb")))
                repository = repository.Parent;
            Assert.NotNull(repository);
            File.Copy(Path.Combine(repository.FullName, "src", "ZTMZ.PacenoteTool.Base", "data.zdb"),
                Path.Combine(root, "data.zdb"));
            // Keep the real database/configuration and any previously initialized singletons untouched.
            ReplaceSingleton(typeof(Config), Activator.CreateInstance(typeof(Config), nonPublic: true));
            ReplaceSingleton(typeof(AppLevelVariables), new AppLevelVariables(this));
            ReplaceSingleton(typeof(ScriptResource), null);
            Config.Instance.PreloadSounds = preload;
            Config.Instance.SupportedAudioTypes = ["*.wav", "*.ogg", "*.mp3"];
            previousAudioTypes = Constants.SupportedAudioTypes;
            Constants.SupportedAudioTypes = Config.Instance.SupportedAudioTypes;
        }

        public string WriteAudio(string relativePath)
        {
            var path = Path.GetFullPath(Path.Combine(PackageDirectory, relativePath));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(44100, 2));
            writer.WriteSamples(Enumerable.Repeat(0.25f, 882).ToArray(), 0, 882);
            return path;
        }

        public string GetConfiguredDataRoot() => root;
        public void SetConfiguredDataRoot(string path) => throw new NotSupportedException();

        private void ReplaceSingleton(Type type, object? replacement)
        {
            var field = type.GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
            singletons.Add((field, field.GetValue(null)));
            field.SetValue(null, replacement);
        }

        public void Dispose()
        {
            var connection = ScriptResource.LoadedInstance?.Connection;
            connection?.Dispose();
            if (connection != null)
                Microsoft.Data.Sqlite.SqliteConnection.ClearPool(connection);
            foreach (var (field, previous) in singletons.AsEnumerable().Reverse())
                field.SetValue(null, previous);
            Constants.SupportedAudioTypes = previousAudioTypes;
            Directory.Delete(root, true);
        }
    }
}
