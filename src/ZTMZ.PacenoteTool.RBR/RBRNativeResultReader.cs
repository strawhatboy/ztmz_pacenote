using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace ZTMZ.PacenoteTool.RBR;

// Private native layout, verified against the executable and live RSF captures.
// RSF mode names and its stale saved result floats are deliberately not used.
internal readonly record struct RBRNativeResultSnapshot(
    uint RaceData, int MapId, int RawState, int Transition, int Mode, int PluginStage,
    bool OwnsMenu, int? CurrentPage, int? UnderlyingPage, int RaceFinished,
    int FinishLinePassed, float Countdown, float RaceTime)
{
    public bool Started => RawState == 1 && Transition == 0 && Mode == 1 && PluginStage == 1
        && RaceFinished == 0 && float.IsFinite(Countdown) && Countdown <= 0
        && float.IsFinite(RaceTime) && RaceTime >= 0;

    public bool Failed => RawState is 9 or 6 && Transition is 0 or 2
        && Mode == 1 && PluginStage == 1 && OwnsMenu
        && CurrentPage == 18 && UnderlyingPage == 18
        && RaceFinished == 1 && FinishLinePassed == 0;
}

internal sealed class RBRNativeResultReader(uint imageBase, Func<uint, int, byte[]?> read)
{
    internal const string SupportedHash = "5BB1B0C8F6719FAECAD63B0AAFD7541089D1D1FF7A4C9BB68ED49BB53DC6F63B";

    public static RBRNativeResultReader? Create(Process process, IntPtr handle)
    {
        var module = process.MainModule;
        if (module == null) return null;
        using var file = File.OpenRead(module.FileName);
        if (Convert.ToHexString(SHA256.HashData(file)) != SupportedHash) return null;
        return new RBRNativeResultReader(checked((uint)module.BaseAddress.ToInt64()), (address, size) =>
        {
            var buffer = new byte[size];
            return ReadProcessMemory(handle, (nint)(long)address, buffer, (nuint)size, out var count)
                && count == (nuint)size ? buffer : null;
        });
    }

    public RBRNativeResultSnapshot? Read()
    {
        try
        {
            uint Global(uint va) => UInt(checked(imageBase + va - 0x400000));
            var app = Global(0x7eac48);
            var race = Global(0x165fc68);
            var flow = Global(0x165fa90);
            var menu = Global(0x165fc48);
            var owner = Global(0x7eac44);
            var map = Global(0x1660804);
            var state = Bytes(checked(app + 0x724), 8);
            var mode = UInt(flow);
            var pluginOwner = UInt(checked(flow + 0xc98));
            if (pluginOwner < 0x10000) return null;
            var plugin = UInt(checked(pluginOwner + 0x1785c));
            var pages = Bytes(menu, 0xcc); // current/underlying page and failure-page slot
            var data = Bytes(race, 0x2c8);
            var failurePage = BitConverter.ToUInt32(pages, 0xc8);
            int? Page(int offset) => failurePage >= 0x10000 && BitConverter.ToUInt32(pages, offset) == failurePage ? 18 : null;

            // Reject observed tearing. This is not an atomic read or a result-generation counter.
            if (app != Global(0x7eac48) || race != Global(0x165fc68) || flow != Global(0x165fa90)
                || menu != Global(0x165fc48) || owner != Global(0x7eac44) || map != Global(0x1660804)
                || mode != UInt(flow) || pluginOwner != UInt(checked(flow + 0xc98))
                || plugin != UInt(checked(pluginOwner + 0x1785c))
                || !state.AsSpan().SequenceEqual(Bytes(checked(app + 0x724), 8))
                || !pages.AsSpan(8, 8).SequenceEqual(Bytes(checked(menu + 8), 8))
                || failurePage != UInt(checked(menu + 0xc8))) return null;

            return new(race, (int)map, BitConverter.ToInt32(state, 4), BitConverter.ToInt32(state, 0),
                (int)mode, (int)plugin, owner == menu, Page(8), Page(12), BitConverter.ToInt32(data, 0x144),
                BitConverter.ToInt32(data, 0x2c4), BitConverter.ToSingle(data, 0x244), BitConverter.ToSingle(data, 0x140));
        }
        catch (Exception ex) when (ex is InvalidDataException or OverflowException)
        {
            return null; // No fabricated zero result when memory is unavailable.
        }
    }

    private byte[] Bytes(uint address, int size)
    {
        if (address < 0x10000 || (ulong)address + (uint)size > 0x100000000UL)
            throw new InvalidDataException();
        var bytes = read(address, size);
        if (bytes == null || bytes.Length != size) throw new InvalidDataException();
        return bytes;
    }

    private uint UInt(uint address) => BitConverter.ToUInt32(Bytes(address, 4));

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(IntPtr process, nint address, [Out] byte[] buffer, nuint size, out nuint count);
}
