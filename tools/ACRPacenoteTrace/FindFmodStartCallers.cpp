#include <windows.h>

#include <algorithm>
#include <cctype>
#include <cstdint>
#include <cstring>
#include <fstream>
#include <iostream>
#include <string>
#include <vector>

namespace
{
constexpr char TargetModule[] = "fmodstudio.dll";
constexpr char TargetImport[] = "?start@EventInstance@Studio@FMOD@@QEAA?AW4FMOD_RESULT@@XZ";

struct DelayImportDescriptor
{
    DWORD Attributes;
    DWORD Name;
    DWORD ModuleHandle;
    DWORD ImportAddressTable;
    DWORD ImportNameTable;
    DWORD BoundImportAddressTable;
    DWORD UnloadImportAddressTable;
    DWORD TimeDateStamp;
};

bool EqualsInsensitive(const char* left, const char* right)
{
    while (*left && *right)
    {
        if (std::tolower(static_cast<unsigned char>(*left)) != std::tolower(static_cast<unsigned char>(*right)))
        {
            return false;
        }
        ++left;
        ++right;
    }
    return *left == '\0' && *right == '\0';
}

DWORD RvaToOffset(DWORD rva, const IMAGE_NT_HEADERS64* nt, const IMAGE_SECTION_HEADER* sections)
{
    if (rva < nt->OptionalHeader.SizeOfHeaders)
    {
        return rva;
    }

    for (WORD index = 0; index < nt->FileHeader.NumberOfSections; ++index)
    {
        const auto& section = sections[index];
        const DWORD virtualSize = section.Misc.VirtualSize > section.SizeOfRawData ? section.Misc.VirtualSize : section.SizeOfRawData;
        if (rva >= section.VirtualAddress && rva < section.VirtualAddress + virtualSize)
        {
            return section.PointerToRawData + (rva - section.VirtualAddress);
        }
    }

    return 0;
}
}

