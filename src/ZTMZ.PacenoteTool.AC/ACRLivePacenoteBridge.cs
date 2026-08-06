using System;
using System.Collections.Generic;
using System.IO.Pipes;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
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
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task _listenerTask;

    public ACRLivePacenoteBridge(Action<LivePacenoteEvent> publish)
    {
        _publish = publish;
        _listenerTask = Task.Run(ListenAsync);
    }

    private async Task ListenAsync()
    {
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
                Logger.Info("ACR live-pacenote bridge connected.");

                using var reader = new StreamReader(pipe, leaveOpen: true);
                while (!_cancellation.IsCancellationRequested && pipe.IsConnected)
                {
                    var line = await reader.ReadLineAsync(_cancellation.Token);
                    if (line is null)
                    {
                        break;
                    }

                    Publish(line);
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
        if (record.Pacenotes.Count == 0)
        {
            Logger.Warn("ACR bridge pacenote {0} contains no playable tokens: {1}",
                message.Id, string.Join(", ", message.Tokens));
            return;
        }

        _publish(new LivePacenoteEvent
        {
            Id = string.IsNullOrWhiteSpace(message.Id) ? Guid.NewGuid().ToString("N") : message.Id,
            StageId = message.StageId ?? string.Empty,
            Record = record
        });
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        try
        {
            _listenerTask.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException exception) when (exception.InnerExceptions.All(inner => inner is OperationCanceledException))
        {
        }
        _cancellation.Dispose();
    }

    private sealed class ACRLivePacenoteMessage
    {
        public int Version { get; init; }
        public string Id { get; init; } = string.Empty;
        public string? StageId { get; init; }
        public List<string> Tokens { get; init; } = new();
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
            ["OverBumps"] = "over_bumps", ["Dip"] = "dip", ["Opens"] = "opens", ["OpensLate"] = "opens_late", ["OpensToFlat"] = "opens_to_flat",
            ["Tightens"] = "tightens", ["TightensLate"] = "tightens_late", ["Tighten1"] = "tightens_to_1", ["Tighten2"] = "tightens_to_2", ["Tighten3"] = "tightens_to_3", ["Tighten4"] = "tightens_to_4", ["Tighten5"] = "tightens_to_5",
            ["DontCut"] = "dont_cut", ["Caution"] = "caution", ["SlowDown"] = "slow", ["Sudden"] = "sudden", ["Narrows"] = "narrows", ["KeepLeft"] = "keep_left", ["KeepIn"] = "keep_in", ["KeepOut"] = "keep_out",
            ["Finish"] = "finish", ["Inside"] = "inside", ["Late"] = "late", ["AfterEnd"] = "after", ["AfterFinish"] = "after,finish", ["AfterJunction"] = "after,junction", ["AtJunction"] = "at_junction", ["AtTheJunction"] = "at_junction",
            ["BadCamber"] = "bad_camber", ["BigCut"] = "big_cut", ["Blind"] = "unseen", ["Brake"] = "brake", ["Bumps"] = "bumps", ["Cut"] = "cut", ["DitchInside"] = "ditches_inside", ["DitchOutside"] = "ditches_outside",
            ["GoLeft"] = "go,left", ["GoRight"] = "go,right", ["GoStraight"] = "go_straight", ["HeavyBrake"] = "hard,brake", ["IntoDip"] = "into,dip", ["JumpMaybe"] = "jump_maybe", ["Junction"] = "junction",
            ["KeepMiddle"] = "keep_middle", ["KeepRight"] = "keep_in,right", ["Kinks"] = "kinks", ["KinksStartingLeft"] = "kinks,slight_left", ["KinksStartingRight"] = "kinks,slight_right", ["LeftAcuteHP"] = "hairpin_left", ["LeftFlat"] = "flat_left", ["LeftKink"] = "kinks,slight_left", ["LeftOpenHP"] = "hairpin_left,opens", ["LeftSquare"] = "square_left",
            ["LogsInside"] = "logs_inside", ["LogsOutside"] = "logs_outside", ["OnAsphalt"] = "tarmac", ["OnGravel"] = "gravel", ["Over"] = "over", ["OverBridge"] = "over_bridge", ["OverBump"] = "over_bumps", ["OverJumpSmall"] = "over_small_jump",
            ["RightAcuteHP"] = "hairpin_right", ["RightChicane"] = "chicane", ["RightChicaneEntry"] = "right_entry_chicane", ["RightFlat"] = "flat_right", ["RightKink"] = "kinks,slight_right", ["RightOpenHP"] = "hairpin_right,opens", ["RightSquare"] = "square_right",
            ["SmallCut"] = "small_cut", ["StopAtMarshals"] = "stop,marshalls", ["ThroughGate"] = "through_gate", ["ThroughTunnel"] = "through_tunnel", ["UpHill"] = "uphill", ["Widens"] = "widens"
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
