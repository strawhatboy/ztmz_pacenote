using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using SevenZipExtractor;

// need to call harmony.PatchAll() in the main entry point
        
namespace ZTMZ.PacenoteTool.Base
{
    [HarmonyPatch(typeof(ArchiveFile), "GetPropertySafe", new Type[] { typeof(uint), typeof(uint)})]
    public static class ArchiveFilePatch
    {
        private static Type propNameId = AccessTools.TypeByName("SevenZipExtractor.ItemPropId");
        public static void Patch(Harmony harmony)
        {
            var original = AccessTools.Method(typeof(SevenZipExtractor.ArchiveFile), "GetPropertySafe", new Type[] { typeof(uint), propNameId });
            var transpiler = AccessTools.Method(typeof(ArchiveFilePatch), "Transpiler");
            harmony.Patch(original, null, null, new HarmonyMethod(transpiler));
        }
        public static bool GetPropertySafe<T>(uint fileIndex, object propNameId, ref T __result, ArchiveFile __instance)
        {
            try
            {
                MethodInfo method = __instance.GetType().GetMethod("GetProperty", AccessTools.all);
                if (method == null)
                {
                    __result = default(T);
                    return false;
                } else {
                    __result = (T)method.Invoke(__instance, new object[] { fileIndex, propNameId });
                    return false;
                }
            }
            catch (InvalidCastException)
            {
                __result = default(T);
            }
            catch (FormatException) {
                __result = default(T);
            }
            return false;
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);

            // Replace the entire method body with new behavior
            codes.Clear();
            
            // Load arguments onto the stack: _type (1st argument) and whatevervalue (2nd argument)
            codes.Add(new CodeInstruction(OpCodes.Ldarg_1));
            codes.Add(new CodeInstruction(OpCodes.Ldarg_2));

            // Your custom code: Display a custom message
            codes.Add(new CodeInstruction(OpCodes.Call, typeof(ArchiveFilePatch).GetMethod("GetPropertySafe")));

            // Return the result from the custom method
            codes.Add(new CodeInstruction(OpCodes.Ret));

            return codes;
        }
    }
}
