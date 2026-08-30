using System;
using System.IO;
using System.Linq;
using Xunit;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.RBR;

namespace ZTMZ.PacenoteTool.Tests.RBR;

public sealed class RBRInstalledCodriverAudioProviderTest
{
    [Fact]
    public void LoadSnapshotGroupsNumberedAlternativesAndModifierDefinitions()
    {
        using var installation = new TemporaryRbrAudioInstallation();

        var snapshot = RBRInstalledCodriverAudioProvider.LoadSnapshot(installation.RootDirectory);

        Assert.NotNull(snapshot);
        Assert.Equal("Test Voice", snapshot.PackageName);
        Assert.Equal("english", snapshot.Language);
        Assert.Equal(
            ["left_six1.ogg", "left_six2.ogg"],
            snapshot.PacenotePaths[5].Select(Path.GetFileName));
        Assert.Equal(
            ["tightens1.ogg", "tightens2.ogg"],
            snapshot.ModifierPaths["TIGHTENS"].Select(Path.GetFileName));
        Assert.Equal(
            ["go1.ogg", "go2.ogg"],
            snapshot.SystemSoundPaths[Constants.SYSTEM_GO].Select(Path.GetFileName));
        Assert.Equal(
            ["problem_engine.ogg"],
            snapshot.SystemSoundPaths[Constants.SYSTEM_ENGINE_DAMAGE].Select(Path.GetFileName));
        Assert.Equal(
            ["water_temp.ogg"],
            snapshot.SystemSoundPaths[Constants.SYSTEM_RADIATOR_DAMAGE].Select(Path.GetFileName));
        Assert.False(snapshot.PacenotePaths.ContainsKey(99));
    }

