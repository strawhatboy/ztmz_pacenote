#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.Core;

public sealed class ProfileCatalogService
{
    private readonly IApplicationPaths _paths;

    public ProfileCatalogService(IApplicationPaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public IReadOnlyList<string> GetProfileDirectories()
    {
        var profilesRoot = _paths.GetPath(Constants.PATH_PROFILES);
        Directory.CreateDirectory(profilesRoot);
        Directory.CreateDirectory(Path.Combine(profilesRoot, Constants.DEFAULT_PROFILE));
        return Directory.EnumerateDirectories(profilesRoot).ToList();
    }

    public IReadOnlyList<string> GetCoDriverDirectories()
    {
        var coDriversRoot = _paths.GetPath(Constants.PATH_CODRIVERS);
        Directory.CreateDirectory(coDriversRoot);

        var directories = Directory.EnumerateDirectories(coDriversRoot).ToList();
        if (!string.IsNullOrWhiteSpace(Config.Instance.AdditionalCoDriverPackagesSearchPath) &&
            Directory.Exists(Config.Instance.AdditionalCoDriverPackagesSearchPath))
        {
            directories.AddRange(Directory.EnumerateDirectories(
                Config.Instance.AdditionalCoDriverPackagesSearchPath));
        }

        return directories
            .Distinct(PathComparer)
            .ToList();
    }

    public async Task<IReadOnlyDictionary<string, CoDriverPackage>> LoadCoDriversAsync(
        CancellationToken cancellationToken = default)
    {
        var directories = GetCoDriverDirectories();
        var packages = await Task.WhenAll(directories.Select(async directory =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new KeyValuePair<string, CoDriverPackage>(
                directory,
                await CoDriverPackage.Load(directory));
        }));

        return packages.ToDictionary(pair => pair.Key, pair => pair.Value, PathComparer);
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
}
