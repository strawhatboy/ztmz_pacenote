using System;
using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;

namespace ZTMZ.PacenoteTool.AC;

internal static class ACRBridgeCompatibility
{
    internal const string RequiredExport = "?getSoundInfo@System@Studio@FMOD@@QEBA?AW4FMOD_RESULT@@PEBDPEAUFMOD_STUDIO_SOUND_INFO@@@Z";

    // Read the PE export table without loading/executing the game DLL. Works on Proton hosts too.
    internal static bool HasRequiredExport(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            if (pe.PEHeaders.CoffHeader.Machine != Machine.Amd64 || pe.PEHeaders.PEHeader is not { } header)
                return false;
            var directory = header.ExportTableDirectory;
            if (directory.RelativeVirtualAddress == 0 || directory.Size < 40) return false;
            var exports = pe.GetSectionData(directory.RelativeVirtualAddress).GetReader();
            exports.Offset = 20;
            var functionCount = exports.ReadUInt32();
            var nameCount = exports.ReadUInt32();
            var functionsRva = exports.ReadInt32();
            var namesRva = exports.ReadInt32();
            var ordinalsRva = exports.ReadInt32();
            if (nameCount > 100000 || functionCount > 100000) return false;
            var names = pe.GetSectionData(namesRva).GetReader();
            var ordinals = pe.GetSectionData(ordinalsRva).GetReader();
            for (uint i = 0; i < nameCount; i++)
            {
                var name = pe.GetSectionData(names.ReadInt32()).GetReader();
                var ordinal = ordinals.ReadUInt16();
                if (name.RemainingBytes < RequiredExport.Length + 1) continue;
                if (name.ReadUTF8(RequiredExport.Length) != RequiredExport || name.ReadByte() != 0) continue;
                if (ordinal >= functionCount) return false;
                var functions = pe.GetSectionData(functionsRva).GetReader();
                functions.Offset = ordinal * 4;
                var target = functions.ReadInt32();
                // Reject forwarded exports; the hook needs executable code in this DLL.
                if (target <= 0 || ((long)target >= directory.RelativeVirtualAddress &&
                    (long)target < (long)directory.RelativeVirtualAddress + directory.Size)) return false;
                return pe.PEHeaders.SectionHeaders.Any(section =>
                    target >= section.VirtualAddress &&
                    (long)target < (long)section.VirtualAddress + section.VirtualSize &&
                    (section.SectionCharacteristics & SectionCharacteristics.MemExecute) != 0);
            }
            return false;
        }
        catch (Exception) { return false; }
    }
}
