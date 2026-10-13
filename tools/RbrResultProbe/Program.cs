using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32.SafeHandles;

// Investigation only: never injects, writes game memory, or classifies a result.
return await Run(args);

static async Task<int> Run(string[] args)
{
    if (args.Length == 0 || args.Contains("--help"))
    {
        Console.WriteLine("RbrResultProbe --output <new.jsonl> [--pid <id>] [--seconds 300] [--interval-ms 20]");
        Console.WriteLine("Start RBR first. Capture before starting a stage. Ctrl+C stops safely. Existing files are never overwritten.");
        return 0;
    }
    try
    {
        if (!OperatingSystem.IsWindows()) throw new InvalidOperationException("Windows is required.");
        var options = new Dictionary<string, string>();
        for (var i = 0; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length || args[i] is not ("--output" or "--pid" or "--seconds" or "--interval-ms")
                || !options.TryAdd(args[i], args[i + 1]))
                throw new ArgumentException("Unknown, duplicate or incomplete option. Use --help.");
        }
        if (!options.TryGetValue("--output", out var path)) throw new ArgumentException("--output is required.");
        var seconds = int.Parse(options.GetValueOrDefault("--seconds", "300"));
        var interval = int.Parse(options.GetValueOrDefault("--interval-ms", "20"));
        if (seconds is < 1 or > 3600 || interval is < 10 or > 1000)
            throw new ArgumentException("Seconds must be 1..3600; interval must be 10..1000 ms.");
        using var process = FindProcess(options.GetValueOrDefault("--pid"));
        var main = process.MainModule ?? throw new InvalidOperationException("Executable module unavailable.");
        var exeHash = Hash(main.FileName);
        if (exeHash != Layout.ExeHash) throw new InvalidOperationException($"Unsupported executable SHA-256: {exeHash}");
        using var memory = new Memory(process.Id);
        var imageBase = checked((uint)main.BaseAddress.ToInt64());
        var rsfModule = process.Modules.Cast<ProcessModule>().FirstOrDefault(m =>
            string.Equals(m.ModuleName, "Rallysimfans.hu.dll", StringComparison.OrdinalIgnoreCase));
        var rsfHash = rsfModule is null ? null : Hash(rsfModule.FileName);
        uint? rsfBase = rsfHash == Layout.RsfHash ? checked((uint)rsfModule!.BaseAddress.ToInt64()) : null;

        using var output = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
        var json = new JsonSerializerOptions { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
        void Write(object value) => output.WriteLine(JsonSerializer.Serialize(value, json));
        Write(new
        {
            kind = "header", utc = DateTimeOffset.UtcNow, processId = process.Id, exeHash, imageBase,
            rsfHash, rsfBase, intervalMs = interval,
            note = "Raw observations only. RSF zero/stale times and menu IDs are not universal retirement flags. Polling can miss short transitions.",
            // Installed plugins may hook native code even when the on-disk hash matches.
            nativeResultHandlerBytes = Convert.ToHexString(memory.Bytes(imageBase + 0x80810, 16)),
            nativeRetireHandlerBytes = Convert.ToHexString(memory.Bytes(imageBase + 0x16e8a0, 16))
        });
        Console.WriteLine($"Recording PID {process.Id} to {Path.GetFullPath(path)}; RSF fields {(rsfBase.HasValue ? "enabled" : "unavailable/unverified")}.");
        using var stop = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; stop.Cancel(); };
        Console.CancelKeyPress += cancel;
        var clock = Stopwatch.StartNew();
        string? previous = null;
        long nextHeartbeat = 0, attempts = 0, samples = 0, errors = 0, changedDuringRead = 0;
        try
        {
            while (clock.Elapsed.TotalSeconds < seconds && !stop.IsCancellationRequested && !process.HasExited)
            {
                attempts++;
                try
                {
                    var snapshot = Capture(memory, imageBase, rsfBase);
                    if (!snapshot.Coherent) changedDuringRead++;
                    // Exclude continuously moving clocks/distances from the change key; keep a 1s heartbeat.
                    var key = JsonSerializer.Serialize(new
                    {
                        snapshot.Roots, snapshot.RawState, snapshot.Transition, snapshot.GameflowMode,
                        snapshot.PluginStage, snapshot.CurrentPage, snapshot.UnderlyingPage, snapshot.QueuedPage,
                        snapshot.QueueKind, snapshot.MenuOwned, snapshot.RaceFinished, snapshot.FinishLinePassed,
                        snapshot.FalseStart, snapshot.WarningEndLatch, snapshot.MapId, snapshot.Rsf, snapshot.Coherent
                    }, json);
                    if (key != previous || clock.ElapsedMilliseconds >= nextHeartbeat)
                    {
                        Write(new { kind = "sample", utc = DateTimeOffset.UtcNow, elapsedMs = clock.ElapsedMilliseconds, snapshot });
                        if (key != previous)
                            Console.WriteLine($"{clock.Elapsed.TotalSeconds:F2}s state={snapshot.RawState} mode={snapshot.GameflowMode} page={snapshot.CurrentPage} transition={snapshot.Transition} coherent={snapshot.Coherent}");
                        previous = key;
                        nextHeartbeat = clock.ElapsedMilliseconds + 1000;
                        samples++;
                    }
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidDataException or OverflowException)
                {
                    errors++;
                    var key = "unavailable:" + ex.Message;
                    if (previous != key || clock.ElapsedMilliseconds >= nextHeartbeat)
                    {
                        Write(new { kind = "unavailable", utc = DateTimeOffset.UtcNow, elapsedMs = clock.ElapsedMilliseconds, error = ex.Message });
                        Console.WriteLine(key);
                        previous = key;
                        nextHeartbeat = clock.ElapsedMilliseconds + 1000;
                    }
                }
                try { await Task.Delay(interval, stop.Token); }
                catch (OperationCanceledException) { break; }
            }
        }
        finally { Console.CancelKeyPress -= cancel; }
        Write(new { kind = "end", utc = DateTimeOffset.UtcNow, attempts, samples, errors, changedDuringRead });
        Console.WriteLine($"Stopped: {samples} recorded samples, {errors} unavailable reads, {changedDuringRead} visibly changing snapshots.");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
}

