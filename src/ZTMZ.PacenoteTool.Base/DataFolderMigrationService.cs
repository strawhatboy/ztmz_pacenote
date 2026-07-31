using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace ZTMZ.PacenoteTool.Base;

public enum DataFolderMigrationPhase
{
    Copying,
    Verifying
}

public sealed record DataFolderMigrationProgress(
    DataFolderMigrationPhase Phase,
    long BytesProcessed,
    long TotalBytes,
    string RelativePath);

public sealed record DataFolderMigrationResult(
    int FileCount,
    int DirectoryCount,
    long TotalBytes);

public sealed class DataFolderMigrationPlan
{
    internal DataFolderMigrationPlan(
        string sourcePath,
        string destinationPath,
        IReadOnlyList<string> directories,
        IReadOnlyList<DataFolderMigrationFile> files,
        long totalBytes,
        long requiredFreeSpaceBytes,
        long availableFreeSpaceBytes)
    {
        SourcePath = sourcePath;
        DestinationPath = destinationPath;
        Directories = directories;
        Files = files;
        TotalBytes = totalBytes;
        RequiredFreeSpaceBytes = requiredFreeSpaceBytes;
        AvailableFreeSpaceBytes = availableFreeSpaceBytes;
    }

    public string SourcePath { get; }
    public string DestinationPath { get; }
    public int FileCount => Files.Count;
    public int DirectoryCount => Directories.Count;
    public long TotalBytes { get; }
    public long RequiredFreeSpaceBytes { get; }
    public long AvailableFreeSpaceBytes { get; }

    internal IReadOnlyList<string> Directories { get; }
    internal IReadOnlyList<DataFolderMigrationFile> Files { get; }
}

public sealed class InsufficientDataFolderMigrationSpaceException : IOException
{
    public InsufficientDataFolderMigrationSpaceException(long requiredBytes, long availableBytes)
        : base("The destination does not have enough free space for the data-folder migration.")
    {
        RequiredBytes = requiredBytes;
        AvailableBytes = availableBytes;
    }

    public long RequiredBytes { get; }
    public long AvailableBytes { get; }
}

internal sealed record DataFolderMigrationFile(string RelativePath, long Length, DateTime LastWriteTimeUtc);

public sealed class DataFolderMigrationService
{
    private const int BufferSize = 1024 * 1024;
    private const long MinimumFreeSpaceMargin = 16L * 1024 * 1024;
    private const long MaximumFreeSpaceMargin = 512L * 1024 * 1024;

    public DataFolderMigrationPlan CreatePlan(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        var normalizedSourcePath = NormalizeDirectoryPath(sourcePath);
        var normalizedDestinationPath = NormalizeDirectoryPath(destinationPath);

        ValidatePathRelationship(normalizedSourcePath, normalizedDestinationPath);
        ValidateDestinationIsEmpty(normalizedDestinationPath);

        var manifest = BuildManifest(normalizedSourcePath, cancellationToken);
        var requiredFreeSpaceBytes = GetRequiredFreeSpace(manifest.TotalBytes);
        var availableFreeSpaceBytes = GetAvailableFreeSpace(normalizedDestinationPath);

        if (availableFreeSpaceBytes < requiredFreeSpaceBytes)
        {
            throw new InsufficientDataFolderMigrationSpaceException(requiredFreeSpaceBytes, availableFreeSpaceBytes);
        }

        return new DataFolderMigrationPlan(
            normalizedSourcePath,
            normalizedDestinationPath,
            manifest.Directories,
            manifest.Files,
            manifest.TotalBytes,
            requiredFreeSpaceBytes,
            availableFreeSpaceBytes);
    }

    public async Task<DataFolderMigrationResult> CopyAndVerifyAsync(
        DataFolderMigrationPlan plan,
        IProgress<DataFolderMigrationProgress> progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        cancellationToken.ThrowIfCancellationRequested();

        ValidatePathRelationship(plan.SourcePath, plan.DestinationPath);
        ValidateDestinationIsEmpty(plan.DestinationPath);
        Directory.CreateDirectory(plan.DestinationPath);

        foreach (var directory in plan.Directories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.Combine(plan.DestinationPath, directory));
        }

