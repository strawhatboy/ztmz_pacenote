using Xunit;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.KT;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Core;
using System.Buffers.Binary;
using System.Text.Json;

public class Wrc7RetirementTests
{
    private static readonly Wrc7NativeResult Pending = new(false, false, false, false);
    private static readonly Wrc7NativeResult Failed = new(true, true, true, true);
    private static Wrc7Snapshot Sample(int ms, Wrc7NativeResult? result, string owner = "attempt") =>
        new(owner, "stage", "car", "class", ms) { NativeResult = result };

    [Fact]
    public void CapturedTwoRestartsAndTwoRetirementsOnlyAnnounceTheRetirements()
    {
        // Original read-only samples, including pauses, a long results screen,
        // and restarting after retirement with all native object addresses reused.
        var session = new Wrc7RaceSession();
        var state = GameState.Unknown;
        var endings = new List<long>();
        var starts = new List<long>();
        var owners = new HashSet<string>();
        foreach (var line in File.ReadLines(Path.Combine(AppContext.BaseDirectory,
                     "TestData", "wrc7-restart-retire-20261014.jsonl")))
        {
            using var document = JsonDocument.Parse(line);
            var row = document.RootElement;
            Assert.True(row.GetProperty("available").GetBoolean());
            var identity = string.Join(":", new[] { "profile", "vehicle", "stats" }
                .Select(key => row.GetProperty(key).GetString()));
            owners.Add(identity);
            var now = row.GetProperty("elapsedMs").GetInt64();
            var raw = row.GetProperty("rawMs").GetInt32();
            var penalty = row.GetProperty("penaltyMs").GetInt32();
            var finish = row.GetProperty("statsFlags8c8To8ca").EnumerateArray().Select(v => v.GetByte()).ToArray();
            var profile = new byte[14];
            BinaryPrimitives.WriteInt32LittleEndian(profile, row.GetProperty("profileRawMs").GetInt32());
            BinaryPrimitives.WriteInt32LittleEndian(profile.AsSpan(4), row.GetProperty("profileTotalMs").GetInt32());
            profile[12] = row.GetProperty("profileFlags160To161")[0].GetByte();
            profile[13] = row.GetProperty("profileFlags160To161")[1].GetByte();
            var sample = Sample(raw, Wrc7NativeResult.Decode(finish, profile), identity) with
            {
                ResultMilliseconds = Wrc7RaceSession.VerifiedResult(raw, penalty, finish, profile),
                PenaltyMilliseconds = penalty
            };
            var next = session.Update(sample, now);
            if (session.IsNewSession) starts.Add(now);
            if (next == GameState.RaceEnd && state != GameState.RaceEnd)
            {
                Assert.True(state is GameState.Racing or GameState.Paused or GameState.AdHocRaceBegin);
                Assert.False(session.IsNewSession);
                var evt = new GameStateChangeEvent
                {
                    LastGameState = state, NewGameState = next, Parameters = session.EndParameters(sample)
                };
                Assert.Equal(GameStateRaceEnd.Retired, session.FinishState);
                Assert.Equal(Constants.SYSTEM_RETIRED, RaceEndPlaybackPolicy.GetSoundToken(evt));
                Assert.False(RaceEndPlaybackPolicy.TryGetReplayFinishTime(evt, out _));
                endings.Add(now);
            }
            state = next;
        }
        Assert.Single(owners);
        Assert.Equal(new long[] { 231094, 293812 }, endings);
        // Initial start, two in-race restarts and a new attempt after retirement.
        Assert.Equal(new long[] { 210281, 214312, 264297, 280359 }, starts);
    }