static Process FindProcess(string? pid)
{
    if (pid is not null) return Process.GetProcessById(int.Parse(pid));
    var matches = Process.GetProcessesByName("RichardBurnsRally_SSE")
        .Concat(Process.GetProcessesByName("RichardBurnsRally")).ToArray();
    if (matches.Length == 1) return matches[0];
    foreach (var p in matches) p.Dispose();
    throw new InvalidOperationException(matches.Length == 0 ? "RBR is not running. Start it first." : "Multiple RBR processes; select --pid.");
}

static string Hash(string path)
{
    using var input = File.OpenRead(path);
    return Convert.ToHexString(SHA256.HashData(input));
}

static Snapshot Capture(Memory memory, uint imageBase, uint? rsfBase)
{
    uint Global(uint va) => memory.UInt(checked(imageBase + va - 0x400000));
    Roots ReadRoots() => new(Global(0x7eac48), Global(0x165fc68), Global(0x165fa90), Global(0x165fc48), Global(0x7eac44));
    var roots = ReadRoots();
    var app = memory.Bytes(checked(roots.Application + 0x724), 8);
    var menu = memory.Bytes(roots.ResultMenu, 0x164); // includes page slots 0..56
    var race = memory.Bytes(roots.RaceData, 0x2c8);
    var mode = memory.Int(roots.Gameflow);
    var pluginOwner = memory.UInt(checked(roots.Gameflow + 0xc98));
    int? pluginStage = pluginOwner == 0 ? null : memory.Int(checked(pluginOwner + 0x1785c));
    int? Page(int offset)
    {
        var pointer = BitConverter.ToUInt32(menu, offset);
        if (pointer == 0) return null;
        for (var id = 0; id < 57; id++)
            if (pointer == BitConverter.ToUInt32(menu, 0x80 + id * 4)) return id;
        return -1; // non-null page not found in the known table
    }
    RsfValues? rsf = null;
    if (rsfBase is uint module)
    {
        var times = memory.Bytes(checked(module + 0x3c2094), 12);
        rsf = new(BitConverter.ToSingle(times, 0), BitConverter.ToSingle(times, 4), BitConverter.ToSingle(times, 8),
            memory.Bytes(checked(module + 0x3bdd83), 1)[0], memory.Bytes(checked(module + 0x3c1db8), 1)[0]);
    }
    // This rejects visible tearing only. Separate remote reads are never an atomic snapshot.
    var coherent = roots == ReadRoots()
        && app.AsSpan().SequenceEqual(memory.Bytes(checked(roots.Application + 0x724), 8))
        && menu.AsSpan(8, 8).SequenceEqual(memory.Bytes(checked(roots.ResultMenu + 8), 8))
        && menu.AsSpan(0x1c, 4).SequenceEqual(memory.Bytes(checked(roots.ResultMenu + 0x1c), 4))
        && mode == memory.Int(roots.Gameflow);
    return new(roots, BitConverter.ToInt32(app, 4), BitConverter.ToInt32(app, 0), mode, pluginStage,
        Page(8), Page(12), Page(0x1c), BitConverter.ToInt32(menu, 0x2c), roots.ActiveMenu == roots.ResultMenu,
        BitConverter.ToInt32(race, 0x144), BitConverter.ToInt32(race, 0x2c4), BitConverter.ToInt32(race, 0x248),
        BitConverter.ToInt32(race, 0x134), BitConverter.ToSingle(race, 0x140), BitConverter.ToSingle(race, 0x244),
        BitConverter.ToSingle(race, 0x20), BitConverter.ToSingle(race, 0x28), Global(0x1660804), rsf, coherent);
}

