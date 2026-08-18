using ZTMZ.PacenoteTool.Base;
using Xunit;

namespace ZTMZ.PacenoteTool.Portable.Tests;

public sealed class PackagedFileProvisionerTest
{
    [Fact]
    public void EnsureCurrent_CopiesThenPreservesProvisionedFileUntilPackageChanges()
    {
        using var directory = new TemporaryDirectory();
        var packagedFile = Path.Combine(directory.Path, "package", "data.zdb");
        var destinationFile = Path.Combine(directory.Path, "data", "data.zdb");
        Directory.CreateDirectory(Path.GetDirectoryName(packagedFile)!);
        File.WriteAllText(packagedFile, "version-one");

        var copied = PackagedFileProvisioner.EnsureCurrent(packagedFile, destinationFile);
        Assert.Equal(PackagedFileProvisioningStatus.Copied, copied.Status);
        Assert.Equal("version-one", File.ReadAllText(destinationFile));

        File.WriteAllText(destinationFile, "runtime-migration");
        var unchanged = PackagedFileProvisioner.EnsureCurrent(packagedFile, destinationFile);
        Assert.Equal(PackagedFileProvisioningStatus.Unchanged, unchanged.Status);
        Assert.Equal("runtime-migration", File.ReadAllText(destinationFile));

        File.WriteAllText(packagedFile, "version-two");
        var updated = PackagedFileProvisioner.EnsureCurrent(packagedFile, destinationFile);
        Assert.Equal(PackagedFileProvisioningStatus.Updated, updated.Status);
        Assert.Equal("version-two", File.ReadAllText(destinationFile));
    }

    [Fact]
    public void EnsureCurrentDirectory_CopiesTreeAndOnlyUpdatesWhenPackageChanges()
    {
        using var directory = new TemporaryDirectory();
        var package = Path.Combine(directory.Path, "package", "default");
        var destination = Path.Combine(directory.Path, "data", "default");
        Directory.CreateDirectory(Path.Combine(package, "tokens"));
        File.WriteAllText(Path.Combine(package, "info.json"), "version-one");
        File.WriteAllText(Path.Combine(package, "tokens", "left.wav"), "audio-one");

        var copied = PackagedFileProvisioner.EnsureCurrentDirectory(package, destination);
        Assert.Equal(PackagedFileProvisioningStatus.Copied, copied.Status);
        Assert.Equal("audio-one", File.ReadAllText(Path.Combine(destination, "tokens", "left.wav")));

        File.WriteAllText(Path.Combine(destination, "tokens", "left.wav"), "runtime-change");
        var unchanged = PackagedFileProvisioner.EnsureCurrentDirectory(package, destination);
        Assert.Equal(PackagedFileProvisioningStatus.Unchanged, unchanged.Status);
        Assert.Equal("runtime-change", File.ReadAllText(Path.Combine(destination, "tokens", "left.wav")));

        File.WriteAllText(Path.Combine(package, "tokens", "left.wav"), "audio-two");
        var updated = PackagedFileProvisioner.EnsureCurrentDirectory(package, destination);
        Assert.Equal(PackagedFileProvisioningStatus.Updated, updated.Status);
        Assert.Equal("audio-two", File.ReadAllText(Path.Combine(destination, "tokens", "left.wav")));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"ZTMZPackagedFileTests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
