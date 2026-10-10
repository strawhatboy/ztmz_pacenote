using System.IO.Pipes;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.Base.Script;

namespace ZTMZ.PacenoteTool.KT;

internal sealed class WrcGenerationsLiveSession : IDisposable
{
    private static readonly NLog.Logger Log = NLog.LogManager.GetCurrentClassLogger();
    private readonly CancellationTokenSource cancellation = new();
    private readonly Task worker;
    private readonly HashSet<string> reported = new(StringComparer.Ordinal);
    public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool Connected { get; private set; }
    public string? Error { get; private set; }

    public WrcGenerationsLiveSession(int pid, string directory, Action<LivePacenoteEvent> publish,
        Func<WrcGenerationsPacenoteCatalog>? catalogLoader = null, IReadOnlySet<string>? tokenVocabulary = null)
    {
        worker = Task.Run(async () =>
        {
            try
            {
                var catalog = catalogLoader?.Invoke() ?? WrcGenerationsArchive.Load(directory);
                var vocabulary = tokenVocabulary ?? Base.Script.ScriptResource.Instance.FilenameToIdDict.Keys.ToHashSet(StringComparer.Ordinal);
                Log.Info("WRCG catalog loaded: {0} blocks, {1} atoms", catalog.BlockCount, catalog.AtomCount);
                while (!cancellation.IsCancellationRequested)
                {
                    await using var pipe = new NamedPipeServerStream($"ZTMZ.WRCG.Live.v1.{pid}", PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await pipe.WaitForConnectionAsync(cancellation.Token);
                    using var reader = new StreamReader(pipe);
                    if (await reader.ReadLineAsync(cancellation.Token) != "READY") continue;
                    Connected = true;
                    Ready.TrySetResult();
                    Log.Info("WRCG live bridge ready for process {0}", pid);
                    try
                    {
                        while (await reader.ReadLineAsync(cancellation.Token) is { } line)
                        {
                            if (line.Length > 1024) throw new InvalidDataException("Oversized bridge message");
                            if (line.StartsWith("DROPPED ", StringComparison.Ordinal)) { Log.Warn("WRCG bridge queue: {0}", line); continue; }
                            if (line.StartsWith("STATS ", StringComparison.Ordinal)) { Log.Debug("WRCG bridge {0}", line); continue; }
                            var fields = line.Split('\t', 2);
                            if (fields.Length != 2 || !long.TryParse(fields[0], out var tick)) continue;
                            var age = Environment.TickCount64 - tick;
                            Log.Debug("WRCG sound received: tick={0}, ageMs={1}, resource={2}", tick, age, fields[1]);
                            if (age < 0 || age > 500)
                            {
                                Log.Debug("WRCG sound rejected as stale: {0}", fields[1]);
                                continue;
                            }
                            if (TryGetCountdownSound(fields[1], out var countdownSound))
                            {
                                Log.Info("WRCG countdown cue: {0}, ageMs={1}", fields[1], age);
                                publish(new LivePacenoteEvent
                                {
                                    Id = $"{pid}:{tick}:countdown", SourceResourceName = fields[1],
                                    SystemSoundToken = countdownSound, SystemSoundSequential = false
                                });
                                continue;
                            }
                            if (!catalog.TryResolve(fields[1], out var atoms))
                            {
                                Log.Debug("WRCG sound has no pacenote mapping (no lifecycle inferred): {0}", fields[1]);
                                continue;
                            }
                            var tokens = new List<string>();
                            var complete = true;
                            foreach (var atom in atoms)
                            {
                                if (WrcGenerationsTokenMapper.TryMap(atom, vocabulary, out var mapped)) tokens.AddRange(mapped);
                                else
                                {
                                    complete = false;
                                    if (reported.Add(atom)) Log.Warn("Unmapped WRCG atom: {0}; retaining original game audio", atom);
                                }
                            }
                            // Never silently remove a warning/modifier from a partially mapped call.
                            if (!complete || tokens.Count == 0) continue;
                            Log.Debug("WRCG sound mapped: {0} -> {1}", fields[1], string.Join('/', tokens));
                            publish(new LivePacenoteEvent
                            {
                                Id = $"{pid}:{tick}", DisplayGroupId = $"{pid}:{tick}", SourceResourceName = fields[1],
                                Record = new PacenoteRecord { RawText = string.Join('/', atoms), Pacenotes = tokens.Select(t => new Pacenote { Note = t }).ToList() }
                            });
                        }
                    }
                    catch (IOException error) { Log.Debug(error, "WRCG bridge disconnected; awaiting reconnection"); }
                    finally { Connected = false; }
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception error)
            {
                Error = error.Message;
                Ready.TrySetException(error);
                Log.Error(error, "WRCG bridge session failed");
            }
        });
    }

    internal static bool TryGetCountdownSound(string resource, out string token)
    {
        const string prefix = "ztmz:/countdown/";
        token = string.Empty;
        if (!resource.StartsWith(prefix, StringComparison.Ordinal) ||
            !int.TryParse(resource.AsSpan(prefix.Length), out var number) || number < 0 || number > 5) return false;
        token = number == 0 ? Constants.SYSTEM_GO : Constants.SYSTEM_COUNTDOWNS[number - 1];
        return true;
    }

    public void Dispose()
    {
        cancellation.Cancel();
        _ = worker.ContinueWith(_ => cancellation.Dispose(), TaskScheduler.Default);
    }
}
