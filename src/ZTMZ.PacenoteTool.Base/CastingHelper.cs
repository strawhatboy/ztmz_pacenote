
using System;
using System.Runtime.InteropServices;

namespace ZTMZ.PacenoteTool.Base;
public static class CastingHelper
{
	public static T CastToStruct<T>(this byte[] data) where T : struct
	{
		if (data == null)
		{
			return default;
		}

		var structureSize = Marshal.SizeOf<T>();
		if (data.Length < structureSize)
		{
			throw new ArgumentException(
				$"Cannot convert a {data.Length}-byte packet to {typeof(T).Name}; at least {structureSize} bytes are required.",
				nameof(data));
		}

		var pData = GCHandle.Alloc(data, GCHandleType.Pinned);
		try
		{
			return Marshal.PtrToStructure<T>(pData.AddrOfPinnedObject());
		}
		finally
		{
			pData.Free();
		}
	}

	public static byte[] CastToArray<T>(this T data) where T : struct
	{
		var result = new byte[Marshal.SizeOf(typeof(T))];
		var pResult = GCHandle.Alloc(result, GCHandleType.Pinned);
		Marshal.StructureToPtr(data, pResult.AddrOfPinnedObject(), true);
		pResult.Free();
		return result;
	}
}
