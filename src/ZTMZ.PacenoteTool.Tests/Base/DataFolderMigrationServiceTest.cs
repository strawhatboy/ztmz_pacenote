using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.Tests.Base;

public class DataFolderMigrationServiceTest
{
    [Fact]
    public async Task CopyAndVerifyAsync_CopiesNestedFilesAndEmptyDirectories()
    {
        using var directory = new TemporaryDirectory();
        var sourcePath = Path.Combine(directory.Path, "source");
        var destinationPath = Path.Combine(directory.Path, "destination");
        Directory.CreateDirectory(Path.Combine(sourcePath, "nested", "empty"));
        Directory.CreateDirectory(Path.Combine(sourcePath, "nested", "files"));
        await File.WriteAllTextAsync(Path.Combine(sourcePath, "root.txt"), "root data");
        await File.WriteAllTextAsync(Path.Combine(sourcePath, "nested", "files", "child.txt"), "child data");

        var service = new DataFolderMigrationService();
        var plan = service.CreatePlan(sourcePath, destinationPath);

        var result = await service.CopyAndVerifyAsync(plan);

        Assert.Equal(2, result.FileCount);
        Assert.True(Directory.Exists(Path.Combine(destinationPath, "nested", "empty")));
        Assert.Equal("root data", await File.ReadAllTextAsync(Path.Combine(destinationPath, "root.txt")));
        Assert.Equal("child data", await File.ReadAllTextAsync(Path.Combine(destinationPath, "nested", "files", "child.txt")));
    }

    [Fact]
    public void CreatePlan_RejectsANonEmptyDestination()
    {
        using var directory = new TemporaryDirectory();
        var sourcePath = Path.Combine(directory.Path, "source");
        var destinationPath = Path.Combine(directory.Path, "destination");
        Directory.CreateDirectory(sourcePath);
        Directory.CreateDirectory(destinationPath);
        File.WriteAllText(Path.Combine(sourcePath, "source.txt"), "source");
        File.WriteAllText(Path.Combine(destinationPath, "existing.txt"), "existing");

        var service = new DataFolderMigrationService();

        Assert.Throws<IOException>(() => service.CreatePlan(sourcePath, destinationPath));
    }

    [Fact]
    public async Task CopyAndVerifyAsync_HonorsCancellationBeforeWritingTheDestination()
    {
        using var directory = new TemporaryDirectory();
        var sourcePath = Path.Combine(directory.Path, "source");
        var destinationPath = Path.Combine(directory.Path, "destination");
        Directory.CreateDirectory(sourcePath);
        await File.WriteAllTextAsync(Path.Combine(sourcePath, "source.txt"), "source");

        var service = new DataFolderMigrationService();
        var plan = service.CreatePlan(sourcePath, destinationPath);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CopyAndVerifyAsync(plan, cancellationToken: cancellation.Token));
        Assert.False(Directory.Exists(destinationPath));
    }

    [Fact]
    public async Task CopyAndVerifyAsync_RejectsFilesAddedAfterThePlanWasCreated()
    {
        using var directory = new TemporaryDirectory();
        var sourcePath = Path.Combine(directory.Path, "source");
        var destinationPath = Path.Combine(directory.Path, "destination");
        Directory.CreateDirectory(sourcePath);
        await File.WriteAllTextAsync(Path.Combine(sourcePath, "source.txt"), "source");

        var service = new DataFolderMigrationService();
        var plan = service.CreatePlan(sourcePath, destinationPath);
        await File.WriteAllTextAsync(Path.Combine(sourcePath, "added-after-scan.txt"), "new data");

        await Assert.ThrowsAsync<IOException>(() => service.CopyAndVerifyAsync(plan));
        Assert.True(Directory.Exists(sourcePath));
    }

    [Fact]
    public async Task DeleteSourceDirectoryAsync_RefusesToDeleteWhenTheSourceChanged()
    {
        using var directory = new TemporaryDirectory();
        var sourcePath = Path.Combine(directory.Path, "source");
        var destinationPath = Path.Combine(directory.Path, "destination");
        Directory.CreateDirectory(sourcePath);
        await File.WriteAllTextAsync(Path.Combine(sourcePath, "source.txt"), "source");

        var service = new DataFolderMigrationService();
        var plan = service.CreatePlan(sourcePath, destinationPath);
        await service.CopyAndVerifyAsync(plan);
        await File.WriteAllTextAsync(Path.Combine(sourcePath, "added-after-copy.txt"), "new data");

        await Assert.ThrowsAsync<IOException>(() => service.DeleteSourceDirectoryAsync(plan));
        Assert.True(Directory.Exists(sourcePath));
        Assert.True(File.Exists(Path.Combine(sourcePath, "added-after-copy.txt")));
    }

    [Fact]
    public async Task DeleteSourceDirectoryAsync_DeletesAReverifiedSource()
    {
        using var directory = new TemporaryDirectory();
        var sourcePath = Path.Combine(directory.Path, "source");
        var destinationPath = Path.Combine(directory.Path, "destination");
        Directory.CreateDirectory(sourcePath);
        await File.WriteAllTextAsync(Path.Combine(sourcePath, "source.txt"), "source");

        var service = new DataFolderMigrationService();
        var plan = service.CreatePlan(sourcePath, destinationPath);
        await service.CopyAndVerifyAsync(plan);

        await service.DeleteSourceDirectoryAsync(plan);

        Assert.False(Directory.Exists(sourcePath));
        Assert.Equal("source", await File.ReadAllTextAsync(Path.Combine(destinationPath, "source.txt")));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ZTMZPacenoteDataMigrationTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
