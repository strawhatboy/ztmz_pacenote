using System.Reflection;
using System.Runtime.CompilerServices;
using ZTMZ.PacenoteTool.AC;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.Core;
using Xunit;

namespace ZTMZ.PacenoteTool.Portable.Tests;

public sealed class AcrLiveOnlyPlaybackTest
{
    [Fact]
    public void StartingAcrClearsOldDistanceQueueWithoutReadingProfiles()
    {
        // Avoid devices and user's configuration. Uninitialized ACR has no reader:
        // any attempt to load its distance script would fail this test.
        var game = (ACRally)RuntimeHelpers.GetUninitializedObject(typeof(ACRally));
        var manager = (ProfileManager)RuntimeHelpers.GetUninitializedObject(typeof(ProfileManager));
        typeof(ProfileManager).GetField("_logger", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(manager, NLog.LogManager.GetLogger("AcrLiveOnlyPlaybackTest"));
        manager.AudioFiles = [new AudioFile()];
        manager.CurrentPlayIndex = 0;
        manager.CurrentScriptPath = "previous-stage.pacenote";

        manager.StartReplaying(game, "Afon Bidno");
        manager.StartReplaying(game, "Afon Bidno");

        Assert.Empty(manager.AudioFiles);
        Assert.Null(manager.CurrentAudioFile);
        Assert.Null(manager.CurrentScriptReader);
        Assert.Equal(string.Empty, manager.CurrentScriptPath);
        Assert.False(GamePacenoteSourcePolicy.AllowsDistancePlayback(game));
    }
}
