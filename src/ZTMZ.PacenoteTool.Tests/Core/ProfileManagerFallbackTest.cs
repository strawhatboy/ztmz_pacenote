using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Core;

namespace ZTMZ.PacenoteTool.Tests.Core;

public class ProfileManagerFallbackTest
{
    [Fact]
    public void DefaultVoicePrefersSerenaWhenXiaoyiIsAlsoInstalled()
    {
        var xiaoyi = new CoDriverPackage { Info = new() { id = ProfileManager.LEGACY_CODRIVER_PACKAGE_ID } };
        var serena = new CoDriverPackage { Info = new() { id = ProfileManager.DEFAULT_CODRIVER_PACKAGE_ID } };
        Assert.Same(serena, ProfileManager.ResolveDefaultCoDriver(new[] { xiaoyi, serena }));
        Assert.Same(serena, ProfileManager.ResolveDefaultCoDriver(new[] { serena }));
    }

    [Fact]
    public void DefaultVoiceSupportsOldDataAndPicksUpNewPackageAfterRefresh()
    {
        var xiaoyi = new CoDriverPackage { Info = new() { id = ProfileManager.LEGACY_CODRIVER_PACKAGE_ID } };
        var packages = new List<CoDriverPackage> { xiaoyi };
        Assert.Same(xiaoyi, ProfileManager.ResolveDefaultCoDriver(packages));
        var serena = new CoDriverPackage { Info = new() { id = ProfileManager.DEFAULT_CODRIVER_PACKAGE_ID } };
        packages.Add(serena);
        Assert.Same(serena, ProfileManager.ResolveDefaultCoDriver(packages));
        // The default change must not override a saved Xiaoyi selection.
        Assert.Equal(0, CoDriverSelectionIdentity.ResolveIndex(
            packages.Select(p => p.Info).ToList(), CoDriverSelectionIdentity.Create(xiaoyi.Info), 1));
    }

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
