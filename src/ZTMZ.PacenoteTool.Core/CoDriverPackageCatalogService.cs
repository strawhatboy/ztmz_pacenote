#nullable enable

using System;
using System.IO;
using System.Threading.Tasks;
using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.Core;

public sealed record CoDriverPackageImportPlan(
    string PackageDirectoryName,
    string DestinationPath,
    bool DestinationExists);

public sealed class CoDriverPackageCatalogService
{
    private readonly IApplicationPaths _paths;

    public CoDriverPackageCatalogService(IApplicationPaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public CoDriverPackageImportPlan InspectImport(string archivePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);

        var packageDirectoryName = CoDriverPackage.ProbePkgFolderName(archivePath);
        if (string.IsNullOrWhiteSpace(packageDirectoryName))
        {
            throw new InvalidDataException("The co-driver package does not contain a package directory.");
        }

        var coDriversRoot = GetCoDriversRoot();
        var destinationPath = Path.GetFullPath(Path.Combine(coDriversRoot, packageDirectoryName));
        EnsureManagedDestination(coDriversRoot, destinationPath);

        return new CoDriverPackageImportPlan(
            packageDirectoryName,
            destinationPath,
            Directory.Exists(destinationPath));
    }

    public async Task<CoDriverPackage> ImportAsync(
        string archivePath,
        string? existingPackagePath = null)
    {
        var plan = InspectImport(archivePath);
        var coDriversRoot = GetCoDriversRoot();

        if (!string.IsNullOrWhiteSpace(existingPackagePath))
        {
            var existingPath = Path.GetFullPath(existingPackagePath);
            if (IsManagedDestination(coDriversRoot, existingPath) &&
                !PathsEqual(existingPath, plan.DestinationPath) &&
                Directory.Exists(existingPath))
            {
                await Task.Run(() => Directory.Delete(existingPath, true));
            }
        }

        return await CoDriverPackage.Import(archivePath, coDriversRoot)
            ?? throw new InvalidDataException("The co-driver package could not be imported.");
    }

    public Task ExportAsync(CoDriverPackage package, string archivePath)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        return package.Export(archivePath);
    }

    private string GetCoDriversRoot()
    {
        var root = Path.GetFullPath(_paths.GetPath(Constants.PATH_CODRIVERS));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void EnsureManagedDestination(string root, string destination)
    {
        if (!IsManagedDestination(root, destination) || PathsEqual(root, destination))
        {
            throw new InvalidDataException("The co-driver package contains an invalid root directory.");
        }
    }

    private static bool IsManagedDestination(string root, string destination)
    {
        var relativePath = Path.GetRelativePath(root, destination);
        return !Path.IsPathRooted(relativePath) &&
               !relativePath.Equals("..", StringComparison.Ordinal) &&
               !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            Path.TrimEndingDirectorySeparator(left),
            Path.TrimEndingDirectorySeparator(right),
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);
    }
}
