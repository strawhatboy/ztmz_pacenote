using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.RBR;

internal readonly record struct RBRGameStateDetection(
    GameState State,
    int? CountdownNumber = null);

internal sealed class RBRGameStateDetector
{
    private const float CountdownRestartTolerance = 0.5f;

    private bool _hasActiveStage;
    private bool _countdownActive;
    private float? _previousCountdown;

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
            return DetectRacing(
                rawState == RBRGameState.Replay,
                memData,
                lastGameState,
                completionRate,
                lapTime);
        }

        if (rawState == RBRGameState.Paused)
        {
            return new RBRGameStateDetection(GameState.Paused);
        }

        if (IsRaceEndState(rawState))
        {
            ClearCountdown();
            IsRacing = false;
            _hasActiveStage = false;
            return new RBRGameStateDetection(GameState.RaceEnd);
        }

        Reset();
        return new RBRGameStateDetection(GameState.Unknown);
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
