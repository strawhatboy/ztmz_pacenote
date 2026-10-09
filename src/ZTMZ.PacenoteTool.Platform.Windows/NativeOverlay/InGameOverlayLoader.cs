using System.ComponentModel;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ZTMZ.PacenoteTool.Platform.Windows.NativeOverlay;

// Caller must enforce OverlayBackendPolicy before invoking this Windows-only mechanism.
internal static class InGameOverlayLoader
{
    private static readonly ConcurrentDictionary<string, Lazy<Task>> Attempts = new();
    public static string GetLibraryPath(Process process)
    {
        using var target = OpenProcess(0x1000, false, process.Id);
        if (target.IsInvalid || !IsWow64Process2(target, out var machine, out var native))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot determine game architecture.");
        var rid = native == 0x8664 && machine == 0x014c ? "win-x86"
            : native == 0x8664 && machine == 0 ? "win-x64"
            : throw new PlatformNotSupportedException("Only x86 and x64 games on x64 Windows are supported.");
        return Path.Combine(AppContext.BaseDirectory, "native-dashboard", rid, "ztmz_dashboard_overlay.dll");
    }

    public static Task LoadAsync(Process process, string path)
    {
        var key = $"{process.Id}:{process.StartTime.ToUniversalTime().ToFileTimeUtc()}";
        // In particular, never repeat an ambiguous remote-load timeout in this host.
        return Attempts.GetOrAdd(key, _ => new Lazy<Task>(() => LoadOnceAsync(process, path))).Value;
    }

    private static async Task LoadOnceAsync(Process process, string path)
    {
        if (!Environment.Is64BitProcess || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("In-game overlay currently requires an x64 host.");
        path = Path.GetFullPath(path);
        Machine dllMachine;
        using (var file = File.OpenRead(path))
        using (var pe = new PEReader(file))
        {
            dllMachine = pe.PEHeaders.CoffHeader.Machine;
            if (dllMachine is not (Machine.Amd64 or Machine.I386) ||
                !pe.PEHeaders.CoffHeader.Characteristics.HasFlag(Characteristics.Dll))
                throw new BadImageFormatException("Overlay module must be an x86 or x64 DLL.");
        }
        var version = FileVersionInfo.GetVersionInfo(path);
        if (version.FileVersion != "1.0.0.0" || version.ProductName != "ZTMZ Native Dashboard")
            throw new InvalidDataException("Unsupported overlay module version; rebuild the native dashboard bundle.");
        using var target = OpenProcess(0x0002 | 0x0008 | 0x0010 | 0x0020 | 0x1000, false, process.Id);
        if (target.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot access game for overlay loading (check elevation).");
        if (!IsWow64Process2(target, out var machine, out var native) || native != 0x8664 || machine is not (0 or 0x014c))
            throw new PlatformNotSupportedException("The target must be an x86 or x64 process on x64 Windows.");
        if (dllMachine != (machine == 0x014c ? Machine.I386 : Machine.Amd64))
            throw new BadImageFormatException("Overlay DLL architecture does not match the game.");
        // Bind the opened handle to the captured Process incarnation, not just its PID.
        if (!GetProcessTimes(target, out long created, out _, out _, out _) ||
            created != process.StartTime.ToUniversalTime().ToFileTimeUtc())
            throw new InvalidOperationException("Game process changed during attachment.");

        if (machine == 0x014c)
        {
            var helper = Path.Combine(Path.GetDirectoryName(path)!, "ztmz_dashboard_injector.exe");
            using (var helperFile = File.OpenRead(helper))
            using (var helperPe = new PEReader(helperFile))
                if (helperPe.PEHeaders.CoffHeader.Machine != Machine.I386)
                    throw new BadImageFormatException("The WOW64 loader helper must be x86.");
            var start = new ProcessStartInfo(helper) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
            start.ArgumentList.Add(process.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
            start.ArgumentList.Add(created.ToString(System.Globalization.CultureInfo.InvariantCulture));
            using var injector = Process.Start(start) ?? throw new InvalidOperationException("Could not start x86 loader helper.");
            var errors = injector.StandardError.ReadToEndAsync();
            await injector.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
            if (injector.ExitCode != 0) throw new InvalidOperationException("x86 overlay loading failed: " + await errors.ConfigureAwait(false));
            process.Refresh();
            if (!process.Modules.Cast<ProcessModule>().Any(m => string.Equals(m.FileName, path, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("x86 overlay DLL was not found in the target after loading.");
            return;
        }

        nint loadLibrary = GetProcAddress(GetModuleHandleW("kernel32.dll"), "LoadLibraryW");
        if (loadLibrary == 0 || !GetModuleHandleExW(0x0004 | 0x0002, loadLibrary, out var owner))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot resolve the system loader.");
        using var local = Process.GetCurrentProcess();
        var localModule = local.Modules.Cast<ProcessModule>().Single(m => m.BaseAddress == owner);
        process.Refresh();
        var remoteModule = process.Modules.Cast<ProcessModule>().SingleOrDefault(m =>
            string.Equals(m.FileName, localModule.FileName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Matching system loader module is absent from the target.");
        long offset = loadLibrary.ToInt64() - owner.ToInt64();
        if (offset < 0 || offset >= remoteModule.ModuleMemorySize)
            throw new InvalidOperationException("Invalid system loader export offset.");
        nint remoteLoader = remoteModule.BaseAddress + checked((int)offset);
        byte[] bytes = Encoding.Unicode.GetBytes(path + '\0');
        nint memory = VirtualAllocEx(target, 0, (nuint)bytes.Length, 0x3000, 0x04);
        if (memory == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        SafeWaitHandle? thread = null;
        bool finished = false;
        try
        {
            if (!WriteProcessMemory(target, memory, bytes, (nuint)bytes.Length, out var written) || written != (nuint)bytes.Length)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            thread = CreateRemoteThread(target, 0, 0, remoteLoader, memory, 0, out _);
            if (thread.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
            uint wait = await Task.Run(() => WaitForSingleObject(thread, 10000)).ConfigureAwait(false);
            finished = wait == 0;
            if (!finished)
                throw new TimeoutException("Overlay loading did not finish. Restart the game before retrying.");
            process.Refresh();
            if (!process.Modules.Cast<ProcessModule>().Any(m => string.Equals(m.FileName, path, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Windows did not load the overlay DLL; check its dependencies.");
        }
        finally
        {
            // Never free the DLL path while a timed-out remote loader may still read it.
            // On timeout this small allocation remains until process exit; no thread termination.
            if (thread == null || thread.IsInvalid || finished) VirtualFreeEx(target, memory, 0, 0x8000);
            thread?.Dispose();
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool IsWow64Process2(SafeProcessHandle process, out ushort machine, out ushort native);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetProcessTimes(SafeProcessHandle process, out long created, out long exited, out long kernel, out long user);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandleW(string name);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)] private static extern nint GetProcAddress(nint module, string name);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetModuleHandleExW(uint flags, nint address, out nint module);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint VirtualAllocEx(SafeProcessHandle process, nint address, nuint size, uint allocation, uint protection);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool VirtualFreeEx(SafeProcessHandle process, nint address, nuint size, uint freeType);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool WriteProcessMemory(SafeProcessHandle process, nint address, byte[] buffer, nuint size, out nuint written);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeWaitHandle CreateRemoteThread(SafeProcessHandle process, nint attributes, nuint stack, nint start, nint argument, uint flags, out uint id);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint WaitForSingleObject(SafeWaitHandle handle, uint milliseconds);
}
