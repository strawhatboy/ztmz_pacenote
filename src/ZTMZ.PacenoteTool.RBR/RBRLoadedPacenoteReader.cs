using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace ZTMZ.PacenoteTool.RBR;

internal sealed record RBRLoadedPacenote(int MapId, string Title, string FilePath)
{
    public bool IsLuppis => Title.Contains("Luppis", StringComparison.OrdinalIgnoreCase);
}

// RSF owns the displayed "Pacenote: ..." title. See Docs/RBRLoadedPacenoteTitle.md.
// Read on roadbook load, never scan the heap or consume the last log message.
internal sealed class RBRLoadedPacenoteReader(uint imageBase, uint rsfBase, Func<uint, int, byte[]?> read)
{
    internal const string SupportedRsfHash = "F9B7D8AA504DF0C6D046BB94B439ED3699756364583D6559578BC3DA29865F97";
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

    public static RBRLoadedPacenoteReader? Create(Process process, IntPtr handle)
    {
        var main = process.MainModule;
        var rsf = process.Modules.Cast<ProcessModule>().FirstOrDefault(m =>
            m.ModuleName.Equals("Rallysimfans.hu.dll", StringComparison.OrdinalIgnoreCase));
        if (main == null || rsf == null || Hash(main.FileName) != RBRNativeResultReader.SupportedHash
            || Hash(rsf.FileName) != SupportedRsfHash) return null;
        return new(checked((uint)main.BaseAddress.ToInt64()), checked((uint)rsf.BaseAddress.ToInt64()), (address, size) =>
        {
            var buffer = new byte[size];
            return ReadProcessMemory(handle, (nint)(long)address, buffer, (nuint)size, out var count)
                && count == (nuint)size ? buffer : null;
        });
    }

    public RBRLoadedPacenote? Read(int expectedMapId)
    {
        try
        {
            uint Game(uint va) => UInt(checked(imageBase + va - 0x400000));
            var app = Game(0x7eac48);
            var state = Bytes(checked(app + 0x724), 8);
            // Titles may remain after leaving a stage; never accept them in menus/loading.
            if (BitConverter.ToInt32(state, 0) != 0 || BitConverter.ToInt32(state, 4) is not (1 or 2 or 8 or 16)) return null;
            if (Game(0x1660804) != expectedMapId || UInt(checked(rsfBase + 0x3c20fc)) != expectedMapId) return null;
            var root = Game(0x7eaba8);
            var notes = UInt(checked(root + 0x10));
            var noteHeader = Bytes(checked(notes + 0x20), 8);
            var count = BitConverter.ToUInt32(noteHeader, 0);
            if (count is 0 or > 100000 || BitConverter.ToUInt32(noteHeader, 4) < 0x10000) return null;
            if (Bytes(checked(rsfBase + 0x3c261d), 1)[0] == 0) return null;
            var headers = Bytes(checked(rsfBase + 0x3c2564), 32);
            var key = Bytes(checked(rsfBase + 0x3c27a0), 92);
            var pathBytes = ReadString(headers.AsSpan(0, 16));
            var titleBytes = ReadString(headers.AsSpan(16, 16));
            var path = Decode(pathBytes, key);
            var title = Decode(titleBytes, key);
            if (string.IsNullOrWhiteSpace(title) || title.Any(char.IsControl) || path.Any(char.IsControl)
                || !Path.IsPathFullyQualified(path) || !Path.GetExtension(path).Equals(".ini", StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileNameWithoutExtension(path).Equals(title, StringComparison.OrdinalIgnoreCase)) return null;

            // Reject visible stage/selection changes and in-place string replacement during the read.
            if (app != Game(0x7eac48) || !state.AsSpan().SequenceEqual(Bytes(checked(app + 0x724), 8))
                || Game(0x1660804) != expectedMapId || UInt(checked(rsfBase + 0x3c20fc)) != expectedMapId
                || root != Game(0x7eaba8) || notes != UInt(checked(root + 0x10))
                || !noteHeader.AsSpan().SequenceEqual(Bytes(checked(notes + 0x20), 8))
                || !headers.AsSpan().SequenceEqual(Bytes(checked(rsfBase + 0x3c2564), 32))
                || !key.AsSpan().SequenceEqual(Bytes(checked(rsfBase + 0x3c27a0), 92))
                || !pathBytes.AsSpan().SequenceEqual(ReadString(headers.AsSpan(0, 16)))
                || !titleBytes.AsSpan().SequenceEqual(ReadString(headers.AsSpan(16, 16)))) return null;
            return new(expectedMapId, title, path);
        }
        catch (Exception ex) when (ex is InvalidDataException or OverflowException or ArgumentException)
        {
            return null;
        }
    }

    private byte[] ReadString(ReadOnlySpan<byte> header)
    {
        var capacity = BitConverter.ToUInt32(header[8..]);
        var length = BitConverter.ToUInt32(header[12..]);
        if (BitConverter.ToUInt32(header) != checked(rsfBase + 0x2a594c)
            || length is 0 or > 4096 || capacity < length || capacity > 65536) throw new InvalidDataException();
        return Bytes(BitConverter.ToUInt32(header[4..]), (int)length);
    }

    private static string Decode(byte[] data, byte[] key)
    {
        var plain = new byte[data.Length];
        for (var i = 0; i < data.Length; i++) plain[i] = (byte)(data[i] ^ key[i % key.Length]);
        return Utf8.GetString(plain);
    }

    private byte[] Bytes(uint address, int size)
    {
        if (address < 0x10000 || (ulong)address + (uint)size > 0x100000000UL) throw new InvalidDataException();
        var bytes = read(address, size);
        if (bytes == null || bytes.Length != size) throw new InvalidDataException();
        return bytes;
    }
    private uint UInt(uint address) => BitConverter.ToUInt32(Bytes(address, 4));
    private static string Hash(string path) { using var file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)); }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(IntPtr process, nint address, [Out] byte[] buffer, nuint size, out nuint count);
}
