using System.IO.Pipes;
using System.Text;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.KT;

internal sealed class Wrc7LiveSession : IDisposable
{
    private static readonly NLog.Logger Log = NLog.LogManager.GetCurrentClassLogger();
    private readonly CancellationTokenSource cancellation = new();
    private readonly Task worker;
    private readonly HashSet<string> reported = new(StringComparer.OrdinalIgnoreCase);
    public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool Connected { get; private set; }
    public string? Error { get; private set; }

    public Wrc7LiveSession(int pid, Wrc7PacenoteCatalog catalog, IReadOnlySet<string> vocabulary,
        Func<Wrc7PacenoteModes> readModes, Action<LivePacenoteEvent> publish)
    {
        var decoder = new Wrc7LivePacenoteDecoder(catalog, vocabulary);
        Wrc7PacenoteModes? previousModes = null;
        worker = Task.Run(async () =>
        {
            try
            {
                while (!cancellation.IsCancellationRequested)
                {
                    await using var pipe = new NamedPipeServerStream($"ZTMZ.WRC7.Live.v1.{pid}", PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    try
                    {
                        await pipe.WaitForConnectionAsync(cancellation.Token);
                        using var reader = new StreamReader(pipe, Encoding.UTF8);
                        if (await ReadLineAsync(reader, cancellation.Token) != "READY")
                            throw new InvalidDataException("Missing WRC7 READY handshake");
                        Connected = true;
                        Ready.TrySetResult();
                        Log.Info("WRC7 bridge ready for process {0}", pid);
                        while (await ReadLineAsync(reader, cancellation.Token) is { } line)
                        {
                            if (line.StartsWith("STATS ", StringComparison.Ordinal))
                            {
                                if (line != "STATS dropped=0" && line != "STATS dropped=0 countdown=1") Log.Warn("WRC7 bridge: {0}", line);
                                continue;
                            }
                            var fields = line.Split('\t', 2);
                            if (fields.Length != 2 || !long.TryParse(fields[0], out var tick))
                                throw new InvalidDataException("Invalid WRC7 sound record");
                            var age = Environment.TickCount64 - tick;
                            if (age is < 0 or > 500) { Log.Debug("Discarding stale WRC7 voice: {0} ms", age); continue; }
                            if (WrcGenerationsLiveSession.TryGetCountdownSound(fields[1], out var countdownSound))
                            {
                                Log.Info("WRC7 countdown cue: {0}, ageMs={1}", fields[1], age);
                                publish(new LivePacenoteEvent
                                {
                                    Id = $"{pid}:{tick}:countdown", SourceResourceName = fields[1],
                                    SystemSoundToken = countdownSound, SystemSoundSequential = false
                                });
                                continue;
                            }
                            var modes = readModes();
                            if (modes != previousModes)
                            {
                                Log.Info("WRC7 pacenote modes (accessible): direction={0}, hairpin={1}, hazard={2}", modes.Direction, modes.Hairpin, modes.Hazard);
                                previousModes = modes;
                                reported.Clear();
                            }
                            if (!decoder.TryDecodeWithModes(pid, tick, fields[1], modes, out var pacenote))
                            {
                                if (reported.Add(fields[1])) Log.Warn("Unresolved WRC7 voice or ambiguous mode: {0}; preserving game audio", fields[1]);
                                continue;
                            }
                            Log.Debug("WRC7 voice: ageMs={0}, {1} -> {2}", age, fields[1], pacenote!.Record.RawText);
                            publish(pacenote);
                        }
                    }
                    catch (IOException error) { Log.Debug(error, "WRC7 bridge disconnected; awaiting reconnection"); }
                    catch (InvalidDataException error) { Log.Warn(error, "Rejected WRC7 bridge connection"); }
                    finally { Connected = false; }
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { Ready.TrySetCanceled(); }
            catch (Exception error)
            {
                Error = error.Message;
                Ready.TrySetException(error);
                Log.Error(error, "WRC7 bridge session failed");
            }
        });
    }

    internal static async Task<string?> ReadLineAsync(StreamReader reader, CancellationToken cancellation)
    {
        var text = new StringBuilder();
        var character = new char[1];
        while (await reader.ReadAsync(character.AsMemory(), cancellation) != 0)
        {
            if (character[0] == '\n') return text.ToString().TrimEnd('\r');
            if (text.Length == 1024) throw new InvalidDataException("Oversized WRC7 bridge message");
            text.Append(character[0]);
        }
        if (text.Length != 0) throw new InvalidDataException("Truncated WRC7 bridge message");
        return null;
    }

    public void Dispose()
    {
        cancellation.Cancel();
        _ = worker.ContinueWith(_ => cancellation.Dispose(), TaskScheduler.Default);
    }
}
