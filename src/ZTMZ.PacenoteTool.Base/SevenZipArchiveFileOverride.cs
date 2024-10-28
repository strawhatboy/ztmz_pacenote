using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using SevenZipExtractor;

// need to call Patch(Harmony harmony) in App.xaml.cs
namespace ZTMZ.PacenoteTool.Base
{
    public static class ArchiveFilePatch
    {
        private static Type PropNameId = AccessTools.TypeByName("SevenZipExtractor.ItemPropId");
        public static void Patch(Harmony harmony)
        {
            var original = AccessTools.Method(typeof(SevenZipExtractor.ArchiveFile), "GetPropertySafe", new Type[] { typeof(uint), PropNameId }).MakeGenericMethod(typeof(DateTime));
            var prefix = AccessTools.Method(typeof(ArchiveFilePatch), "GetPropertySafe");
            harmony.Patch(original, new HarmonyMethod(prefix));
        }
        public static bool GetPropertySafe(uint fileIndex, object name, ref DateTime __result, ref ArchiveFile __instance)
        {
            try
            {
                MethodInfo method = __instance.GetType().GetMethod("GetProperty", AccessTools.all).MakeGenericMethod(typeof(DateTime));
                if (method == null)
                {
                    __result = default(DateTime);
                    return false;
                } else {
                    __result = (DateTime)method.Invoke(__instance, new object[] { fileIndex, name });
                    return false;
                }
            }
            catch (Exception)
            {
                __result = default(DateTime);
            }
            return false;
        }
    }
}
