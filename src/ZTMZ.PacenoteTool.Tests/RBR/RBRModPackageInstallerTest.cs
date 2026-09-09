using System.IO.Compression;
using System.Text;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.RBR;

namespace ZTMZ.PacenoteTool.Tests.RBR;

public class RBRModPackageInstallerTest
{
    [Fact]
    public void ExtractsCp936PackageDirectlyFromZipRoot()
    {
        using var directory = new TemporaryDirectory();
        var archivePath = Path.Combine(directory.Path, "mod.zip");
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        using (var stream = File.Create(archivePath))
        using (var archive = new ZipArchive(
                   stream, ZipArchiveMode.Create, false, Encoding.GetEncoding(936)))
        {
            var entry = archive.CreateEntry("Plugins/路书/配置.ini");
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write("test");
        }

        var mod = CreateMod(
            RBRModPackageInstaller.ComputeSha256(archivePath),
            packageRoot: string.Empty,
            encoding: 936,
            externalFolderName: "Luppis 中文语音");
        var operation = Path.Combine(directory.Path, "operation");
        Directory.CreateDirectory(operation);

        var packageRoot = RBRModPackageInstaller.ExtractValidatedPackage(archivePath, mod, operation);

        Assert.Equal(Path.Combine(operation, "extracted"), packageRoot);
        Assert.True(File.Exists(Path.Combine(packageRoot, "Plugins", "路书", "配置.ini")));
    }

    [Fact]
    public void ManagedInstallPreservesDeclaredConfigAndRestoresOriginalFiles()
    {
        using var directory = new TemporaryDirectory();
        var gameRoot = Path.Combine(directory.Path, "game");
        var packageRoot = Path.Combine(directory.Path, "package");
        Directory.CreateDirectory(Path.Combine(gameRoot, "Plugins", "Example"));
        Directory.CreateDirectory(Path.Combine(packageRoot, "Plugins", "Example"));
        var gameBinary = Path.Combine(gameRoot, "Plugins", "Example", "plugin.dll");
        var gameConfig = Path.Combine(gameRoot, "Plugins", "Example", "settings.ini");
        File.WriteAllText(gameBinary, "original");
        File.WriteAllText(gameConfig, "user settings");
        File.WriteAllText(Path.Combine(packageRoot, "Plugins", "Example", "plugin.dll"), "new binary");
        File.WriteAllText(Path.Combine(packageRoot, "Plugins", "Example", "settings.ini"), "defaults");
        var mod = CreateMod("0".PadLeft(64, '0'), preserve: ["Plugins/Example/settings.ini"]);

        RBRModPackageInstaller.InstallManaged(packageRoot, gameRoot, mod);

        Assert.Equal("new binary", File.ReadAllText(gameBinary));
        Assert.Equal("user settings", File.ReadAllText(gameConfig));
        var inspection = RBRModPackageInstaller.InspectManagedInstallation(gameRoot, mod);
        Assert.True(inspection.IsIntact);
        Assert.Equal(mod.Sha256, inspection.PackageSha256);

        var retained = RBRModPackageInstaller.UninstallManaged(gameRoot, mod);

        Assert.Equal(0, retained);
        Assert.Equal("original", File.ReadAllText(gameBinary));
        Assert.Equal("user settings", File.ReadAllText(gameConfig));
    }

    [Fact]
    public void ExtractionRejectsEntryOutsidePackageRoot()
    {
        using var directory = new TemporaryDirectory();
        var archivePath = Path.Combine(directory.Path, "unsafe.zip");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("../outside.txt");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("unsafe");
        }
        var mod = CreateMod(
            RBRModPackageInstaller.ComputeSha256(archivePath),
            packageRoot: string.Empty);
        var operation = Path.Combine(directory.Path, "operation");
        Directory.CreateDirectory(operation);