    [Fact]
    public void RetirementWithClearedClockIsNotRestartAndHasNoFinishTime()
    {
        var session = new Wrc7RaceSession();
        session.Update(Sample(-1, Pending), 0);
        session.Update(Sample(5000, Pending), 100);
        var result = Sample(0, Failed);
        Assert.Equal(GameState.RaceEnd, session.Update(result, 200));
        Assert.False(session.IsNewSession);
        Assert.Equal(GameStateRaceEnd.Retired, session.FinishState);
        var parameters = session.EndParameters(result);
        Assert.Single(parameters);
        Assert.Equal(GameStateRaceEnd.Retired, parameters[GameStateRaceEndProperty.FINISH_STATE]);
        var evt = new GameStateChangeEvent { LastGameState = GameState.Paused, NewGameState = GameState.RaceEnd, Parameters = parameters };
        Assert.Equal(Constants.SYSTEM_RETIRED, RaceEndPlaybackPolicy.GetSoundToken(evt));
        Assert.False(RaceEndPlaybackPolicy.TryGetReplayFinishTime(evt, out _));
        Assert.Equal(GameState.RaceEnd, session.Update(result, 10000));
        Assert.False(session.IsNewSession);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void PreStartAbandonmentIsSilent(int clock)
    {
        var session = new Wrc7RaceSession();
        session.Update(Sample(clock, Pending), 0);
        Assert.Equal(GameState.Unknown, session.Update(Sample(0, Failed), 50));
        Assert.Null(session.FinishState);
    }

    [Fact]
    public void LateAttachmentAndChangedOwnersDoNotAnnounceStaleFailure()
    {
        var session = new Wrc7RaceSession();
        Assert.Equal(GameState.Unknown, session.Update(Sample(0, Failed), 0));
        session.Update(Sample(1000, Pending), 50);
        Assert.Equal(GameState.Unknown, session.Update(Sample(0, Failed, "other"), 100));
        Assert.Null(session.FinishState);
    }

    [Fact]
    public void RestartClearsObservedAttemptAndAllowsAnotherRace()
    {
        var session = new Wrc7RaceSession();
        session.Update(Sample(5000, Pending), 0);
        Assert.Equal(GameState.CountingDown, session.Update(Sample(-1, Pending), 50));
        Assert.True(session.IsNewSession);
        Assert.Null(session.FinishState);
        Assert.Equal(GameState.Unknown, session.Update(Sample(0, Failed), 100));
        session.Update(Sample(-1, Pending), 150);
        session.Update(Sample(1000, Pending), 200);
        Assert.Equal(GameState.RaceEnd, session.Update(Sample(0, Failed), 250));
        Assert.Equal(GameState.CountingDown, session.Update(Sample(-1, Pending), 300));
        Assert.True(session.IsNewSession);
        Assert.Null(session.FinishState);
    }

    [Fact]
    public void PauseMissingDataAndMissingFlagsCannotProveRetirement()
    {
        var session = new Wrc7RaceSession();
        session.Update(Sample(5000, Pending), 0);
        Assert.Equal(GameState.Paused, session.Update(Sample(5000, Pending), 60000));
        Assert.Equal(GameState.Unknown, session.Update(null, 60050));
        Assert.Equal(GameState.Unknown, session.Update(Sample(0, Failed), 60100));
        session.Update(Sample(5000, null), 60200);
        Assert.Equal(GameState.Unknown, session.Update(Sample(0, Failed), 60300));
        Assert.Null(session.FinishState);
    }

    [Theory]
    [InlineData(false, true, true, true)]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, false, true)]
    [InlineData(true, true, true, false)]
    public void PartialResultsWaitForBothFailureFlags(bool ended, bool failed, bool ready, bool profileFailed)
    {
        var session = new Wrc7RaceSession();
        session.Update(Sample(5000, Pending), 0);
        Assert.Equal(GameState.Paused, session.Update(Sample(0, new(ended, failed, ready, profileFailed)), 50));
        Assert.Null(session.FinishState);
        Assert.Equal(GameState.RaceEnd, session.Update(Sample(0, Failed), 100));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IncompleteResultExpiresWithoutAnEndingVoice(bool completedAfterTimeout)
    {
        var session = new Wrc7RaceSession();
        session.Update(Sample(5000, Pending), 0);
        var incomplete = Sample(0, new(true, true, false, false));
        Assert.Equal(GameState.Paused, session.Update(incomplete, 50));
        Assert.Equal(GameState.Unknown, session.Update(completedAfterTimeout ? Sample(0, Failed) : incomplete, 2051));
        Assert.Equal(GameState.Unknown, session.Update(Sample(0, Failed), 2100));
        Assert.Null(session.FinishState);
    }

    [Fact]
    public void NormalFinishKeepsTimeAndCannotBecomeRetirement()
    {
        var session = new Wrc7RaceSession();
        session.Update(Sample(5000, Pending), 0);
        var result = Sample(100000, new(true, false, true, false)) with
        { ResultMilliseconds = 118000, PenaltyMilliseconds = 18000 };
        Assert.Equal(GameState.RaceEnd, session.Update(result, 50));
        var parameters = session.EndParameters(result);
        Assert.Equal(GameStateRaceEnd.Normal, parameters[GameStateRaceEndProperty.FINISH_STATE]);
        Assert.Equal(118f, parameters[GameStateRaceEndProperty.FINISH_TIME]);
        Assert.Equal(18f, parameters[GameStateRaceEndProperty.FINISH_TIME_PANALTY]);
        session.Update(Sample(0, Failed), 100);
        Assert.Equal(GameStateRaceEnd.Normal, session.FinishState);
    }

    [Fact]
    public void NativeDecoderRejectsInvalidOrShortRecords()
    {
        var profile = new byte[14];
        Assert.True(Wrc7NativeResult.Decode([0, 0, 0], profile)!.Pending);
        profile[12] = profile[13] = 1;
        Assert.True(Wrc7NativeResult.Decode([1, 0, 1], profile)!.Retired);
        Assert.Null(Wrc7NativeResult.Decode([2, 0, 1], profile));
        Assert.Null(Wrc7NativeResult.Decode([1, 0], profile));
        Assert.Null(Wrc7NativeResult.Decode([1, 0, 1], profile.AsSpan(0, 13)));
        profile[13] = 2;
        Assert.Null(Wrc7NativeResult.Decode([1, 0, 1], profile));
    }
}
