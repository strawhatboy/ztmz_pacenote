
using System.Runtime.InteropServices;

namespace ZTMZ.PacenoteTool.Base;
public static class CastingHelper
{
	public static T CastToStruct<T>(this byte[] data) where T : struct
	{
		if (data == null) 
		{
			return default(T);
		}
		var pData = GCHandle.Alloc(data, GCHandleType.Pinned);
		var result = (T)Marshal.PtrToStructure(pData.AddrOfPinnedObject(), typeof(T));
		pData.Free();
		return result;
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
