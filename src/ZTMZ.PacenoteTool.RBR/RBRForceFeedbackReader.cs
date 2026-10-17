using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.RBR;

// Reads the native getter's input AFTER the physics/NGP force shaping. This is
// the game's signed command before device gain/deadzone, not wheel torque.
internal sealed class RBRForceFeedbackReader(Func<uint, int, byte[]?> read)
{
    internal static readonly byte[] GetterCode = Convert.FromHexString(
        "518B49048B0183E8000F57C0F30F110424740A487519F30F104124EB05F30F104120" +
        "F30F5905D80E6601F30F110424A1DC0E660185C0750DF30F5905A0347300F30F110424D9042459C3");

    public static RBRForceFeedbackReader? Create(Process process, IntPtr handle)
    {
        var module = process.MainModule;
        // Only this executable/layout has been verified, including its native getter.
        if (module == null || module.BaseAddress.ToInt64() != 0x400000) return null;
        using var file = File.OpenRead(module.FileName);
        if (Convert.ToHexString(SHA256.HashData(file)) != RBRNativeResultReader.SupportedHash) return null;
        return new((address, size) =>
        {
            var bytes = new byte[size];
            return ReadProcessMemory(handle, (nint)(long)address, bytes, (nuint)size, out var count)
                && count == (nuint)size ? bytes : null;
        });
    }

    public ForceFeedbackTelemetrySnapshot? Read()
    {
        try
        {
            // Recheck the getter so a later plugin hook cannot silently change its meaning.
            if (!Bytes(0x4c5f90, GetterCode.Length).AsSpan().SequenceEqual(GetterCode)) return null;
            var app = UInt(0x7eac48);
            var rawState = UInt(checked(app + 0x728));
            if (rawState is not (1 or 16)) return null; // Paused/menu/replay fields can be stale.
            var controller = UInt(0x8951d0);
            var state = UInt(checked(controller + 4));
            var block = Bytes(state, 0x24);
            // Mode 1 is a calibration force, not road feedback; other modes are inactive.
            if (BitConverter.ToUInt32(block, 0) != 0) return null;
            var force = BitConverter.ToSingle(block, 0x20);
            var settings = Bytes(0x1660ed8, 8);
            var scale = BitConverter.ToSingle(settings, 0);
            var invert = BitConverter.ToInt32(settings, 4) == 0;
            if (!float.IsFinite(force) || force is < -1 or > 1 || !float.IsFinite(scale)) return null;

            // Reject observed transitions; polling is not an atomic simulation snapshot.
            if (app != UInt(0x7eac48) || rawState != UInt(checked(app + 0x728))
                || controller != UInt(0x8951d0) || state != UInt(checked(controller + 4))
                || UInt(state) != 0 || !settings.AsSpan().SequenceEqual(Bytes(0x1660ed8, 8))) return null;

            return ForceFeedbackTelemetrySnapshot.FromNormalizedOutput("RBR", force * scale * (invert ? -1 : 1));
        }
        catch (Exception ex) when (ex is InvalidDataException or OverflowException)
        {
            return null;
        }
    }

    private uint UInt(uint address) => BitConverter.ToUInt32(Bytes(address, 4));

    private byte[] Bytes(uint address, int size)
    {
        if (address < 0x10000 || (ulong)address + (uint)size > 0x100000000UL)
            throw new InvalidDataException();
        var bytes = read(address, size);
        if (bytes == null || bytes.Length != size) throw new InvalidDataException();
        return bytes;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(IntPtr process, nint address, [Out] byte[] buffer, nuint size, out nuint count);
}
