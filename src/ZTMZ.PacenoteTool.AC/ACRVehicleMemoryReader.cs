using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;
using NLog;

namespace ZTMZ.PacenoteTool.AC;

[SupportedOSPlatform("windows")]
internal sealed class ACRVehicleMemoryReader : IDisposable
{
    private readonly object gate = new();
    private readonly ILogger logger = LogManager.GetCurrentClassLogger();
    private Task<Session?>? opening;
    private Session? session;
    private bool disposed;
    private bool available;
    private long nextDiscovery;
    private (int Pid, long Started) rejectedProcess;
    private ulong damageVehicle;
    private long nextDamageRead;
    private ZTMZ.PacenoteTool.Base.Game.CarDamageSnapshot? damage;
    private long nextStageRead;
    public ACRStageState? StageState { get; private set; }

    public ACRVehicleTelemetry? Read()
    {
        if (!Monitor.TryEnter(gate)) return null;
        try
        {
            if (disposed) return null;
            if (opening?.IsCompleted == true)
            {
                session = opening.GetAwaiter().GetResult();
                opening = null;
            }
            if (Environment.TickCount64 >= nextDiscovery)
            {
                nextDiscovery = Environment.TickCount64 + 5000;
                if (session?.Process.HasExited == true) { session.Dispose(); session = null; }
                // Hashing a large executable must not block the telemetry timer or UI.
                if (session is null && opening is null) opening = Task.Run(Open);
            }
            ACRVehicleTelemetry? snapshot = null;
            if (session is not null)
            {
                if (Environment.TickCount64 >= nextStageRead)
                {
                    nextStageRead = Environment.TickCount64 + 100;
                    try { StageState = ACRStageMemory.Read(session.Read, session.ImageBase); }
                    catch (IOException) { StageState = null; }
                }
                try { snapshot = ACRVehicleMemory.Read(session.Read, session.ImageBase); }
                catch (IOException) { /* Menus, stage loading and vehicle replacement. */ }
            }
            else StageState = null;
            if (snapshot is null)
            {
                damage = null;
                damageVehicle = 0;
            }
            else if (session is not null)
            {
                if (snapshot.VehicleIdentity != damageVehicle || Environment.TickCount64 >= nextDamageRead)
                {
                    damageVehicle = snapshot.VehicleIdentity;
                    nextDamageRead = Environment.TickCount64 + 200;
                    // Component health changes slowly; never perform its name /
                    // collection reads at the physics telemetry rate.
                    try { damage = ACRCarDamage.Read(session.Read, session.ImageBase, damageVehicle); }
                    catch (IOException) { damage = null; }
                }
                snapshot = snapshot with { Damage = damage };
            }
            if (available != (snapshot is not null))
            {
                available = snapshot is not null;
                logger.Info("ACR supplemental vehicle telemetry {0}.", available ? "available" : "unavailable");
            }
            return snapshot;
        }
        finally { Monitor.Exit(gate); }
    }

    private Session? Open()
    {
        Process[] processes = [];
        try
        {
            processes = Process.GetProcessesByName("acr");
            if (processes.Length != 1) return null;
            var process = processes[0];
            var identity = (process.Id, process.StartTime.ToUniversalTime().Ticks);
            if (identity == rejectedProcess) return null;
            var module = process.MainModule ?? throw new IOException("ACR executable unavailable.");
            using var binary = File.OpenRead(module.FileName);
            if (Convert.ToHexString(SHA256.HashData(binary)) != ACRVehicleMemory.SupportedHash)
            {
                rejectedProcess = identity;
                logger.Warn("ACR supplemental telemetry disabled: executable build not verified. Shared-memory telemetry remains enabled.");
                return null;
            }
            return new Session(Process.GetProcessById(process.Id), (ulong)module.BaseAddress.ToInt64());
        }
        catch (Exception ex)
        {
            logger.Debug(ex, "ACR supplemental telemetry discovery will retry.");
            return null;
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            session?.Dispose();
            session = null;
            StageState = null;
            if (opening is not null)
                _ = opening.ContinueWith(task => task.GetAwaiter().GetResult()?.Dispose(), TaskScheduler.Default);
            opening = null;
        }
    }

    private sealed class Session : IDisposable
    {
        public Process Process { get; }
        public ulong ImageBase { get; }
        private readonly SafeProcessHandle handle;
        public Session(Process process, ulong imageBase)
        {
            Process = process;
            ImageBase = imageBase;
            handle = OpenProcess(0x1010, false, process.Id); // Query + VM_READ only.
            if (handle.IsInvalid) { Dispose(); throw new IOException("Cannot read ACR process."); }
        }
        public byte[] Read(ulong address, int count)
        {
            if (address < 0x10000 || address > 0x7fffffffffff || count is < 1 or > 65536)
                throw new IOException("Invalid ACR telemetry address.");
            var bytes = new byte[count];
            if (!ReadProcessMemory(handle, (nint)address, bytes, (nuint)count, out var read) || read != (nuint)count)
                throw new IOException("ACR vehicle data unavailable.");
            return bytes;
        }
        public void Dispose() { handle.Dispose(); Process.Dispose(); }
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(SafeProcessHandle process, nint address, [Out] byte[] bytes, nuint size, out nuint read);
}
