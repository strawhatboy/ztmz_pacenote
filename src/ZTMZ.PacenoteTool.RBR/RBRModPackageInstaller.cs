#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.RBR;

internal static class RBRModPackageInstaller
{
    private const string StateDirectoryName = ".ztmz-mods";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string ComputeSha256(string path, CancellationToken cancellationToken = default)
    {
        using var stream = File.OpenRead(path);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            hash.AppendData(buffer, 0, read);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    public static string ExtractValidatedPackage(
        string archivePath,
        GameModDefinition mod,
        string operationDirectory,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var actualHash = ComputeSha256(archivePath, cancellationToken);
        if (!string.Equals(actualHash, mod.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Package checksum mismatch. Expected {mod.Sha256}, got {actualHash}.");
        }

        var extractionDirectory = Path.Combine(operationDirectory, "extracted");
        Directory.CreateDirectory(extractionDirectory);
        var extractionRoot = Path.GetFullPath(extractionDirectory) + Path.DirectorySeparatorChar;
        var expectedRoot = NormalizeArchivePath(mod.PackageRootDirectory).Trim('/');
        var extractedFiles = 0;

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var entryNameEncoding = mod.ArchiveEntryCodePage is int codePage
            ? Encoding.GetEncoding(codePage)
            : null;
        using var stream = File.OpenRead(archivePath);
        using var archive = new ZipArchive(
            stream, ZipArchiveMode.Read, leaveOpen: false, entryNameEncoding);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entryPath = NormalizeArchivePath(entry.FullName);
            ValidateArchiveEntry(entryPath, expectedRoot);
            if (entryPath.EndsWith("/", StringComparison.Ordinal))
            {
                continue;
            }

            var destination = Path.GetFullPath(Path.Combine(
                extractionDirectory,
                entryPath.Replace('/', Path.DirectorySeparatorChar)));
            if (!destination.StartsWith(extractionRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Package entry escapes the extraction directory: {entry.FullName}");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: false);
            extractedFiles++;
        }

        if (extractedFiles == 0)
        {
            throw new InvalidDataException("Package does not contain any files.");
        }

        var packageRoot = string.IsNullOrWhiteSpace(mod.PackageRootDirectory)
            ? extractionDirectory
            : Path.Combine(extractionDirectory, mod.PackageRootDirectory);
        if (!Directory.Exists(packageRoot))
        {
            throw new InvalidDataException("Package root directory was not found after extraction.");
        }

        return packageRoot;
    }

    public static RBRManagedModInspection InspectManagedInstallation(string rbrRoot, GameModDefinition mod)
    {
        var manifestPath = GetManifestPath(rbrRoot, mod.Id);
        var manifest = ReadManifest(manifestPath);
        if (manifest is null)
        {
            return new RBRManagedModInspection(false, false, null, null);
        }

        var intact = manifest.Files
            .Where(file => file.OwnedByZtmz)
            .All(file =>
            {
                var destination = ResolveGamePath(rbrRoot, file.RelativePath);
                return File.Exists(destination) && string.Equals(
                    ComputeSha256(destination),
                    file.InstalledSha256,
                    StringComparison.OrdinalIgnoreCase);
            });
        return new RBRManagedModInspection(
            true,
            intact,
            string.IsNullOrWhiteSpace(manifest.Version) ? null : manifest.Version,
            string.IsNullOrWhiteSpace(manifest.PackageSha256) ? null : manifest.PackageSha256);
    }

    public static bool HasManagedInstallation(string rbrRoot, GameModDefinition mod) =>
        File.Exists(GetManifestPath(rbrRoot, mod.Id));

    public static void InstallManaged(
        string packageRoot,
        string rbrRoot,
        GameModDefinition mod,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var manifestPath = GetManifestPath(rbrRoot, mod.Id);
        var previousManifest = ReadManifest(manifestPath);
        EnsurePreviouslyManagedFilesAreSafe(rbrRoot, previousManifest, cancellationToken);

        var packageFiles = Directory.EnumerateFiles(packageRoot, "*", SearchOption.AllDirectories)
            .Select(path => new
            {
                Source = path,
                Relative = Path.GetRelativePath(packageRoot, path)
            })
            .OrderBy(item => item.Relative, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (packageFiles.Count == 0)
        {
            throw new InvalidDataException("Package does not contain any installable files.");
        }

        EnsureFreeSpace(rbrRoot, packageFiles.Select(file => file.Source));

        var stateRoot = GetStateRoot(rbrRoot);
        var permanentBackupRoot = Path.Combine(stateRoot, "backups", mod.Id, "original");
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        var newManifest = new RBRManagedModManifest
        {
            Id = mod.Id,
            Version = mod.Version,
            PackageSha256 = mod.Sha256
        };

        using var transaction = new FileMutationTransaction();
        try
        {
            foreach (var packageFile in packageFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = NormalizeRelativePath(packageFile.Relative);
                var destination = ResolveGamePath(rbrRoot, relative);
                var previous = previousManifest?.Files.FirstOrDefault(file =>
                    string.Equals(file.RelativePath, relative, StringComparison.OrdinalIgnoreCase));

                if (ShouldPreserveExisting(relative, mod) &&
                    File.Exists(destination))
                {
                    newManifest.Files.Add(previous ?? new RBRManagedModFile
                    {
                        RelativePath = relative,
                        InstalledSha256 = ComputeSha256(destination, cancellationToken),
                        OwnedByZtmz = false
                    });
                    continue;
                }

                if (previous is { OwnedByZtmz: false } && File.Exists(destination))
                {
                    newManifest.Files.Add(previous);
                    continue;
                }

                var originalBackupRelativePath = previous?.OriginalBackupRelativePath;
                if (previous is null && File.Exists(destination))
                {
                    var backupPath = Path.Combine(permanentBackupRoot, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                    File.Copy(destination, backupPath, overwrite: true);
                    originalBackupRelativePath = Path.GetRelativePath(stateRoot, backupPath);
                }

                transaction.Capture(destination);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(packageFile.Source, destination, overwrite: true);
                newManifest.Files.Add(new RBRManagedModFile
                {
                    RelativePath = relative,
                    InstalledSha256 = ComputeSha256(destination, cancellationToken),
                    OwnedByZtmz = true,
                    OriginalBackupRelativePath = originalBackupRelativePath
                });
            }

            RemoveObsoleteManagedFiles(
                rbrRoot, stateRoot, previousManifest, newManifest, transaction, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            transaction.Capture(manifestPath);
            WriteManifest(manifestPath, newManifest);
            transaction.Commit();
        }
        catch
        {
            if (previousManifest is null && Directory.Exists(Path.Combine(stateRoot, "backups", mod.Id)))
            {
                Directory.Delete(Path.Combine(stateRoot, "backups", mod.Id), recursive: true);
            }
            throw;
        }
    }

    public static int UninstallManaged(
        string rbrRoot,
        GameModDefinition mod,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var manifestPath = GetManifestPath(rbrRoot, mod.Id);
        var manifest = ReadManifest(manifestPath);
        if (manifest is null)
        {
            return 0;
        }

        var stateRoot = GetStateRoot(rbrRoot);
        var retainedFiles = 0;
        using var transaction = new FileMutationTransaction();
        foreach (var file in manifest.Files.Where(file => file.OwnedByZtmz))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destination = ResolveGamePath(rbrRoot, file.RelativePath);
            if (!File.Exists(destination))
            {
                continue;
            }

            if (!string.Equals(
                    ComputeSha256(destination, cancellationToken),
                    file.InstalledSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                retainedFiles++;
                continue;
            }

            transaction.Capture(destination);
            if (!string.IsNullOrWhiteSpace(file.OriginalBackupRelativePath))
            {
                var backupPath = ResolveStatePath(stateRoot, file.OriginalBackupRelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(backupPath, destination, overwrite: true);
            }
            else
            {
                File.Delete(destination);
            }
        }

        if (retainedFiles == 0)
        {
            transaction.Capture(manifestPath);
            File.Delete(manifestPath);
            transaction.Commit();
            var backupRoot = Path.Combine(stateRoot, "backups", mod.Id);
            if (Directory.Exists(backupRoot))
            {
                Directory.Delete(backupRoot, recursive: true);
            }
        }
        else
        {
            transaction.Commit();
        }

        return retainedFiles;
    }

    public static void AddToJsgme(
        string packageRoot,
        RBRJsgmeRepository repository,
        GameModDefinition mod,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(repository.ModsDirectory);
        var folderName = mod.ExternalFolderName;
        if (string.IsNullOrWhiteSpace(folderName))
        {
            folderName = mod.PackageRootDirectory;
        }
        if (string.IsNullOrWhiteSpace(folderName))
        {
            throw new InvalidDataException("The Mod does not define a JSGME folder name.");
        }

        var destination = Path.Combine(repository.ModsDirectory, folderName);
        if (Directory.Exists(destination))
        {
            return;
        }

        var staging = Path.Combine(repository.ModsDirectory, $".ztmz-stage-{Guid.NewGuid():N}");
        try
        {
            CopyDirectory(packageRoot, staging, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            Directory.Move(staging, destination);
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
    }

    private static void EnsurePreviouslyManagedFilesAreSafe(
        string rbrRoot,
        RBRManagedModManifest? manifest,
        CancellationToken cancellationToken)
    {
        if (manifest is null)
        {
            return;
        }

        foreach (var file in manifest.Files.Where(file => file.OwnedByZtmz))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destination = ResolveGamePath(rbrRoot, file.RelativePath);
            if (!File.Exists(destination))
            {
                continue;
            }

            if (!string.Equals(
                    ComputeSha256(destination, cancellationToken),
                    file.InstalledSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException($"Managed Mod file was changed outside ZTMZ: {file.RelativePath}");
            }
        }
    }

    private static void RemoveObsoleteManagedFiles(
        string rbrRoot,
        string stateRoot,
        RBRManagedModManifest? previous,
        RBRManagedModManifest current,
        FileMutationTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (previous is null)
        {
            return;
        }

        var currentPaths = current.Files.Select(file => file.RelativePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var oldFile in previous.Files.Where(file =>
                     file.OwnedByZtmz && !currentPaths.Contains(file.RelativePath)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destination = ResolveGamePath(rbrRoot, oldFile.RelativePath);
            if (!File.Exists(destination))
            {
                continue;
            }

            if (!string.Equals(
                    ComputeSha256(destination, cancellationToken),
                    oldFile.InstalledSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException($"Obsolete managed Mod file was changed outside ZTMZ: {oldFile.RelativePath}");
            }

            transaction.Capture(destination);
            if (!string.IsNullOrWhiteSpace(oldFile.OriginalBackupRelativePath))
            {
                File.Copy(
                    ResolveStatePath(stateRoot, oldFile.OriginalBackupRelativePath),
                    destination,
                    overwrite: true);
            }
            else
            {
                File.Delete(destination);
            }
        }
    }

    private static void ValidateArchiveEntry(string entryPath, string expectedRoot)
    {
        if (string.IsNullOrWhiteSpace(entryPath) ||
            entryPath.StartsWith("/", StringComparison.Ordinal) ||
            entryPath.Contains(':', StringComparison.Ordinal) ||
            entryPath.Split('/').Any(part => part is ".." or ".") ||
            (expectedRoot.Length > 0 &&
             !(string.Equals(entryPath.TrimEnd('/'), expectedRoot, StringComparison.Ordinal) ||
               entryPath.StartsWith(expectedRoot + "/", StringComparison.Ordinal))))
        {
            throw new InvalidDataException($"Unexpected or unsafe package entry: {entryPath}");
        }
    }

    private static string NormalizeArchivePath(string path) => path.Replace('\\', '/');

    private static string NormalizeRelativePath(string path) =>
        path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

    private static bool ShouldPreserveExisting(string relativePath, GameModDefinition mod) =>
        mod.PreserveExistingPaths?.Any(path => string.Equals(
            NormalizeRelativePath(path), relativePath, StringComparison.OrdinalIgnoreCase)) == true;

    private static void EnsureFreeSpace(string rbrRoot, IEnumerable<string> packageFiles)
    {
        var required = packageFiles.Sum(path => new FileInfo(path).Length);
        var root = Path.GetPathRoot(Path.GetFullPath(rbrRoot));
        if (string.IsNullOrWhiteSpace(root))
            return;

        var available = new DriveInfo(root).AvailableFreeSpace;
        if (available < required * 2 + 16 * 1024 * 1024)
            throw new IOException("There is not enough free disk space to install and back up this Mod safely.");
    }

    private static string ResolveGamePath(string root, string relativePath)
    {
        var rootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var fullPath = Path.GetFullPath(Path.Combine(rootPath, relativePath));
        if (!fullPath.StartsWith(rootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Mod path escapes the RBR directory: {relativePath}");
        }
        return fullPath;
    }

    private static string ResolveStatePath(string stateRoot, string relativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(stateRoot, relativePath));
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(stateRoot));
        if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Backup path escapes the Mod state directory: {relativePath}");
        }
        return fullPath;
    }

    private static string GetStateRoot(string rbrRoot) => Path.Combine(rbrRoot, StateDirectoryName);

    private static string GetManifestPath(string rbrRoot, string modId) =>
        Path.Combine(GetStateRoot(rbrRoot), "manifests", modId + ".json");

    private static RBRManagedModManifest? ReadManifest(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }
        return JsonSerializer.Deserialize<RBRManagedModManifest>(File.ReadAllText(path));
    }

    private static void WriteManifest(string path, RBRManagedModManifest manifest)
    {
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(manifest, JsonOptions));
        File.Move(temporaryPath, path, overwrite: true);
    }

    private static void CopyDirectory(
        string source,
        string destination,
        CancellationToken cancellationToken)
    {
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: false);
        }
    }

    private sealed class FileMutationTransaction : IDisposable
    {
        private readonly string _backupRoot = Path.Combine(
            Path.GetTempPath(),
            $"ztmz-mod-rollback-{Guid.NewGuid():N}");
        private readonly Dictionary<string, (bool Existed, string Backup)> _entries =
            new(StringComparer.OrdinalIgnoreCase);
        private bool _committed;

        public void Capture(string path)
        {
            if (_entries.ContainsKey(path))
            {
                return;
            }

            var backup = Path.Combine(_backupRoot, _entries.Count.ToString("D6", CultureInfo.InvariantCulture));
            var existed = File.Exists(path);
            if (existed)
            {
                Directory.CreateDirectory(_backupRoot);
                File.Copy(path, backup, overwrite: true);
            }
            _entries[path] = (existed, backup);
        }

        public void Commit() => _committed = true;

        public void Dispose()
        {
            if (!_committed)
            {
                foreach (var (path, snapshot) in _entries.Reverse())
                {
                    if (snapshot.Existed)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                        File.Copy(snapshot.Backup, path, overwrite: true);
                    }
                    else if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }
            }

            if (Directory.Exists(_backupRoot))
            {
                Directory.Delete(_backupRoot, recursive: true);
            }
        }
    }
}

internal sealed record RBRManagedModInspection(
    bool Exists,
    bool IsIntact,
    string? Version,
    string? PackageSha256);

internal sealed class RBRManagedModManifest
{
    public string Id { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string PackageSha256 { get; set; } = string.Empty;
    public List<RBRManagedModFile> Files { get; set; } = [];
}

internal sealed class RBRManagedModFile
{
    public string RelativePath { get; set; } = string.Empty;
    public string InstalledSha256 { get; set; } = string.Empty;
    public bool OwnedByZtmz { get; set; }
    public string? OriginalBackupRelativePath { get; set; }
}