int main(int argc, char** argv)
{
    if (argc != 2)
    {
        std::cerr << "Usage: FindFmodStartCallers <acr.exe>\n";
        return 2;
    }

    std::ifstream input(argv[1], std::ios::binary | std::ios::ate);
    if (!input)
    {
        std::cerr << "Could not open input executable.\n";
        return 3;
    }

    const auto size = static_cast<size_t>(input.tellg());
    std::string image(size, '\0');
    input.seekg(0);
    input.read(image.data(), static_cast<std::streamsize>(image.size()));
    if (!input)
    {
        std::cerr << "Could not read input executable.\n";
        return 4;
    }

    const auto* bytes = reinterpret_cast<const unsigned char*>(image.data());
    const auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(bytes);
    if (dos->e_magic != IMAGE_DOS_SIGNATURE || static_cast<size_t>(dos->e_lfanew) + sizeof(IMAGE_NT_HEADERS64) > size)
    {
        std::cerr << "Not a supported 64-bit PE executable.\n";
        return 5;
    }

    const auto* nt = reinterpret_cast<const IMAGE_NT_HEADERS64*>(bytes + dos->e_lfanew);
    if (nt->Signature != IMAGE_NT_SIGNATURE || nt->OptionalHeader.Magic != IMAGE_NT_OPTIONAL_HDR64_MAGIC)
    {
        std::cerr << "Not a supported 64-bit PE executable.\n";
        return 6;
    }

    const auto* sections = IMAGE_FIRST_SECTION(nt);
    const auto importDirectory = nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT];
    const DWORD importOffset = RvaToOffset(importDirectory.VirtualAddress, nt, sections);
    if (!importOffset || importOffset >= size)
    {
        std::cerr << "Import directory not found.\n";
        return 7;
    }

    const uint64_t imageBase = nt->OptionalHeader.ImageBase;
    DWORD targetIatRva = 0;
    auto* descriptor = reinterpret_cast<const IMAGE_IMPORT_DESCRIPTOR*>(bytes + importOffset);
    for (; descriptor->Name != 0; ++descriptor)
    {
        const DWORD moduleOffset = RvaToOffset(descriptor->Name, nt, sections);
        if (!moduleOffset || moduleOffset >= size || !EqualsInsensitive(reinterpret_cast<const char*>(bytes + moduleOffset), TargetModule))
        {
            continue;
        }

        const DWORD namesRva = descriptor->OriginalFirstThunk ? descriptor->OriginalFirstThunk : descriptor->FirstThunk;
        const DWORD namesOffset = RvaToOffset(namesRva, nt, sections);
        if (!namesOffset)
        {
            continue;
        }

        const auto* thunk = reinterpret_cast<const IMAGE_THUNK_DATA64*>(bytes + namesOffset);
        for (DWORD index = 0; thunk[index].u1.AddressOfData != 0; ++index)
        {
            if (IMAGE_SNAP_BY_ORDINAL64(thunk[index].u1.Ordinal))
            {
                continue;
            }

            const DWORD nameOffset = RvaToOffset(static_cast<DWORD>(thunk[index].u1.AddressOfData), nt, sections);
            if (!nameOffset || nameOffset + sizeof(WORD) >= size)
            {
                continue;
            }

            const auto* importName = reinterpret_cast<const IMAGE_IMPORT_BY_NAME*>(bytes + nameOffset);
            if (std::string(importName->Name) == TargetImport)
            {
                targetIatRva = descriptor->FirstThunk + index * sizeof(ULONGLONG);
                break;
            }
        }
    }

    // Unreal delay-loads FMOD, so the native event API normally appears here instead.
    const auto delayDirectory = nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_DELAY_IMPORT];
    const DWORD delayOffset = RvaToOffset(delayDirectory.VirtualAddress, nt, sections);
    if (!targetIatRva && delayOffset && delayOffset < size)
    {
        const auto* delay = reinterpret_cast<const DelayImportDescriptor*>(bytes + delayOffset);
        for (; delay->Name != 0; ++delay)
        {
            const bool valuesAreRvas = (delay->Attributes & 1U) != 0;
            const auto toRva = [imageBase, valuesAreRvas](DWORD value)
            {
                return valuesAreRvas ? value : static_cast<DWORD>(static_cast<uint64_t>(value) - imageBase);
            };
            const DWORD moduleOffset = RvaToOffset(toRva(delay->Name), nt, sections);
            if (!moduleOffset || moduleOffset >= size || !EqualsInsensitive(reinterpret_cast<const char*>(bytes + moduleOffset), TargetModule))
            {
                continue;
            }

            const DWORD namesRva = toRva(delay->ImportNameTable);
            const DWORD namesOffset = RvaToOffset(namesRva, nt, sections);
            if (!namesOffset)
            {
                continue;
            }

            const auto* thunk = reinterpret_cast<const IMAGE_THUNK_DATA64*>(bytes + namesOffset);
            for (DWORD index = 0; thunk[index].u1.AddressOfData != 0; ++index)
            {
                if (IMAGE_SNAP_BY_ORDINAL64(thunk[index].u1.Ordinal))
                {
                    continue;
                }

                const DWORD nameOffset = RvaToOffset(toRva(static_cast<DWORD>(thunk[index].u1.AddressOfData)), nt, sections);
                if (!nameOffset || nameOffset + sizeof(WORD) >= size)
                {
                    continue;
                }

                const auto* importName = reinterpret_cast<const IMAGE_IMPORT_BY_NAME*>(bytes + nameOffset);
                if (std::string(importName->Name) == TargetImport)
                {
                    targetIatRva = toRva(delay->ImportAddressTable) + index * sizeof(ULONGLONG);
                    break;
                }
            }
        }
    }

    if (!targetIatRva)
    {
        std::cerr << "Target FMOD import not found.\n";
        return 8;
    }

    const uint64_t targetIatAddress = imageBase + targetIatRva;
    std::cout << "FMOD EventInstance::start IAT RVA 0x" << std::hex << targetIatRva << " VA 0x" << targetIatAddress << "\n";

    std::vector<uint64_t> importThunks;
    for (WORD sectionIndex = 0; sectionIndex < nt->FileHeader.NumberOfSections; ++sectionIndex)
    {
        const auto& section = sections[sectionIndex];
        if ((section.Characteristics & IMAGE_SCN_MEM_EXECUTE) == 0 || section.PointerToRawData >= size)
        {
            continue;
        }

        const size_t sectionEnd = (std::min)(size, static_cast<size_t>(section.PointerToRawData) + section.SizeOfRawData);
        for (size_t offset = section.PointerToRawData; offset + 6 <= sectionEnd; ++offset)
        {
            if (bytes[offset] != 0xFF || bytes[offset + 1] != 0x25)
            {
                continue;
            }

            int32_t displacement = 0;
            std::memcpy(&displacement, bytes + offset + 2, sizeof(displacement));
            const uint64_t instructionAddress = imageBase + section.VirtualAddress + (offset - section.PointerToRawData);
            const uint64_t resolvedAddress = static_cast<uint64_t>(static_cast<int64_t>(instructionAddress + 6) + displacement);
            if (resolvedAddress != targetIatAddress)
            {
                continue;
            }

            importThunks.push_back(instructionAddress);
            std::cout << "import thunk RVA 0x" << std::hex << (instructionAddress - imageBase) << " VA 0x" << instructionAddress << "\n";
        }
    }

    size_t count = 0;
    for (WORD sectionIndex = 0; sectionIndex < nt->FileHeader.NumberOfSections; ++sectionIndex)
    {
        const auto& section = sections[sectionIndex];
        if ((section.Characteristics & IMAGE_SCN_MEM_EXECUTE) == 0 || section.PointerToRawData >= size)
        {
            continue;
        }

        const size_t sectionEnd = (std::min)(size, static_cast<size_t>(section.PointerToRawData) + section.SizeOfRawData);
        for (size_t offset = section.PointerToRawData; offset + 5 <= sectionEnd; ++offset)
        {
            if (bytes[offset] != 0xE8)
            {
                continue;
            }

            int32_t displacement = 0;
            std::memcpy(&displacement, bytes + offset + 1, sizeof(displacement));
            const uint64_t instructionAddress = imageBase + section.VirtualAddress + (offset - section.PointerToRawData);
            const uint64_t resolvedAddress = static_cast<uint64_t>(static_cast<int64_t>(instructionAddress + 5) + displacement);
            if ((std::find)(importThunks.begin(), importThunks.end(), resolvedAddress) == importThunks.end())
            {
                continue;
            }

            std::cout << "call site RVA 0x" << std::hex << (instructionAddress - imageBase) << " VA 0x" << instructionAddress << " bytes";
            for (size_t byteIndex = offset > 16 ? offset - 16 : offset; byteIndex < (std::min)(sectionEnd, offset + 32); ++byteIndex)
            {
                std::cout << ' ' << static_cast<unsigned int>(bytes[byteIndex]);
            }
            std::cout << "\n";
            ++count;
        }
    }

    std::cout << std::dec << "Found " << count << " direct call sites.\n";
    return 0;
}
