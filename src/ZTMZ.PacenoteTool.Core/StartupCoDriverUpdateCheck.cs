using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.Core;

/// <summary>One optional, bounded catalog check per application instance. Never downloads packages.</summary>
public sealed class StartupCoDriverUpdateCheck(CoDriverPackageUpdateService updates)
{
    private readonly object gate = new();
    private Task<IReadOnlyList<CoDriverPackageUpdateStatus>> check;

    public Task<IReadOnlyList<CoDriverPackageUpdateStatus>> CheckOnceAsync(bool enabled, string catalogUrl,
        IReadOnlyCollection<CoDriverPackageInfo> installed, CancellationToken cancellationToken = default)
    {
        lock (gate)
            return check ??= enabled && installed.Count > 0
                ? CheckAsync(catalogUrl, installed, cancellationToken)
                : Task.FromResult<IReadOnlyList<CoDriverPackageUpdateStatus>>(Array.Empty<CoDriverPackageUpdateStatus>());
    }

    private async Task<IReadOnlyList<CoDriverPackageUpdateStatus>> CheckAsync(string url,
        IReadOnlyCollection<CoDriverPackageInfo> installed, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            var statuses = await updates.CheckForUpdatesAsync(url, installed, timeout.Token).ConfigureAwait(false);
            return statuses.Where(s => s.NeedsUpdate && !s.NeedsDownload)
                .DistinctBy(s => s.Definition.id).ToArray();
        }
        catch (Exception ex)
        {
            NLog.LogManager.GetCurrentClassLogger().Warn(ex, "Startup co-driver update check was unavailable; installed audio is unaffected.");
            return Array.Empty<CoDriverPackageUpdateStatus>();
        }
    }
}
