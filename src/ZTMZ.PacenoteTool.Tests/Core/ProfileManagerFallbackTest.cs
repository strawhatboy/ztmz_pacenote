using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Core;

namespace ZTMZ.PacenoteTool.Tests.Core;

public class ProfileManagerFallbackTest
{
    [Fact]
    public void EmptyFallbackResultsAreNotCountedAsResolved()
    {
        var sounds = ProfileManager.ResolveFallbackSounds(
            new[] { 1, 2 },
            new HashSet<int> { 100 },
            (_, _) => new AutoResampledCachedSound());

        Assert.Empty(sounds);
    }

    [Fact]
    public void RepeatedFallbacksUseIndependentVisitedSets()
    {
        var visitedSets = new List<HashSet<int>>();
        var sounds = ProfileManager.ResolveFallbackSounds(
            new[] { 296, 296, 133 },
            new HashSet<int> { 88 },
            (fallback, visitedIds) =>
            {
                visitedSets.Add(new HashSet<int>(visitedIds));
                visitedIds.Add(fallback);
                return new AutoResampledCachedSound(new[] { (float)fallback });
            });

        Assert.Equal(3, sounds.Count);
        Assert.Equal(new[] { 296f, 296f, 133f }, sounds.Select(sound => sound.AudioData.Single()));
        Assert.DoesNotContain(296, visitedSets[1]);
        Assert.NotSame(visitedSets[0], visitedSets[1]);
    }

    [Fact]
    public void VisitedFallbackIsSkippedToBreakCycles()
    {
        var resolveCalls = 0;
        var sounds = ProfileManager.ResolveFallbackSounds(
            new[] { 17 },
            new HashSet<int> { 17, 297 },
            (_, _) =>
            {
                resolveCalls++;
                return new AutoResampledCachedSound(new[] { 1f });
            });

        Assert.Empty(sounds);
        Assert.Equal(0, resolveCalls);
    }
}
