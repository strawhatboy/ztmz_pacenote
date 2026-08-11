using System;
using System.Collections.Generic;
using System.IO.Pipes;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.AC;

/// <summary>
/// Receives pacenote calls from the optional in-process ACR bridge. The game never
/// connects to the network: the bridge is a local named-pipe client only.
/// </summary>
internal sealed class ACRLivePacenoteBridge : IDisposable
{
    internal const string PipeName = "ZTMZPacenote.ACR.Live.v1";
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    private static readonly JsonSerializerOptions MessageJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    private readonly Action<LivePacenoteEvent> _publish;
    private readonly Action<GameState, Dictionary<string, object>?> _publishGameState;
    private readonly Action<CarDamageEvent> _publishCarDamage;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Channel<string> _messageQueue = Channel.CreateBounded<string>(new BoundedChannelOptions(256)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true,
        SingleWriter = true
    });
    private readonly Task _listenerTask;
    private readonly Task _messageProcessorTask;
    private readonly object _countdownLock = new();
    private CancellationTokenSource? _countdownCancellation;
    private Task? _countdownTask;
    private DateTime _lastCountdownGoAtUtc = DateTime.MinValue;
    private bool _hasObservedRaceLifecycle;
    private int _messageQueueOverflowLogged;

    private static readonly TimeSpan CountdownInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan CountdownGoDuplicateWindow = TimeSpan.FromSeconds(3);

    public ACRLivePacenoteBridge(
        Action<LivePacenoteEvent> publish,
        Action<GameState, Dictionary<string, object>?> publishGameState,
        Action<CarDamageEvent> publishCarDamage)
    {
        _publish = publish;
        _publishGameState = publishGameState;
        _publishCarDamage = publishCarDamage;
        _listenerTask = Task.Run(ListenAsync);
        _messageProcessorTask = Task.Run(ProcessMessagesAsync);
    }

    private async Task ListenAsync()
    {
        Logger.Info("ACR live-pacenote bridge listener started as {0}.", GetCurrentProcessIdentity());
        while (!_cancellation.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_cancellation.Token);

                using var reader = new StreamReader(pipe, leaveOpen: true);
                while (!_cancellation.IsCancellationRequested && pipe.IsConnected)
                {
                    var line = await reader.ReadLineAsync(_cancellation.Token);
                    if (line is null)
                    {
                        break;
                    }

                    if (_messageQueue.Writer.TryWrite(line))
                    {
                        Interlocked.Exchange(ref _messageQueueOverflowLogged, 0);
                    }
                    else if (Interlocked.Exchange(ref _messageQueueOverflowLogged, 1) == 0)
                    {
                        Logger.Warn("ACR live-pacenote bridge receive queue is full; dropping incoming messages until it recovers.");
                    }
                }
            }
            catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                Logger.Warn(exception, "ACR live-pacenote bridge listener failed; retrying.");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), _cancellation.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private async Task ProcessMessagesAsync()
    {
        try
        {
            await foreach (var line in _messageQueue.Reader.ReadAllAsync(_cancellation.Token))
            {
                try
                {
                    Publish(line);
                }
                catch (Exception exception)
                {
                    Logger.Error(exception, "Failed to process an ACR live-pacenote bridge message.");
                }
            }
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
        }
    }

    private static string GetCurrentProcessIdentity()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return $"{identity.Name} (elevated={principal.IsInRole(WindowsBuiltInRole.Administrator)})";
        }
        catch
        {
            return "<unavailable>";
        }
    }

    private void Publish(string line)
    {
        ACRLivePacenoteMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<ACRLivePacenoteMessage>(line, MessageJsonOptions);
        }
        catch (JsonException exception)
        {
            Logger.Warn(exception, "Ignoring malformed ACR live-pacenote bridge message.");
            return;
        }

        if (message is null || message.Version != 1 || message.Tokens.Count == 0)
        {
            Logger.Warn("Ignoring unsupported ACR live-pacenote bridge message.");
            return;
        }

        var record = ACRPacenoteTokenTranslator.Translate(message.Tokens);
        if (record.Pacenotes.Count == 0 && !IsHandledSystemCue(message.Token ?? message.Tokens.FirstOrDefault()))
        {
            Logger.Warn(
                "ACR bridge received an unmapped PN resource: raw={0}, token={1}, placement={2}, variant={3}",
                message.RawSoundName ?? "<unknown>",
                message.Token ?? string.Join(", ", message.Tokens),
                message.Placement ?? "<unknown>",
                message.Variant ?? "<unknown>");
        }

        var eventId = string.IsNullOrWhiteSpace(message.Id) ? Guid.NewGuid().ToString("N") : message.Id;

        // If ZTMZ attaches after ACR has already started, the bridge will not
        // replay the earlier countdown resources. Attach to the active race
        // before forwarding the first mapped live pacenote so the core does
        // not discard it while still in Unknown.
        if (record.Pacenotes.Count > 0)
        {
            EnsureActiveRaceAttached();
        }

        _publish(new LivePacenoteEvent
        {
            Id = eventId,
            StageId = message.StageId ?? string.Empty,
            Record = record,
            SourceResourceName = message.RawSoundName,
            SourceToken = message.Token,
            SourcePlacement = message.Placement,
            SourceVariant = message.Variant
        });

        PublishSystemCue(message, eventId);
    }

    private void PublishSystemCue(ACRLivePacenoteMessage message, string eventId)
    {
        var token = string.IsNullOrWhiteSpace(message.Token)
            ? message.Tokens.FirstOrDefault()
            : message.Token;
        if (string.Equals(token, "EventCountdown", StringComparison.OrdinalIgnoreCase))
        {
            StartCountdown(message, eventId);
        }
        else if (string.Equals(token, "EventCountdownGo", StringComparison.OrdinalIgnoreCase))
        {
            PublishCountdownGo();
        }
        else if (string.Equals(token, "StopAtMarshals", StringComparison.OrdinalIgnoreCase))
        {
            PublishRaceEnd(null);
        }
        else if (IsDisqualifiedToken(token))
        {
            PublishRaceEnd(GameStateRaceEnd.Disqualified);
        }
        else if (string.Equals(token, "EventOutOfTrack", StringComparison.OrdinalIgnoreCase))
        {
            PublishSystemSound(message, $"{eventId}-out-of-track", "outtrack");
        }
        else if (string.Equals(token, "EventCollide", StringComparison.OrdinalIgnoreCase))
        {
            // FMOD variants select voice takes, not collision severity.
            PublishCollision();
        }
        else if (TryGetEventCountSound(token, out var systemSoundToken))
        {
            PublishSystemSound(message, $"{eventId}-count", systemSoundToken);
        }
    }

    private void StartCountdown(ACRLivePacenoteMessage source, string eventId)
    {
        CancellationTokenSource cancellation;
        lock (_countdownLock)
        {
            if (_countdownTask is { IsCompleted: false })
            {
                Logger.Debug("Ignoring duplicate ACR EventCountdown while the current countdown is active.");
                return;
            }

            _countdownCancellation?.Dispose();
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(_cancellation.Token);
            _countdownCancellation = cancellation;
            _hasObservedRaceLifecycle = true;
            _publishGameState(GameState.RaceBegin, null);
            _countdownTask = PublishCountdownAsync(source, eventId, cancellation);
        }
    }

    private void EnsureActiveRaceAttached()
    {
        if (_hasObservedRaceLifecycle)
        {
            return;
        }

        _hasObservedRaceLifecycle = true;
        Logger.Info("ACR live bridge attached after race start; emitting AdHocRaceBegin and Racing without countdown.");
        _publishGameState(GameState.AdHocRaceBegin, null);
        _publishGameState(GameState.Racing, null);
    }

    private async Task PublishCountdownAsync(ACRLivePacenoteMessage source, string eventId, CancellationTokenSource cancellation)
    {
        try
        {
            Logger.Info("ACR EventCountdown received; publishing ZTMZ countdown 5 to 1.");
            for (var number = 5; number >= 1; --number)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                _publish(CreateSystemSoundEvent(
                    source,
                    $"{eventId}-countdown-{number}",
                    Constants.SYSTEM_COUNTDOWNS[number - 1],
                    isSequential: false));

                if (number > 1)
                {
                    await Task.Delay(CountdownInterval, cancellation.Token);
                }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Failed to publish the ZTMZ countdown for ACR.");
        }
        finally
        {
            lock (_countdownLock)
            {
                if (ReferenceEquals(_countdownCancellation, cancellation))
                {
                    _countdownCancellation = null;
                    _countdownTask = null;
                }
            }

            cancellation.Dispose();
        }
    }

    private void PublishCountdownGo()
    {
        var joinedAfterCountdown = false;
        lock (_countdownLock)
        {
            var now = DateTime.UtcNow;
            if (now - _lastCountdownGoAtUtc < CountdownGoDuplicateWindow)
            {
                Logger.Debug("Ignoring duplicate ACR EventCountdownGo resource.");
                return;
            }

            _lastCountdownGoAtUtc = now;
            joinedAfterCountdown = !_hasObservedRaceLifecycle;
        }

        if (joinedAfterCountdown)
        {
            // ZTMZ can attach after EventCountdown but before EventCountdownGo.
            // Treat this as a late join so the core initializes its active-race
            // state without replaying either the countdown or Go.
            EnsureActiveRaceAttached();
            return;
        }

        _hasObservedRaceLifecycle = true;
        Logger.Info("ACR EventCountdownGo received; emitting ZTMZ Racing state.");
        _publishGameState(GameState.Racing, null);
    }

    private void PublishRaceEnd(GameStateRaceEnd? finishState)
    {
        var parameters = finishState is null
            ? null
            : new Dictionary<string, object>
            {
                [GameStateRaceEndProperty.FINISH_STATE] = finishState.Value
            };
        Logger.Info(
            "ACR ending resource received; emitting ZTMZ RaceEnd state with finish state {0}.",
            finishState?.ToString() ?? "Unknown");
        _publishGameState(GameState.RaceEnd, parameters);
    }

    private void PublishSystemSound(ACRLivePacenoteMessage source, string eventId, string systemSoundToken)
    {
        Logger.Info("ACR {0} received; playing ZTMZ sound {1}.", source.Token, systemSoundToken);
        _publish(CreateSystemSoundEvent(source, eventId, systemSoundToken, isSequential: false));
    }

    private void PublishCollision()
    {
        Logger.Info("ACR EventCollide received; publishing a medium collision event.");
        _publishCarDamage(new CarDamageEvent
        {
            DamageType = CarDamage.Collision,
            Parameters = new Dictionary<string, object>
            {
                [CarDamageConstants.SEVERITY] = (int)CollisionSeverity.Medium
            }
        });
    }

    private static bool TryGetEventCountSound(string? token, out string systemSoundToken)
    {
        systemSoundToken = string.Empty;
        if (token is null || !token.StartsWith("EventCount", StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(token["EventCount".Length..], out var count) || count is < 1 or > 5)
        {
            return false;
        }

        systemSoundToken = Constants.SYSTEM_COUNTDOWNS[count - 1];
        return true;
    }

    private static bool IsHandledSystemCue(string? token)
    {
        return string.Equals(token, "EventCountdown", StringComparison.OrdinalIgnoreCase)
            || string.Equals(token, "EventCountdownGo", StringComparison.OrdinalIgnoreCase)
            || string.Equals(token, "StopAtMarshals", StringComparison.OrdinalIgnoreCase)
            || string.Equals(token, "EventOutOfTrack", StringComparison.OrdinalIgnoreCase)
            || string.Equals(token, "EventCollide", StringComparison.OrdinalIgnoreCase)
            || TryGetEventCountSound(token, out _)
            || IsDisqualifiedToken(token);
    }

    private static bool IsDisqualifiedToken(string? token)
    {
        return string.Equals(token, "EventDisqualified", StringComparison.OrdinalIgnoreCase)
            || string.Equals(token, "EventDisqualified1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(token, "EventDisqualified2", StringComparison.OrdinalIgnoreCase);
    }

    private static LivePacenoteEvent CreateSystemSoundEvent(
        ACRLivePacenoteMessage source,
        string eventId,
        string systemSoundToken,
        bool isSequential)
    {
        return new LivePacenoteEvent
        {
            Id = eventId,
            StageId = source.StageId ?? string.Empty,
            SourceResourceName = source.RawSoundName ?? string.Empty,
            SourceToken = source.Token ?? string.Empty,
            SourcePlacement = source.Placement ?? string.Empty,
            SourceVariant = source.Variant ?? string.Empty,
            SystemSoundToken = systemSoundToken,
            SystemSoundSequential = isSequential
        };
    }

    public void Dispose()
    {
        CancellationTokenSource? countdownCancellation;
        lock (_countdownLock)
        {
            countdownCancellation = _countdownCancellation;
            _countdownCancellation = null;
            _countdownTask = null;
        }
        countdownCancellation?.Cancel();

        _cancellation.Cancel();
        _messageQueue.Writer.TryComplete();
        try
        {
            Task.WaitAll(new[] { _listenerTask, _messageProcessorTask }, TimeSpan.FromSeconds(1));
        }
        catch (AggregateException exception) when (exception.InnerExceptions.All(inner => inner is OperationCanceledException))
        {
        }
        countdownCancellation?.Dispose();
        _cancellation.Dispose();
    }

    private sealed class ACRLivePacenoteMessage
    {
        public int Version { get; init; }
        public string Id { get; init; } = string.Empty;
        public string? StageId { get; init; }
        public List<string> Tokens { get; init; } = new();
        public string? RawSoundName { get; init; }
        public string? Token { get; init; }
        public string? Placement { get; init; }
        public string? Variant { get; init; }
    }
}

internal static class ACRPacenoteTokenTranslator
{
    private static readonly IReadOnlyDictionary<string, string> TokenMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Left1"] = "1_left", ["Left2"] = "2_left", ["Left3"] = "3_left", ["Left4"] = "4_left", ["Left5"] = "5_left", ["Left6"] = "6_left",
            ["Right1"] = "1_right", ["Right2"] = "2_right", ["Right3"] = "3_right", ["Right4"] = "4_right", ["Right5"] = "5_right", ["Right6"] = "6_right",
            ["LeftHP"] = "hairpin_left", ["RightHP"] = "hairpin_right", ["Flat"] = "flat", ["FlatLeft"] = "flat_left", ["FlatRight"] = "flat_right",
            ["Into"] = "into", ["And"] = "and", ["Long"] = "long", ["VeryLong"] = "very_long", ["Short"] = "short", ["Crest"] = "crest", ["OverCrest"] = "over_crest",
            ["OverBumps"] = "over_bumps", ["Dip"] = "dip", ["Opens"] = "opens", ["OpensLate"] = "opens,late", ["OpensToFlat"] = "opens_to_flat",
            ["Tightens"] = "tightens", ["TightensLate"] = "tightens,late", ["Tighten1"] = "tightens_to_1", ["Tighten2"] = "tightens_to_2", ["Tighten3"] = "tightens_to_3", ["Tighten4"] = "tightens_to_4", ["Tighten5"] = "tightens_to_5",
            ["DontCut"] = "dont_cut", ["Caution"] = "caution", ["SlowDown"] = "slow", ["Sudden"] = "sudden", ["Narrows"] = "narrows", ["KeepLeft"] = "keep_left", ["KeepIn"] = "keep_in", ["KeepOut"] = "keep_out",
            ["Finish"] = "finish", ["Inside"] = "inside", ["Late"] = "late", ["AfterEnd"] = "after", ["AfterFinish"] = "after,finish", ["AfterJunction"] = "after,junction", ["AtJunction"] = "at_junction", ["AtTheJunction"] = "at_junction",
            ["BadCamber"] = "bad_camber", ["BigCut"] = "big_cut", ["Blind"] = "unseen", ["Brake"] = "brake", ["Bumps"] = "bumps", ["Cut"] = "cut", ["DitchInside"] = "ditches_inside", ["DitchOutside"] = "ditches_outside",
            ["GoLeft"] = "go,left", ["GoRight"] = "go,right", ["GoStraight"] = "go_straight", ["HeavyBrake"] = "hard,brake", ["IntoDip"] = "into,dip", ["JumpMaybe"] = "jump_maybe", ["Junction"] = "junction",
            ["KeepMiddle"] = "keep_middle", ["KeepRight"] = "keep_in,right", ["Kinks"] = "kinks", ["KinksStartingLeft"] = "kinks,slight_left", ["KinksStartingRight"] = "kinks,slight_right", ["LeftAcuteHP"] = "hairpin_left", ["LeftFlat"] = "flat_left", ["LeftKink"] = "kinks,slight_left", ["LeftOpenHP"] = "hairpin_left,opens", ["LeftSquare"] = "square_left",
            ["LogsInside"] = "logs_inside", ["LogsOutside"] = "logs_outside", ["OnAsphalt"] = "tarmac", ["OnGravel"] = "gravel", ["Over"] = "over", ["OverBridge"] = "over_bridge", ["OverBump"] = "over_bumps", ["OverJumpSmall"] = "over_small_jump",
            ["RightAcuteHP"] = "hairpin_right", ["RightChicane"] = "chicane", ["RightChicaneEntry"] = "right_entry_chicane", ["RightFlat"] = "flat_right", ["RightKink"] = "kinks,slight_right", ["RightOpenHP"] = "hairpin_right,opens", ["RightSquare"] = "square_right",
            ["SmallCut"] = "small_cut", ["StopAtMarshals"] = "stop,marshalls", ["ThroughGate"] = "through_gate", ["ThroughTunnel"] = "through_tunnel", ["UpHill"] = "uphill", ["Widens"] = "widens",

            // Synonyms and detail calls verified against the active ZTMZ data.zdb vocabulary.
            ["After"] = "after", ["AfterCrossroad"] = "after,junction", ["AfterTheSign"] = "after,sign", ["AfterTheSigns"] = "after,sign",
            ["AroundBale"] = "around_bales", ["AroundBarrel"] = "around_bales", ["AroundPole"] = "around,post", ["AroundPoles"] = "around,post", ["AroundSign"] = "around_sign", ["AroundSigns"] = "around_sign", ["AroundTyres"] = "around,tyres",
            ["Asphalt"] = "tarmac",
            ["AtTheBale"] = "at,bales", ["AtTheBarrel"] = "at,bales", ["AtTheCrossroad"] = "at,junction", ["AtTheGate"] = "at,gate", ["AtTheHouse"] = "at,house", ["AtThePoles"] = "at,post", ["AtTheRail"] = "at,rails", ["AtTheRoundabout"] = "at,roundabout", ["AtTheSign"] = "at,sign", ["AtTheSigns"] = "at,sign", ["AtTheTyres"] = "at,tyres",
            ["Bale"] = "bales", ["Barrel"] = "bales", ["BigCrest"] = "big_crest", ["BigDip"] = "big,dip", ["Bridge"] = "bridge", ["Bump"] = "bumps",
            ["CautionInside"] = "caution,inside", ["CautionOutside"] = "caution,outside", ["Chicane"] = "chicane", ["Cobbles"] = "cobble", ["Concrete"] = "concrete", ["CrestBig"] = "big_crest", ["CrestOver"] = "over_crest", ["CrestSmall"] = "small_crest", ["Crossroad"] = "junction", ["CutIn"] = "cut,inside",
            ["Ditch"] = "ditches", ["Down"] = "downhill", ["Downhill"] = "downhill", ["Early"] = "early",
            ["FlatCrest"] = "flat,crest", ["FlatOut"] = "flat", ["Ford"] = "ford", ["Gate"] = "gate", ["Gravel"] = "gravel", ["Handbrake"] = "handbrake", ["Hidden"] = "unseen", ["Hole"] = "hole", ["HoleInside"] = "hole,inside", ["HoleOutside"] = "hole,outside", ["Holes"] = "hole", ["Ice"] = "ice", ["Immediate"] = "directly",
            ["Jump"] = "jump", ["JumpBig"] = "big_jump", ["JumpOver"] = "over_jump", ["JumpSmall"] = "small_jump",
            ["Left"] = "left", ["LeftChicane"] = "left_entry_chicane", ["LeftChicaneEntry"] = "left_entry_chicane", ["LeftEasy"] = "left,easy", ["LeftFast"] = "left,fast", ["LeftHard"] = "left,hard", ["LeftRight"] = "left,right", ["LeftSudden"] = "sudden,left", ["LeftVeryHard"] = "left,very,hard",
            ["Logs"] = "logs", ["LongMale"] = "long", ["LongStraight"] = "long,straight", ["Loose"] = "loose_gravel", ["LoseGravel"] = "loose_gravel",
            ["Minus"] = "minus", ["Mud"] = "slippery", ["NarrowsInside"] = "narrows,inside", ["NarrowsLeft"] = "narrows,left", ["NarrowsOutside"] = "narrows,outside", ["NarrowsRight"] = "narrows,right",
            ["Numb01"] = "1", ["Numb02"] = "2", ["Numb03"] = "3", ["Numb04"] = "4", ["Numb05"] = "5", ["Numb06"] = "6", ["Numb07"] = "7",
            ["OnCobbles"] = "cobble", ["OnConcrete"] = "concrete", ["OnIce"] = "ice", ["OnLooseGravel"] = "loose_gravel", ["OnPavement"] = "tarmac", ["OnSnow"] = "snow", ["OnSnowAndIce"] = "snow,ice",
            ["Outside"] = "outside", ["OverBigJump"] = "over_big_jump", ["OverJump"] = "over_jump", ["Pavement"] = "tarmac", ["Plus"] = "plus", ["Pole"] = "post", ["Poles"] = "post", ["Pothole"] = "hole", ["Pothole1"] = "hole", ["Pothole2"] = "hole", ["Potholes"] = "hole", ["Potholes1"] = "hole", ["Potholes2"] = "hole",
            ["Right"] = "right", ["RightEasy"] = "right,easy", ["RightFast"] = "right,fast", ["RightHard"] = "right,hard", ["RightLeft"] = "right,left", ["RightSudden"] = "sudden,right", ["RightVeryHard"] = "right,very,hard",
            ["Rock"] = "rocks", ["Rocks"] = "rocks", ["RocksInside"] = "rocks,inside", ["RocksOutside"] = "rocks,outside", ["Rough"] = "rough", ["Roundabout"] = "roundabout", ["Ruts"] = "ruts", ["ShortMale"] = "short", ["Sign"] = "sign", ["Signs"] = "sign", ["Slippery"] = "slippery", ["SmallCrest"] = "small_crest", ["Snow"] = "snow", ["SnowAndIce"] = "snow,ice",
            ["SuddenLeft"] = "sudden,left", ["SuddenRight"] = "sudden,right", ["Threes"] = "3", ["ThreesInside"] = "3,inside", ["ThreesOutside"] = "3,outside", ["ThroughWater"] = "through,water_splash",
            ["Tree"] = "tree", ["TreeInside"] = "tree,inside", ["TreeOutside"] = "tree,outside", ["Trees"] = "tree", ["TreesInside"] = "tree,inside", ["TreesOutside"] = "tree,outside", ["Tunnel"] = "tunnel", ["Tyres"] = "tyres", ["UnderBridge"] = "tunnel", ["Up"] = "uphill", ["VeryLongStraight"] = "very_long,straight", ["Water"] = "water_splash", ["WaterSplash"] = "water_splash", ["Wet"] = "slippery", ["WidensInside"] = "widens,inside", ["WidensLeft"] = "widens,left", ["WidensOutside"] = "widens,outside", ["WidensRight"] = "widens,right"
        };

    public static PacenoteRecord Translate(IEnumerable<string> sourceTokens)
    {
        var translated = new List<string>();
        foreach (var token in sourceTokens)
        {
            if (string.IsNullOrWhiteSpace(token) || token.StartsWith("Pause", StringComparison.OrdinalIgnoreCase) || token.StartsWith("sPause", StringComparison.OrdinalIgnoreCase) || token.StartsWith("s_ResetPause", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (TokenMap.TryGetValue(token, out var mapped))
            {
                translated.AddRange(mapped.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            }
            else if (TryTranslateCompoundToken(token, translated))
            {
            }
            else if (token.StartsWith("Dist", StringComparison.OrdinalIgnoreCase) && int.TryParse(token[4..], out var distance))
            {
                translated.Add(distance.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        return new PacenoteRecord
        {
            RawText = string.Join(" ", sourceTokens),
            Pacenotes = translated.Select(token => new Pacenote { Note = token }).ToList()
        };
    }

    private static bool TryTranslateCompoundToken(string token, List<string> translated)
    {
        var parts = token.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            return false;
        }

        var mapped = new List<string>();
        foreach (var part in parts)
        {
            if (!TokenMap.TryGetValue(part, out var partMapping))
            {
                return false;
            }
            mapped.AddRange(partMapping.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        translated.AddRange(mapped);
        return true;
    }
}
