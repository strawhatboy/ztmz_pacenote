using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace ZTMZ.PacenoteTool.Base;

public enum PackagedFileProvisioningStatus
{
    Copied,
    Updated,
    Unchanged
}

public sealed record PackagedFileProvisioningResult(
    PackagedFileProvisioningStatus Status,
    string PackageHash);

public static class PackagedFileProvisioner
{
    public const string PackageHashSuffix = ".package.sha256";

    public static PackagedFileProvisioningResult EnsureCurrent(
        string packagedFile,
        string destinationFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagedFile);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationFile);

        var sourcePath = Path.GetFullPath(packagedFile);
        var destinationPath = Path.GetFullPath(destinationFile);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Packaged file was not found.", sourcePath);
        }

        var packageHash = ComputeSha256(sourcePath);
        var hashFile = destinationPath + PackageHashSuffix;
        if (File.Exists(destinationPath) &&
            File.Exists(hashFile) &&
            string.Equals(
                File.ReadAllText(hashFile).Trim(),
                packageHash,
                StringComparison.OrdinalIgnoreCase))
        {
            return new PackagedFileProvisioningResult(
                PackagedFileProvisioningStatus.Unchanged,
                packageHash);
        }

        var destinationExisted = File.Exists(destinationPath);
        var destinationDirectory = Path.GetDirectoryName(destinationPath)
            ?? throw new InvalidOperationException("Destination file has no parent directory.");
        Directory.CreateDirectory(destinationDirectory);

        var temporaryFile = Path.Combine(
            destinationDirectory,
            $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.Copy(sourcePath, temporaryFile, overwrite: true);
            File.Move(temporaryFile, destinationPath, overwrite: true);
            File.WriteAllText(hashFile, packageHash + Environment.NewLine);
        }
        finally
        {
            File.Delete(temporaryFile);
        }

        return new PackagedFileProvisioningResult(
            destinationExisted
                ? PackagedFileProvisioningStatus.Updated
                : PackagedFileProvisioningStatus.Copied,
            packageHash);
    }

    public static PackagedFileProvisioningResult EnsureCurrentDirectory(
        string packagedDirectory,
        string destinationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagedDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);

        var sourcePath = Path.GetFullPath(packagedDirectory);
        var destinationPath = Path.GetFullPath(destinationDirectory);
        if (!Directory.Exists(sourcePath))
        {
            throw new DirectoryNotFoundException(
                $"Packaged directory was not found: {sourcePath}");
        }

        var packageHash = ComputeDirectorySha256(sourcePath);
        var hashFile = Path.Combine(destinationPath, PackageHashSuffix);
        if (Directory.Exists(destinationPath) &&
            File.Exists(hashFile) &&
            string.Equals(
                File.ReadAllText(hashFile).Trim(),
                packageHash,
                StringComparison.OrdinalIgnoreCase))
        {
            return new PackagedFileProvisioningResult(
                PackagedFileProvisioningStatus.Unchanged,
                packageHash);
        }

        var destinationExisted = Directory.Exists(destinationPath);
        Directory.CreateDirectory(destinationPath);
        foreach (var sourceFile in Directory.EnumerateFiles(
                     sourcePath,
                     "*",
                     SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourcePath, sourceFile);
            var destinationFile = Path.Combine(destinationPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
            File.Copy(sourceFile, destinationFile, overwrite: true);
        }

        File.WriteAllText(hashFile, packageHash + Environment.NewLine);
        return new PackagedFileProvisioningResult(
            destinationExisted
                ? PackagedFileProvisioningStatus.Updated
                : PackagedFileProvisioningStatus.Copied,
            packageHash);
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string ComputeDirectorySha256(string path)
    {
        using var combinedHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                     .OrderBy(file => Path.GetRelativePath(path, file), StringComparer.Ordinal))
        {
            var relativePath = Path.GetRelativePath(path, file)
                .Replace(Path.DirectorySeparatorChar, '/');
            combinedHash.AppendData(Encoding.UTF8.GetBytes(relativePath));
            combinedHash.AppendData([0]);
            using var stream = File.OpenRead(file);
            combinedHash.AppendData(SHA256.HashData(stream));
        }

        return Convert.ToHexString(combinedHash.GetHashAndReset());
    }
}
