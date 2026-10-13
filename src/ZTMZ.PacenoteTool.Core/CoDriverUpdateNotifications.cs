using System;
using System.Collections.Generic;
using System.Linq;
using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.Core;

/// <summary>Installed packages with pending updates; failed requests preserve the last known state.</summary>
public sealed class CoDriverUpdateNotifications
{
    private readonly object gate = new();
    private Dictionary<string, string> pending = new(StringComparer.Ordinal);
    private long revision;
    public event EventHandler Changed;
    public long Revision { get { lock (gate) return revision; } }
    public int Count { get { lock (gate) return pending.Count; } }

    public void Replace(IEnumerable<CoDriverPackageUpdateStatus> statuses, long expectedRevision)
    {
        lock (gate)
        {
            // An installation completed while this request was in flight.
            if (revision != expectedRevision) return;
            pending = statuses.Where(s => s.NeedsUpdate && !s.NeedsDownload)
                .GroupBy(s => s.Definition.id, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First().Definition.version, StringComparer.Ordinal);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Installed(CoDriverPackageInfo package)
    {
        lock (gate)
        {
            revision++;
            if (package.id != null && pending.TryGetValue(package.id, out var target) &&
                Version.TryParse(package.version, out var installed) &&
                Version.TryParse(target, out var available) && installed >= available)
                pending.Remove(package.id);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
