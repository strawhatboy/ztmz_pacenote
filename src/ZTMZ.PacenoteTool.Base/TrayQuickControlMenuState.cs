using System;
using System.Collections.Generic;
using System.Linq;

namespace ZTMZ.PacenoteTool.Base;

public sealed record TrayCodriverMenuOption(
    CoDriverPackageInfo Package,
    string Identity,
    bool IsSelected);

public sealed record TrayQuickControlMenuState(
    IReadOnlyList<TrayCodriverMenuOption> Codrivers,
    bool CanSelectCodriver,
    bool CanPlayExample,
    bool IsMuted,
    bool IsNativeHudVisible,
    bool IsWebDashboardEnabled)
{
    public TrayCodriverMenuOption SelectedCodriver => Codrivers.FirstOrDefault(option => option.IsSelected);
}

public static class TrayQuickControlMenuBuilder
{
    public static TrayQuickControlMenuState Build(
        IEnumerable<CoDriverPackageInfo> codrivers,
        string selectedIdentity,
        bool isStageActive,
        bool isMuted,
        bool isNativeHudVisible,
        bool isWebDashboardEnabled)
    {
        var normalizedSelection = selectedIdentity?.Trim() ?? string.Empty;
        var options = (codrivers ?? Array.Empty<CoDriverPackageInfo>())
            .Where(package => package != null)
            .OrderBy(package => package.name ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(package => package.language ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(package => package.version ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
            .Select(package =>
            {
                var identity = CoDriverSelectionIdentity.Create(package);
                return new TrayCodriverMenuOption(
                    package,
                    identity,
                    string.Equals(identity, normalizedSelection, StringComparison.OrdinalIgnoreCase));
            })
            .ToList();

        var canSelectCodriver = !isStageActive && options.Count > 0;
        var canPlayExample = canSelectCodriver && !isMuted && options.Any(option => option.IsSelected);

        return new TrayQuickControlMenuState(
            options,
            canSelectCodriver,
            canPlayExample,
            isMuted,
            isNativeHudVisible,
            isWebDashboardEnabled);
    }
}