    [Fact]
    public void EnabledProviderResolvesTheSelectedPackageWithoutCopyingIt()
    {
        using var installation = new TemporaryRbrAudioInstallation();
        var provider = new RBRInstalledCodriverAudioProvider();
        provider.Configure(
            new RBRGameInstallationConfig { ExecutablePath = installation.ExecutablePath },
            enabled: true);

        provider.PreparePacenoteAudio();
        var resolved = provider.TryResolvePacenoteAudio(
            new GamePacenoteAudioCue
            {
                ProviderId = RBRInstalledCodriverAudioProvider.ProviderId,
                PacenoteType = 5
            },
            out var resolution);

        Assert.True(resolved);
        var segment = Assert.Single(resolution.Segments);
        Assert.All(segment.AlternativePaths, path =>
            Assert.StartsWith(installation.PackageDirectory, path, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, segment.AlternativePaths.Count);
    }

    [Fact]
    public void EnabledProviderResolvesPackageAndOriginalRbrSystemAudio()
    {
        using var installation = new TemporaryRbrAudioInstallation();
        var provider = new RBRInstalledCodriverAudioProvider();
        provider.Configure(
            new RBRGameInstallationConfig { ExecutablePath = installation.ExecutablePath },
            enabled: true);
        provider.PreparePacenoteAudio();

        Assert.True(provider.TryResolveSystemAudio(Constants.SYSTEM_COUNTDOWN_5, out var countdown));
        Assert.All(countdown.AlternativePaths, path =>
            Assert.StartsWith(installation.PackageDirectory, path, StringComparison.OrdinalIgnoreCase));

        Assert.True(provider.TryResolveSystemAudio(Constants.SYSTEM_ENGINE_DAMAGE, out var engine));
        Assert.EndsWith("problem_engine.ogg", Assert.Single(engine.AlternativePaths));

        Assert.False(provider.TryResolveSystemAudio(Constants.SYSTEM_COLLISION_SEVERE, out _));
    }

    [Fact]
    public void DisabledProviderDoesNotResolveInstalledAudio()
    {
        using var installation = new TemporaryRbrAudioInstallation();
        var provider = new RBRInstalledCodriverAudioProvider();
        provider.Configure(
            new RBRGameInstallationConfig { ExecutablePath = installation.ExecutablePath },
            enabled: false);

        provider.PreparePacenoteAudio();

        Assert.False(provider.TryResolvePacenoteAudio(
            new GamePacenoteAudioCue
            {
                ProviderId = RBRInstalledCodriverAudioProvider.ProviderId,
                PacenoteType = 5
            },
            out _));
        Assert.False(provider.TryResolveSystemAudio(Constants.SYSTEM_GO, out _));
    }

    [Fact]
    public void LoadSnapshotRejectsPackagePathsOutsideTheSoundRoot()
    {
        using var installation = new TemporaryRbrAudioInstallation();
        Directory.CreateDirectory(Path.Combine(
            installation.RootDirectory,
            "Plugins",
            "Pacenote",
            "Outside"));
        File.WriteAllText(
            installation.SettingsPath,
            "[SETTINGS]\r\nsounds=..\\Outside\r\nlanguage=english\r\n");

        var snapshot = RBRInstalledCodriverAudioProvider.LoadSnapshot(installation.RootDirectory);

        Assert.Null(snapshot);
    }

    private sealed class TemporaryRbrAudioInstallation : IDisposable
    {
        public TemporaryRbrAudioInstallation()
        {
            RootDirectory = Path.Combine(Path.GetTempPath(), $"ztmz-rbr-audio-{Guid.NewGuid():N}");
            ExecutablePath = Path.Combine(RootDirectory, "RichardBurnsRally_SSE.exe");
            PackageDirectory = Path.Combine(
                RootDirectory,
                "Plugins",
                "Pacenote",
                "sounds",
                "Test Voice");
            SettingsPath = Path.Combine(
                RootDirectory,
                "Plugins",
                "Pacenote",
                "PaceNote.ini");

            Directory.CreateDirectory(PackageDirectory);
            File.WriteAllText(ExecutablePath, string.Empty);
            File.WriteAllText(
                SettingsPath,
                "[SETTINGS]\r\nsounds=Test Voice\r\nlanguage=english\r\n");
            CreateAudioFile("left_six1.ogg");
            CreateAudioFile("left_six2.ogg");
            CreateAudioFile("tightens1.ogg");
            CreateAudioFile("tightens2.ogg");
            CreateAudioFile("_one1.ogg");
            CreateAudioFile("_two1.ogg");
            CreateAudioFile("_three1.ogg");
            CreateAudioFile("_four1.ogg");
            CreateAudioFile("_five1.ogg");
            CreateAudioFile("go1.ogg");
            CreateAudioFile("go2.ogg");
            CreateAudioFile("finish1.ogg");

            var brokenSpeechDirectory = Path.Combine(
                RootDirectory,
                "Audio",
                "Speech",
                "Broken",
                "Eng");
            Directory.CreateDirectory(brokenSpeechDirectory);
            File.WriteAllBytes(Path.Combine(brokenSpeechDirectory, "problem_engine.ogg"), []);
            File.WriteAllBytes(Path.Combine(brokenSpeechDirectory, "water_temp.ogg"), []);

            var packagesDirectory = Path.Combine(
                RootDirectory,
                "Plugins",
                "Pacenote",
                "config",
                "pacenotes",
                "packages");
            var cornersDirectory = Path.Combine(packagesDirectory, "corners");
            var modifiersDirectory = Path.Combine(packagesDirectory, "modifier");
            Directory.CreateDirectory(cornersDirectory);
            Directory.CreateDirectory(modifiersDirectory);
            File.WriteAllText(
                Path.Combine(cornersDirectory, "Numeric-Swapped.ini"),
                "[PACENOTE::SIX_LEFT]\r\nid=5\r\nSounds=1\r\nSnd0=left_six.ogg\r\n" +
                "[PACENOTE::UNSAFE]\r\nid=99\r\nSounds=1\r\nSnd0=..\\outside.ogg\r\n");
            File.WriteAllText(
                Path.Combine(modifiersDirectory, "Extended.ini"),
                "[PACENOTE::TIGHTENS] ; legacy sound and modifier\r\n" +
                "Sounds=1\r\nSnd0=tightens.ogg\r\n");
        }

        public string RootDirectory { get; }

        public string ExecutablePath { get; }

        public string PackageDirectory { get; }

        public string SettingsPath { get; }

        public void Dispose()
        {
            if (Directory.Exists(RootDirectory))
            {
                Directory.Delete(RootDirectory, recursive: true);
            }
        }

        private void CreateAudioFile(string filename)
        {
            File.WriteAllBytes(Path.Combine(PackageDirectory, filename), []);
        }
    }
}