        Assert.Throws<InvalidDataException>(() =>
            RBRModPackageInstaller.ExtractValidatedPackage(archivePath, mod, operation));
        Assert.False(File.Exists(Path.Combine(directory.Path, "outside.txt")));
    }

    [Fact]
    public void ExtractionHonorsCancellationBeforeWritingFiles()
    {
        using var directory = new TemporaryDirectory();
        var archivePath = Path.Combine(directory.Path, "mod.zip");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("Plugins/plugin.dll");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("plugin");
        }
        var mod = CreateMod(RBRModPackageInstaller.ComputeSha256(archivePath), packageRoot: string.Empty);
        var operation = Path.Combine(directory.Path, "operation");
        Directory.CreateDirectory(operation);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            RBRModPackageInstaller.ExtractValidatedPackage(
                archivePath, mod, operation, cancellation.Token));
        Assert.False(Directory.Exists(Path.Combine(operation, "extracted")));
    }

    [Fact]
    public void UninstallRetainsFilesChangedAfterManagedInstall()
    {
        using var directory = new TemporaryDirectory();
        var gameRoot = Path.Combine(directory.Path, "game");
        var packageRoot = Path.Combine(directory.Path, "package");
        Directory.CreateDirectory(Path.Combine(packageRoot, "Plugins"));
        var packageFile = Path.Combine(packageRoot, "Plugins", "plugin.dll");
        File.WriteAllText(packageFile, "installed");
        var mod = CreateMod("0".PadLeft(64, '0'));
        RBRModPackageInstaller.InstallManaged(packageRoot, gameRoot, mod);
        var installedFile = Path.Combine(gameRoot, "Plugins", "plugin.dll");
        File.WriteAllText(installedFile, "user modified");

        var retained = RBRModPackageInstaller.UninstallManaged(gameRoot, mod);

        Assert.Equal(1, retained);
        Assert.Equal("user modified", File.ReadAllText(installedFile));
        Assert.False(RBRModPackageInstaller.HasManagedInstallation(gameRoot, mod));
        RBRModPackageInstaller.InstallManaged(packageRoot, gameRoot, mod);
        Assert.True(RBRModPackageInstaller.InspectManagedInstallation(gameRoot, mod).IsIntact);
        Assert.Equal(0, RBRModPackageInstaller.UninstallManaged(gameRoot, mod));
        Assert.Equal("user modified", File.ReadAllText(installedFile));
    }

    [Fact]
    public void UninstallRecoversPartialUninstallAndArchivesOriginalConfig()
    {
        using var directory = new TemporaryDirectory();
        var gameRoot = Path.Combine(directory.Path, "game");
        var packageRoot = Path.Combine(directory.Path, "package");
        Directory.CreateDirectory(Path.Combine(packageRoot, "Plugins"));
        Directory.CreateDirectory(Path.Combine(gameRoot, "Plugins"));
        var config = Path.Combine(gameRoot, "Plugins", "settings.ini");
        File.WriteAllText(config, "original");
        File.WriteAllText(Path.Combine(packageRoot, "Plugins", "settings.ini"), "defaults");
        File.WriteAllText(Path.Combine(packageRoot, "Plugins", "plugin.dll"), "binary");
        var mod = CreateMod(new string('0', 64));
        RBRModPackageInstaller.InstallManaged(packageRoot, gameRoot, mod);
        File.WriteAllText(config, "user settings");
        // Reproduce the previous uninstaller leaving only a changed config.
        File.Delete(Path.Combine(gameRoot, "Plugins", "plugin.dll"));

        Assert.Equal(1, RBRModPackageInstaller.UninstallManaged(gameRoot, mod));
        Assert.False(RBRModPackageInstaller.HasManagedInstallation(gameRoot, mod));
        Assert.Equal("user settings", File.ReadAllText(config));
        var archivedConfig = Assert.Single(Directory.GetFiles(
            Path.Combine(gameRoot, ".ztmz-mods", "uninstalled"), "settings.ini", SearchOption.AllDirectories));
        Assert.Equal("original", File.ReadAllText(archivedConfig));
        Assert.Equal(0, RBRModPackageInstaller.UninstallManaged(gameRoot, mod));
        RBRModPackageInstaller.InstallManaged(packageRoot, gameRoot, mod);
        Assert.True(RBRModPackageInstaller.InspectManagedInstallation(gameRoot, mod).IsIntact);
        Assert.Equal("original", File.ReadAllText(archivedConfig));
    }

    [Fact]
    public void JsgmeLocatorUsesConfiguredRepositoryAndModsSection()
    {
        using var directory = new TemporaryDirectory();
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var encoding = Encoding.GetEncoding(936);
        var executable = Path.Combine(directory.Path, "JSGME MOD集合程序.exe");
        File.WriteAllText(executable, string.Empty);
        File.WriteAllText(Path.Combine(directory.Path, "JSGME.ini"),
            "[MODS FOLDER]\r\nName=MODS 插件集合包\r\n", encoding);
        var repositoryPath = Path.Combine(directory.Path, "MODS 插件集合包");
        Directory.CreateDirectory(repositoryPath);
        File.WriteAllText(Path.Combine(repositoryPath, "JSGME.ini"),
            "[MODS]\r\nLuppis 中文语音=1\r\n旧日志但未启用=0\r\n", encoding);

        var found = RBRJsgmeRepositoryLocator.TryLocate(directory.Path, out var repository);

        Assert.True(found);
        Assert.Equal(repositoryPath, repository.ModsDirectory);
        Assert.Contains("Luppis 中文语音", repository.EnabledMods);
        Assert.DoesNotContain("旧日志但未启用", repository.EnabledMods);
    }

    [Fact]
    public void FreshJsgmeWithoutIniUsesDefaultModsDirectory()
    {
        using var directory = new TemporaryDirectory();
        var executable = Path.Combine(directory.Path, "JSGME.exe");
        File.WriteAllText(executable, string.Empty);

        var found = RBRJsgmeRepositoryLocator.TryLocate(directory.Path, out var repository);

        Assert.True(found);
        Assert.Equal(Path.Combine(directory.Path, "MODS"), repository.ModsDirectory);
        Assert.Empty(repository.EnabledMods);
    }

    private static GameModDefinition CreateMod(
        string hash,
        string packageRoot = "Package",
        int? encoding = null,
        string? externalFolderName = null,
        IReadOnlyList<string>? preserve = null) => new(
            "test-mod",
            new Dictionary<string, string> { ["en-us"] = "Test" },
            new Dictionary<string, string>(),
            "author",
            "1.0",
            new Uri("https://example.com/mod.zip"),
            new Uri("https://example.com/release"),
            hash,
            packageRoot,
            externalFolderName,
            ArchiveEntryCodePage: encoding,
            PreserveExistingPaths: preserve);

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"ztmz-rbr-mod-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
