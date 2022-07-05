
using System;
using System.Linq;
using System.Runtime.ConstrainedExecution;
using System.Runtime.InteropServices;
using System.Security;

namespace ZTMZ.PacenoteTool.Base;

public static class BytesReaderEx
{
    public static T Read<T>(this byte[] br)
    {
        return BytesReader<T>.Read(br);
    }
}

public static class BytesReader<T>
{
    public static readonly Func<byte[], T> Read;

    static BytesReader()
    {
        Type type = typeof(T);

        if (type == typeof(bool))
        {
            Read = (Func<byte[], T>)(Delegate)((Func<byte[], bool>)(p => BitConverter.ToBoolean(p)));
        }
        else if (type == typeof(byte))
        {
            Read = (Func<byte[], T>)(Delegate)((Func<byte[], byte>)(p => p[0]));
        }
        else if (type == typeof(char))
        {
            Read = (Func<byte[], T>)(Delegate)((Func<byte[], char>)(p => BitConverter.ToChar(p)));
        }
        else if (type == typeof(string))
        {
            Read = (Func<byte[], T>)(Delegate)((Func<byte[], string>)(p => BitConverter.ToString(p)));
        }
        else if (type == typeof(short))
        {
            Read = (Func<byte[], T>)(Delegate)((Func<byte[], short>)(p => BitConverter.ToInt16(p)));
        }
        else if (type == typeof(int))
        {
            Read = (Func<byte[], T>)(Delegate)((Func<byte[], int>)(p => BitConverter.ToInt32(p)));
        }
        else if (type == typeof(long))
        {
            Read = (Func<byte[], T>)(Delegate)((Func<byte[], long>)(p => BitConverter.ToInt64(p)));
        }
        else if (type == typeof(ushort))
        {
            Read = (Func<byte[], T>)(Delegate)((Func<byte[], ushort>)(p => BitConverter.ToUInt16(p)));
        }
        else if (type == typeof(uint))
        {
            Read = (Func<byte[], T>)(Delegate)((Func<byte[], uint>)(p => BitConverter.ToUInt32(p)));
        }
        else if (type == typeof(ulong))
        {
            Read = (Func<byte[], T>)(Delegate)((Func<byte[], ulong>)(p => BitConverter.ToUInt64(p)));
        }
        else if (type == typeof(float))
        {
            Read = (Func<byte[], T>)(Delegate)((Func<byte[], float>)(p => BitConverter.ToSingle(p)));
        }
        else if (type == typeof(double))
        {
            Read = (Func<byte[], T>)(Delegate)((Func<byte[], double>)(p => BitConverter.ToDouble(p)));
        }
        else
        {
            throw new ArgumentException();
        }
    }
}

public class MemoryReader
{
    [DllImport("kernel32.dll")]
	public static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
	[SuppressUnmanagedCodeSecurity]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll")]
	public static extern bool ReadProcessMemory(int hProcess, int lpBaseAddress, byte[] lpBuffer, int dwSize, ref int lpNumberOfBytesRead);

    public static T Read<T>(IntPtr handle, int baseAddr, params int[] offsets)
	{
		int offset = Read<int>(handle, baseAddr);
		for (int i = 0; i < offsets.Length - 1; i++)
		{
			offset = Read<int>(handle, offset + offsets[i]);
		}
		return Read<T>(handle, offset + offsets.Last());
	}

	public static T Read<T>(IntPtr handle, int adress)
	{
		byte[] array = new byte[Marshal.SizeOf(typeof(T))];
		int lpNumberOfBytesRead = 0;
		ReadProcessMemory((int)handle, adress, array, array.Length, ref lpNumberOfBytesRead);
		return BytesReader<T>.Read(array);
	}
}
