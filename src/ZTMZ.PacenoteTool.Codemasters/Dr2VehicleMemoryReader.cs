using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using NLog;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.Codemasters;

[SupportedOSPlatform("windows")]
internal sealed class Dr2VehicleMemoryReader : IDisposable
{
    private readonly object gate = new();
    private readonly Logger logger = LogManager.GetCurrentClassLogger();
    private Task<Session?>? opening;
    private Session? session;
    private long nextDiscovery;
    private (int, long) rejected;
    private bool disposed;
    private bool available;
    private ulong damagePhysics;
    private long nextDamageRead;
    private CarDamageSnapshot? damage;

    public Dr2StartLightsSnapshot? ReadStartLights()
    {
        lock (gate)
        {
            if (disposed) return null;
            UpdateSession();
            if (session is null) return null;
            try { return Dr2StartLightsMemory.Read(session.Read, session.ImageBase); }
            catch (IOException) { return null; }
        }
    }

    public Dr2RaceStateSnapshot? ReadRaceState()
    {
        lock (gate)
        {
            if (disposed) return null;
            UpdateSession();
            if (session is null) return null;
            try { return Dr2RaceStateMemory.Read(session.Read, session.ImageBase); }
            catch (IOException) { return null; }
        }
    }

    private void UpdateSession()
    {
        if (opening?.IsCompleted == true)
        {
            session = opening.GetAwaiter().GetResult();
            opening = null;
        }
        if (Environment.TickCount64 < nextDiscovery) return;
        nextDiscovery = Environment.TickCount64 + 5000;
        if (session?.Process.HasExited == true) { session.Dispose(); session = null; }
        if (session is null && opening is null) opening = Task.Run(Open);
    }

    public Dr2VehicleTelemetry? Read(float idle, float maximum)
    {
        lock (gate)
        {
            if (disposed) return null;
            UpdateSession();
            Dr2VehicleTelemetry? snapshot = null;
            if (session is not null)
            {
                try { snapshot = Dr2VehicleMemory.Read(session.Read, session.ImageBase, idle, maximum); }
                catch (IOException) { /* Stage loading and vehicle replacement invalidate pointers. */ }
                if (snapshot is { } vehicle)
                {
                    var now = Environment.TickCount64;
                    if (vehicle.PhysicsAddress != damagePhysics || now >= nextDamageRead)
                    {
                        damagePhysics = vehicle.PhysicsAddress;
                        nextDamageRead = now + 200;
                        damage = null;
                        try { damage = Dr2CarDamage.Read(session.Read, damagePhysics); }
                        catch (IOException) { /* Optional damage must not disable handbrake/RPM. */ }
                    }
                    snapshot = vehicle with { Damage = damage };
                }
                else { damagePhysics = 0; damage = null; }
            }
            if (available != snapshot.HasValue)
            {
                available = snapshot.HasValue;
                logger.Info("DR2 supplemental vehicle telemetry {0}.", available ? "available" : "unavailable");
            }
            return snapshot;
        }
    }

    private Session? Open()
    {
        Process[] processes = [];
        try
        {
            processes = Process.GetProcessesByName("dirtrally2");
            if (processes.Length != 1) return null;
            var process = processes[0];
            var identity = (process.Id, process.StartTime.ToUniversalTime().Ticks);
            if (identity == rejected) return null;
            var module = process.MainModule ?? throw new IOException("DR2 executable unavailable.");
            using var binary = File.OpenRead(module.FileName);
            if (Convert.ToHexString(SHA256.HashData(binary)) != Dr2VehicleMemory.SupportedHash)
            {
                rejected = identity;
                logger.Warn("DR2 supplemental telemetry disabled: executable build not verified. UDP remains available.");
                return null;
            }
            return new Session(Process.GetProcessById(process.Id), (ulong)module.BaseAddress.ToInt64());
        }
        catch (Exception ex) { logger.Debug(ex, "DR2 telemetry discovery will retry."); return null; }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            session?.Dispose();
            session = null;
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
            handle = OpenProcess(0x1010, false, process.Id);
            if (handle.IsInvalid) { Dispose(); throw new IOException("Cannot read DR2 process."); }
        }
        public byte[] Read(ulong address, int count)
        {
            if (address < 0x10000 || address > 0x7fffffffffff || count is < 1 or > 65536)
                throw new IOException("Invalid DR2 telemetry address.");
            var bytes = new byte[count];
            if (!ReadProcessMemory(handle, (nint)address, bytes, (nuint)count, out var read) || read != (nuint)count)
                throw new IOException("DR2 vehicle data unavailable.");
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
