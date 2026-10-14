using System;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.RBR;

internal readonly record struct RBRGameStateDetection(
    GameState State,
    int? CountdownNumber = null,
    GameStateRaceEnd? FinishState = null);

internal sealed class RBRGameStateDetector
{
    private const float CountdownRestartTolerance = 0.5f;

    private bool _hasActiveStage;
    private bool _countdownActive;
    private float? _previousCountdown;
    private readonly Func<long> _clock;
    private RBRNativeResultSnapshot? _startedNativeStage;
    private long? _pendingEndSince;
    private bool _endResolved;
    // A responsiveness bound, not an assumption that the game always resolves within this time.
    internal const int ResultWaitMilliseconds = 2000;

    public RBRGameStateDetector(Func<long>? clock = null) => _clock = clock ?? (() => Environment.TickCount64);

    public bool IsReplaySession { get; private set; }
    public bool IsRacing { get; private set; }
    public bool IsPostGame { get; private set; }
    public float FinishTime { get; private set; }
    public bool SawRaceEnded { get; private set; }
    public bool SawFalseStart { get; private set; }

    public RBRGameStateDetection Detect(
        RBRMemData memData,
        bool playWhenReplay,
        GameState lastGameState,
        float completionRate,
        float lapTime)
    {
        var rawState = (RBRGameState)memData.GameStateId;
        var native = memData.NativeResult is { } sample && sample.RawState == memData.GameStateId
            ? sample : (RBRNativeResultSnapshot?)null;
        if (native is { Transition: 3 })
        {
            _startedNativeStage = null;
            _pendingEndSince = null;
        }

        if (IsCountdownState(rawState, memData.StageStartCountdown, playWhenReplay))
        {
            return DetectCountdown(rawState == RBRGameState.Replay, memData.StageStartCountdown, lastGameState);
        }

        if (rawState == RBRGameState.RaceBegin)
        {
            BeginStageSession(isReplay: false);
            ClearCountdown();
            return new RBRGameStateDetection(GameState.RaceBegin);
        }

        if (rawState == RBRGameState.Racing ||
            (rawState == RBRGameState.Replay && playWhenReplay))
        {
            var detection = DetectRacing(
                rawState == RBRGameState.Replay,
                memData,
                lastGameState,
                completionRate,
                lapTime);
            if (!IsReplaySession && native is { Started: true } started)
                _startedNativeStage = started;
            return detection;
        }

        if (rawState == RBRGameState.Paused)
        {
            return new RBRGameStateDetection(GameState.Paused);
        }

        if (IsRaceEndState(rawState))
        {
            ClearCountdown();
            IsRacing = false;
            if (!_endResolved && !IsPostGame && !IsReplaySession && _startedNativeStage is { } started)
            {
                // A different stage identity invalidates this pending result, never re-arms it.
                if (native is { } current && (current.MapId != started.MapId || current.RaceData != started.RaceData))
                    return ResolveEnd();
                _pendingEndSince ??= _clock();
                if (_clock() - _pendingEndSince.Value < ResultWaitMilliseconds)
                {
                    if (native is { Failed: true }) return ResolveEnd(GameStateRaceEnd.Retired);
                    // Stop racing/pacenote updates while waiting; publish only one terminal event.
                    return new RBRGameStateDetection(GameState.Paused);
                }
            }
            return ResolveEnd();
        }

        var pendingEnd = _pendingEndSince.HasValue;
        Reset();
        return new RBRGameStateDetection(pendingEnd ? GameState.RaceEnd : GameState.Unknown);
    }

    private RBRGameStateDetection ResolveEnd(GameStateRaceEnd? result = null)
    {
        _hasActiveStage = false;
        _startedNativeStage = null;
        _pendingEndSince = null;
        _endResolved = true;
        return new RBRGameStateDetection(GameState.RaceEnd, FinishState: result);
    }

    public void Reset()
    {
        _hasActiveStage = false;
        ClearCountdown();
        IsReplaySession = false;
        IsRacing = false;
        IsPostGame = false;
        FinishTime = 0;
        SawRaceEnded = false;
        SawFalseStart = false;
        _startedNativeStage = null;
        _pendingEndSince = null;
        _endResolved = false;
    }

    private RBRGameStateDetection DetectCountdown(bool isReplay, float countdown, GameState lastGameState)
    {
        if (!_countdownActive ||
            !_hasActiveStage ||
            IsReplaySession != isReplay ||
            (_previousCountdown.HasValue && countdown > _previousCountdown.Value + CountdownRestartTolerance))
        {
            BeginStageSession(isReplay);
            _countdownActive = true;
            _previousCountdown = countdown;
            return new RBRGameStateDetection(GameState.RaceBegin);
        }

        var countdownNumber = GetCrossedCountdownNumber(_previousCountdown, countdown);
        _previousCountdown = countdown;

        if (countdownNumber.HasValue)
        {
            return new RBRGameStateDetection(GameState.CountingDown, countdownNumber.Value);
        }

        return new RBRGameStateDetection(
            lastGameState == GameState.CountingDown ? GameState.CountingDown : GameState.RaceBegin);
    }

    private RBRGameStateDetection DetectRacing(
        bool isReplay,
        RBRMemData memData,
        GameState lastGameState,
        float completionRate,
        float lapTime)
    {
        var beginsNewSession = !_hasActiveStage || IsReplaySession != isReplay;
        if (beginsNewSession)
        {
            BeginStageSession(isReplay);
        }

        ClearCountdown();
        IsRacing = true;
        _pendingEndSince = null;
        SawRaceEnded |= memData.RaceEnded;
        SawFalseStart |= memData.FalseStart;

        if (!isReplay &&
            !SawFalseStart &&
            !IsPostGame &&
            float.IsFinite(completionRate) &&
            completionRate >= 1)
        {
            IsPostGame = true;
            FinishTime = lapTime;
        }

        if (beginsNewSession &&
            lastGameState != GameState.RaceBegin &&
            lastGameState != GameState.CountingDown)
        {
            return new RBRGameStateDetection(GameState.AdHocRaceBegin);
        }

        return new RBRGameStateDetection(GameState.Racing);
    }

    private void BeginStageSession(bool isReplay)
    {
        _startedNativeStage = null;
        _pendingEndSince = null;
        _endResolved = false;
        _hasActiveStage = true;
        IsReplaySession = isReplay;
        IsRacing = false;
        IsPostGame = false;
        FinishTime = 0;
        SawRaceEnded = false;
        SawFalseStart = false;
    }

    private void ClearCountdown()
    {
        _countdownActive = false;
        _previousCountdown = null;
    }

    private static bool IsCountdownState(RBRGameState state, float countdown, bool playWhenReplay)
    {
        if (!float.IsFinite(countdown) || countdown <= 0)
        {
            return false;
        }

        return state == RBRGameState.RaceBegin ||
               state == RBRGameState.Racing ||
               ((state == RBRGameState.Replay || state == RBRGameState.ReplayBegin) && playWhenReplay);
    }

    private static bool IsRaceEndState(RBRGameState state)
    {
        return state == RBRGameState.RaceEndOrReplay0 ||
               state == RBRGameState.RaceEnd ||
               state == RBRGameState.RaceEndOrReplay1;
    }

    private static int? GetCrossedCountdownNumber(float? previousCountdown, float countdown)
    {
        if (!previousCountdown.HasValue)
        {
            return null;
        }

        for (var number = 5; number >= 1; number--)
        {
            if (previousCountdown.Value >= number && countdown < number)
            {
                return number;
            }
        }

        return null;
    }
}
