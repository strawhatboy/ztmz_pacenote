using System;
using System.Collections.Generic;
using System.Linq;

namespace ZTMZ.PacenoteTool.Base;

/// <summary>
/// A rendered replay point. The source telemetry remains untouched.
/// </summary>
public readonly record struct ReplayPlotPoint(float Distance, float Value);

/// <summary>
/// Produces bounded plot data for replay analysis without discarding the raw replay samples.
/// </summary>
public static class ReplayPlotSampler
{
    public const int DefaultMaximumPoints = 2048;

    public static IReadOnlyList<ReplayPlotPoint> DownsampleByDistance(
        IEnumerable<ReplayDetailsPerTime> details,
        Func<ReplayDetailsPerTime, float> valueSelector,
        int maximumPoints = DefaultMaximumPoints)
    {
        ArgumentNullException.ThrowIfNull(valueSelector);
        if (maximumPoints < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumPoints), "At least two points are required.");
        }

        var points = details?
            .Where(detail => detail != null)
            .Select(detail => new ReplayPlotPoint(detail.distance, valueSelector(detail)))
            .Where(point => IsFinite(point.Distance) && IsFinite(point.Value))
            .OrderBy(point => point.Distance)
            .ToList() ?? new List<ReplayPlotPoint>();

        if (points.Count <= maximumPoints)
        {
            return points;
        }

        if (maximumPoints == 2)
        {
            return new[] { points[0], points[^1] };
        }

        if (maximumPoints == 3)
        {
            var middle = points
                .Select((point, index) => new { point, index })
                .OrderByDescending(item => Math.Abs(item.point.Value))
                .First().index;
            return new[] { 0, middle, points.Count - 1 }
                .Distinct()
                .OrderBy(index => index)
                .Select(index => points[index])
                .ToList();
        }

        var selected = new HashSet<int> { 0, points.Count - 1 };
        var bucketCount = Math.Max(1, (maximumPoints - 2) / 2);
        var minimumDistance = points[0].Distance;
        var distanceRange = points[^1].Distance - minimumDistance;
        var buckets = new List<int>[bucketCount];

        for (var index = 0; index < points.Count; index++)
        {
            var bucket = distanceRange <= float.Epsilon
                ? Math.Min(bucketCount - 1, index * bucketCount / points.Count)
                : Math.Min(bucketCount - 1, (int)((points[index].Distance - minimumDistance) / distanceRange * bucketCount));

            (buckets[bucket] ??= new List<int>()).Add(index);
        }

        foreach (var bucket in buckets)
        {
            if (bucket == null || bucket.Count == 0)
            {
                continue;
            }

            var minimum = bucket[0];
            var maximum = bucket[0];
            foreach (var index in bucket)
            {
                if (points[index].Value < points[minimum].Value)
                {
                    minimum = index;
                }

                if (points[index].Value > points[maximum].Value)
                {
                    maximum = index;
                }
            }

            selected.Add(minimum);
            selected.Add(maximum);
        }

        return selected
            .OrderBy(index => index)
            .Take(maximumPoints)
            .Select(index => points[index])
            .ToList();
    }

    public static double NormalizeDistance(float distance, float maximumDistance)
    {
        if (!IsFinite(distance) || !IsFinite(maximumDistance) || maximumDistance <= 0)
        {
            return 0;
        }

        return Math.Clamp(distance / maximumDistance, 0, 1);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
