using System;
using System.IO;
using System.Security.Cryptography;

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

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