record Roots(uint Application, uint RaceData, uint Gameflow, uint ResultMenu, uint ActiveMenu);
record RsfValues(float Checkpoint1, float Checkpoint2, float FinishTime, byte RawStageFlag, byte RawSaveFlag);
record Snapshot(Roots Roots, int RawState, int Transition, int GameflowMode, int? PluginStage,
    int? CurrentPage, int? UnderlyingPage, int? QueuedPage, int QueueKind, bool MenuOwned,
    int RaceFinished, int FinishLinePassed, int FalseStart, int WarningEndLatch,
    float RaceTime, float Countdown, float DistanceFromStart, float DistanceToFinish, uint MapId, RsfValues? Rsf, bool Coherent);

static class Layout
{
    public const string ExeHash = "5BB1B0C8F6719FAECAD63B0AAFD7541089D1D1FF7A4C9BB68ED49BB53DC6F63B";
    public const string RsfHash = "F9B7D8AA504DF0C6D046BB94B439ED3699756364583D6559578BC3DA29865F97";
}

sealed class Memory : IDisposable
{
    private readonly SafeProcessHandle handle;
    public Memory(int pid)
    {
        handle = OpenProcess(0x0010 | 0x1000, false, pid); // VM_READ | QUERY_LIMITED_INFORMATION only
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error);
        }
    }
    public byte[] Bytes(uint address, int count)
    {
        if (address < 0x10000 || (ulong)address + (uint)count > 0x100000000UL)
            throw new InvalidDataException($"Invalid read address 0x{address:x} ({count} bytes).");
        var buffer = new byte[count];
        if (!ReadProcessMemory(handle, (nint)(long)address, buffer, (nuint)count, out var read) || read != (nuint)count)
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Read unavailable at 0x{address:x} ({count} bytes).");
        return buffer;
    }
    public uint UInt(uint address) => BitConverter.ToUInt32(Bytes(address, 4));
    public int Int(uint address) => BitConverter.ToInt32(Bytes(address, 4));
    public void Dispose() => handle.Dispose();
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(SafeProcessHandle process, nint address, [Out] byte[] buffer, nuint count, out nuint read);
}