        long copiedBytes = 0;
        foreach (var file in plan.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sourceFilePath = Path.Combine(plan.SourcePath, file.RelativePath);
            var destinationFilePath = Path.Combine(plan.DestinationPath, file.RelativePath);
            var destinationDirectory = Path.GetDirectoryName(destinationFilePath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            copiedBytes += await CopyFileAsync(
                sourceFilePath,
                destinationFilePath,
                file.RelativePath,
                copiedBytes,
                plan.TotalBytes,
                progress,
                cancellationToken);
            File.SetLastWriteTimeUtc(destinationFilePath, file.LastWriteTimeUtc);
        }

        await VerifyAsync(plan, progress, cancellationToken);

        return new DataFolderMigrationResult(plan.FileCount, plan.DirectoryCount, plan.TotalBytes);
    }

    public async Task DeleteSourceDirectoryAsync(
        DataFolderMigrationPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ValidatePathRelationship(plan.SourcePath, plan.DestinationPath);
        await VerifyAsync(plan, progress: null, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        if (Directory.Exists(plan.SourcePath))
        {
            Directory.Delete(plan.SourcePath, recursive: true);
        }
    }

    private static async Task<long> CopyFileAsync(
        string sourceFilePath,
        string destinationFilePath,
        string relativePath,
        long copiedBytes,
        long totalBytes,
        IProgress<DataFolderMigrationProgress> progress,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[BufferSize];
        long copiedFileBytes = 0;

        await using var source = new FileStream(
            sourceFilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var destination = new FileStream(
            destinationFilePath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        while (true)
        {
            var bytesRead = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            copiedFileBytes += bytesRead;
            progress?.Report(new DataFolderMigrationProgress(
                DataFolderMigrationPhase.Copying,
                copiedBytes + copiedFileBytes,
                totalBytes,
                relativePath));
        }

        return copiedFileBytes;
    }

    private static async Task VerifyAsync(
        DataFolderMigrationPlan plan,
        IProgress<DataFolderMigrationProgress> progress,
        CancellationToken cancellationToken)
    {
        var sourceManifest = BuildManifest(plan.SourcePath, cancellationToken);
        var destinationManifest = BuildManifest(plan.DestinationPath, cancellationToken);
        var sourceDirectories = new HashSet<string>(sourceManifest.Directories, StringComparer.OrdinalIgnoreCase);
        var destinationDirectories = new HashSet<string>(destinationManifest.Directories, StringComparer.OrdinalIgnoreCase);
        var plannedDirectories = new HashSet<string>(plan.Directories, StringComparer.OrdinalIgnoreCase);
        if (!plannedDirectories.SetEquals(sourceDirectories))
        {
            throw new IOException("The source data folder changed after the migration started.");
        }

        if (!plannedDirectories.SetEquals(destinationDirectories))
        {
            throw new IOException("The copied folder structure does not match the source data folder.");
        }

        var sourceFiles = sourceManifest.Files.ToDictionary(file => file.RelativePath, StringComparer.OrdinalIgnoreCase);
        var destinationFiles = destinationManifest.Files.ToDictionary(file => file.RelativePath, StringComparer.OrdinalIgnoreCase);
        if (sourceFiles.Count != plan.Files.Count)
        {
            throw new IOException("The source data folder changed after the migration started.");
        }

        if (destinationFiles.Count != plan.Files.Count)
        {
            throw new IOException("The copied file list does not match the source data folder.");
        }

        long verifiedBytes = 0;
        foreach (var sourceFile in plan.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!sourceFiles.TryGetValue(sourceFile.RelativePath, out var currentSourceFile) ||
                currentSourceFile.Length != sourceFile.Length)
            {
                throw new IOException($"The source data folder changed after the migration started: {sourceFile.RelativePath}");
            }

            if (!destinationFiles.TryGetValue(sourceFile.RelativePath, out var destinationFile) ||
                destinationFile.Length != sourceFile.Length)
            {
                throw new IOException($"The copied file does not match the source: {sourceFile.RelativePath}");
            }

            var sourceFilePath = Path.Combine(plan.SourcePath, sourceFile.RelativePath);
            var destinationFilePath = Path.Combine(plan.DestinationPath, sourceFile.RelativePath);
            var sourceHash = await ComputeHashAsync(sourceFilePath, cancellationToken);
            var destinationHash = await ComputeHashAsync(destinationFilePath, cancellationToken);
            if (!CryptographicOperations.FixedTimeEquals(sourceHash, destinationHash))
            {
                throw new IOException($"The copied file hash does not match the source: {sourceFile.RelativePath}");
            }

            verifiedBytes += sourceFile.Length;
            progress?.Report(new DataFolderMigrationProgress(
                DataFolderMigrationPhase.Verifying,
                verifiedBytes,
                plan.TotalBytes,
                sourceFile.RelativePath));
        }
    }

    private static async Task<byte[]> ComputeHashAsync(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var sha256 = SHA256.Create();
        return await sha256.ComputeHashAsync(stream, cancellationToken);
    }

    private static DataFolderMigrationManifest BuildManifest(string rootPath, CancellationToken cancellationToken)
    {
        var directories = new List<string>();
        var files = new List<DataFolderMigrationFile>();
        var pendingDirectories = new Stack<DirectoryInfo>();
        pendingDirectories.Push(new DirectoryInfo(rootPath));
        long totalBytes = 0;

        while (pendingDirectories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pendingDirectories.Pop();

            foreach (var entry in directory.EnumerateFileSystemInfos())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException($"Reparse points are not supported during data-folder migration: {entry.FullName}");
                }

                var relativePath = Path.GetRelativePath(rootPath, entry.FullName);
                if (entry is DirectoryInfo childDirectory)
                {
                    directories.Add(relativePath);
                    pendingDirectories.Push(childDirectory);
                    continue;
                }

                if (entry is FileInfo file)
                {
                    files.Add(new DataFolderMigrationFile(relativePath, file.Length, file.LastWriteTimeUtc));
                    totalBytes += file.Length;
                }
            }
        }

        directories.Sort(StringComparer.OrdinalIgnoreCase);
        files.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.RelativePath, right.RelativePath));

        return new DataFolderMigrationManifest(directories, files, totalBytes);
    }

    private static string NormalizeDirectoryPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A data-folder path is required.", nameof(path));
        }

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    private static void ValidatePathRelationship(string sourcePath, string destinationPath)
    {
        if (!Directory.Exists(sourcePath))
        {
            throw new DirectoryNotFoundException($"The source data folder does not exist: {sourcePath}");
        }

        if (IsFileSystemRoot(sourcePath) || IsFileSystemRoot(destinationPath))
        {
            throw new IOException("A drive or share root cannot be used as a data folder.");
        }

        if (PathsEqual(sourcePath, destinationPath) || IsChildPath(sourcePath, destinationPath) || IsChildPath(destinationPath, sourcePath))
        {
            throw new IOException("The destination must be separate from the current data folder.");
        }
    }

    private static void ValidateDestinationIsEmpty(string destinationPath)
    {
        if (Directory.Exists(destinationPath) && Directory.EnumerateFileSystemEntries(destinationPath).Any())
        {
            throw new IOException("The destination data folder must be empty.");
        }
    }

    private static long GetAvailableFreeSpace(string destinationPath)
    {
        var rootPath = Path.GetPathRoot(destinationPath);
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new IOException($"The destination drive could not be determined: {destinationPath}");
        }

        try
        {
            return new DriveInfo(rootPath).AvailableFreeSpace;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new IOException($"The destination free space could not be determined: {destinationPath}", exception);
        }
    }

    private static long GetRequiredFreeSpace(long totalBytes)
    {
        var margin = Math.Clamp(totalBytes / 100, MinimumFreeSpaceMargin, MaximumFreeSpaceMargin);
        return checked(totalBytes + margin);
    }

    private static bool IsFileSystemRoot(string path)
    {
        var rootPath = Path.GetPathRoot(path);
        return !string.IsNullOrWhiteSpace(rootPath) && PathsEqual(path, rootPath);
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            Path.TrimEndingDirectorySeparator(left),
            Path.TrimEndingDirectorySeparator(right),
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsChildPath(string candidatePath, string parentPath)
    {
        var normalizedParent = Path.TrimEndingDirectorySeparator(parentPath);
        var parentWithSeparator = normalizedParent.EndsWith(Path.DirectorySeparatorChar) ||
                                  normalizedParent.EndsWith(Path.AltDirectorySeparatorChar)
            ? normalizedParent
            : normalizedParent + Path.DirectorySeparatorChar;

        return candidatePath.StartsWith(parentWithSeparator, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record DataFolderMigrationManifest(
        List<string> Directories,
        List<DataFolderMigrationFile> Files,
        long TotalBytes);
}
