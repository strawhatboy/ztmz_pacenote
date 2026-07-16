using System;
using System.Collections.Generic;

namespace ZTMZ.PacenoteTool.Base
{
    [Flags]
    public enum DashboardOutputTarget
    {
        None = 0,
        Overlay = 1,
        Web = 2,
        Both = Overlay | Web
    }

    public static class DashboardOutputTargetExtensions
    {
        public static bool Includes(this DashboardOutputTarget target, DashboardOutputTarget destination)
        {
            return (target & destination) == destination;
        }

        public static bool IsValid(this DashboardOutputTarget target)
        {
            return target is DashboardOutputTarget.None
                or DashboardOutputTarget.Overlay
                or DashboardOutputTarget.Web
                or DashboardOutputTarget.Both;
        }
    }

    public static class DashboardOutputTargetResolver
    {
        public static DashboardOutputTarget Resolve(
            IReadOnlyDictionary<string, DashboardOutputTarget> outputTargets,
            string dashboardId,
            bool legacyEnabled)
        {
            if (!string.IsNullOrWhiteSpace(dashboardId)
                && outputTargets != null
                && outputTargets.TryGetValue(dashboardId, out var target)
                && target.IsValid())
            {
                return target;
            }

            return legacyEnabled ? DashboardOutputTarget.Both : DashboardOutputTarget.None;
        }
    }
}
