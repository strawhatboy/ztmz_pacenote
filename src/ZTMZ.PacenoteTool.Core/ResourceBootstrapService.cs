#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ZTMZ.PacenoteTool.Base;
using PacenoteDatabase = ZTMZ.PacenoteTool.Base.Script.ScriptResource;

namespace ZTMZ.PacenoteTool.Core;

public sealed record ResourceBootstrapOptions(
    string? PackagedPacenoteDatabasePath = null,
    IReadOnlyList<string>? LocalizationRoots = null,
    string? Culture = null,
    bool InitializeLocalization = false,
    string? PackagedDefaultCoDriverDirectory = null,
    string? PackagedDefaultProfileDirectory = null);

public sealed record ResourceBootstrapResult(
    PackagedFileProvisioningStatus? ProvisioningStatus,
    int PacenoteCount,
    IReadOnlyList<string> LocalizationDirectories);

public sealed class ResourceBootstrapService
{
    private readonly IApplicationPaths _paths;

    public ResourceBootstrapService(IApplicationPaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public async Task<ResourceBootstrapResult> InitializeAsync(
        ResourceBootstrapOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        EnsureDataDirectories();
        ProvisionPackagedDirectory(
            options.PackagedDefaultCoDriverDirectory,
            _paths.GetPath(Path.Combine(Constants.PATH_CODRIVERS, "default")));
        ProvisionPackagedDirectory(
            options.PackagedDefaultProfileDirectory,
            _paths.GetPath(Path.Combine(Constants.PATH_PROFILES, Constants.DEFAULT_PROFILE)));

        IReadOnlyList<string> localizationDirectories = [];
        if (options.InitializeLocalization)
        {
            localizationDirectories = InitializeLocalization(
                options.LocalizationRoots ?? [],
                options.Culture);
        }

        PackagedFileProvisioningStatus? provisioningStatus = null;
        if (!string.IsNullOrWhiteSpace(options.PackagedPacenoteDatabasePath))
        {
            var provisioning = PackagedFileProvisioner.EnsureCurrent(
                options.PackagedPacenoteDatabasePath,
                _paths.GetPath(PacenoteDatabase.DBNAME));
            provisioningStatus = provisioning.Status;
        }

        await PacenoteDatabase.Instance.LoadData().WaitAsync(cancellationToken);
        return new ResourceBootstrapResult(
            provisioningStatus,
            PacenoteDatabase.Instance.Pacenotes.Count,
            localizationDirectories);
    }

    public void EnsureDataDirectories()
    {
        Directory.CreateDirectory(_paths.DataRoot);
        Directory.CreateDirectory(_paths.GetPath(Constants.PATH_GAMES));
        Directory.CreateDirectory(_paths.GetPath(Constants.PATH_PROFILES));
        Directory.CreateDirectory(_paths.GetPath(Constants.PATH_CODRIVERS));
        Directory.CreateDirectory(_paths.GetPath(Constants.PATH_LANGUAGE));
    }

    private static void ProvisionPackagedDirectory(string? source, string destination)
    {
        if (!string.IsNullOrWhiteSpace(source))
        {
            PackagedFileProvisioner.EnsureCurrentDirectory(source, destination);
        }
    }

    public static IReadOnlyList<string> InitializeLocalization(
        IEnumerable<string> roots,
        string? culture = null)
    {
        var directories = DiscoverLocalizationDirectories(roots);
        I18NLoader.Instance.Initialize(directories.ToList());
        if (!string.IsNullOrWhiteSpace(culture))
        {
            I18NLoader.Instance.SetCulture(culture);
        }

        return directories;
    }

    public static IReadOnlyList<string> DiscoverLocalizationDirectories(
        IEnumerable<string> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);

        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var seenDirectories = new HashSet<string>(comparer);
        var directories = new List<string>();

        foreach (var root in roots.Where(root => !string.IsNullOrWhiteSpace(root)))
        {
            var fullRoot = Path.GetFullPath(root);
            if (!Directory.Exists(fullRoot))
            {
                continue;
            }

            if (Path.GetFileName(fullRoot).Equals(
                    Constants.PATH_LANGUAGE,
                    StringComparison.OrdinalIgnoreCase))
            {
                AddDirectory(fullRoot);
            }

            foreach (var directory in Directory.EnumerateDirectories(
                         fullRoot,
                         Constants.PATH_LANGUAGE,
                         SearchOption.AllDirectories))
            {
                AddDirectory(Path.GetFullPath(directory));
            }
        }

        return directories;

        void AddDirectory(string directory)
        {
            if (seenDirectories.Add(directory))
            {
                directories.Add(directory);
            }
        }
    }
}
