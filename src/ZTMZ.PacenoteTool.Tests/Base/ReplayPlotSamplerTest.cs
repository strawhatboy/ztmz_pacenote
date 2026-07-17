using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.Tests.Base;

public class ReplayPlotSamplerTest
{
    [Fact]
    public void DownsampleByDistanceCapsPointsAndPreservesExtrema()
    {
        var details = Enumerable.Range(0, 1_000)
            .Select(index => new ReplayDetailsPerTime
            {
                distance = index,
                time = index,
                speed = index == 501 ? 9_999 : index % 200
            })
            .ToList();

        var sampled = ReplayPlotSampler.DownsampleByDistance(details, detail => detail.speed, 32);

        Assert.True(sampled.Count <= 32);
        Assert.Equal(0, sampled.First().Distance);
        Assert.Equal(999, sampled.Last().Distance);
        Assert.Contains(sampled, point => point.Distance == 501 && point.Value == 9_999);
        Assert.Equal(1_000, details.Count);
        Assert.Equal(9_999, details[501].speed);
    }

    [Fact]
    public void DownsampleByDistanceOrdersDuplicateAndReverseDistanceSamples()
    {
        var details = new List<ReplayDetailsPerTime>
        {
            new() { distance = 100, speed = 50 },
            new() { distance = 25, speed = 10 },
            new() { distance = 25, speed = 20 },
            new() { distance = 75, speed = 40 },
            new() { distance = 50, speed = 30 }
        };

        var sampled = ReplayPlotSampler.DownsampleByDistance(details, detail => detail.speed, 16);

        Assert.Equal(5, sampled.Count);
        Assert.True(sampled.Zip(sampled.Skip(1), (left, right) => left.Distance <= right.Distance).All(value => value));
        Assert.Equal(100, details[0].distance);
        Assert.Equal(25, details[1].distance);
    }

    [Fact]
    public void DownsampleByDistanceReturnsSingleValidPoint()
    {
        var sampled = ReplayPlotSampler.DownsampleByDistance(
            new[] { new ReplayDetailsPerTime { distance = 42, speed = 180 } },
            detail => detail.speed);

        var point = Assert.Single(sampled);
        Assert.Equal(42, point.Distance);
        Assert.Equal(180, point.Value);
    }

    [Fact]
    public void DownsampleByDistancePreservesEndpointsWhenOnlyTwoPointsAreRequested()
    {
        var details = Enumerable.Range(0, 10)
            .Select(index => new ReplayDetailsPerTime { distance = index, speed = index })
            .ToList();

        var sampled = ReplayPlotSampler.DownsampleByDistance(details, detail => detail.speed, 2);

        Assert.Equal(new[] { 0f, 9f }, sampled.Select(point => point.Distance));
    }

    [Fact]
    public void DownsampleByDistanceBoundsEveryFrameReplayData()
    {
        var details = Enumerable.Range(0, 36_000)
            .Select(index => new ReplayDetailsPerTime
            {
                distance = index * 0.25f,
                speed = index % 997 == 0 ? 400 : index % 180
            })
            .ToList();

        var sampled = ReplayPlotSampler.DownsampleByDistance(details, detail => detail.speed);

        Assert.True(sampled.Count <= ReplayPlotSampler.DefaultMaximumPoints);
        Assert.Equal(details.First().distance, sampled.First().Distance);
        Assert.Equal(details.Last().distance, sampled.Last().Distance);
        Assert.Contains(sampled, point => point.Value == 400);
    }

    [Theory]
    [InlineData(-5, 100, 0)]
    [InlineData(50, 100, 0.5)]
    [InlineData(150, 100, 1)]
    [InlineData(50, 0, 0)]
    public void NormalizeDistanceClampsToThePlotRange(float distance, float maximumDistance, double expected)
    {
        Assert.Equal(expected, ReplayPlotSampler.NormalizeDistance(distance, maximumDistance), 3);
    }
}
