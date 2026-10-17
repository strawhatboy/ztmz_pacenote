#include "hooks/rbr_dx9_device.hpp"
#include <windows.h>
#include <bcrypt.h>
#include <filesystem>
#include <fstream>
#include <vector>
#include <stdexcept>

namespace ztmz {
namespace {
bool verifiedExecutable(const wchar_t* path) {
    // The installed SSE executable verified with the SDK chain and crash dump.
    constexpr std::array<unsigned char,32> expected{
        0x5b,0xb1,0xb0,0xc8,0xf6,0x71,0x9f,0xae,0xca,0xd6,0x3b,0x0a,0xaf,0xd7,0x54,0x10,
        0x89,0xd1,0xd1,0xff,0x7a,0x4c,0x9b,0xb6,0x8e,0xd4,0x9b,0xb5,0x3d,0xc6,0xf6,0x3b};
    std::ifstream file(std::filesystem::path(path),std::ios::binary|std::ios::ate);
    if(!file) return false;
    auto size=file.tellg();if(size<=0 || size>64*1024*1024) return false;
    std::vector<unsigned char> bytes(static_cast<size_t>(size));file.seekg(0);
    if(!file.read(reinterpret_cast<char*>(bytes.data()),static_cast<std::streamsize>(bytes.size()))) return false;
    std::array<unsigned char,32> hash{};
    return BCryptHash(BCRYPT_SHA256_ALG_HANDLE,nullptr,0,bytes.data(),static_cast<ULONG>(bytes.size()),hash.data(),static_cast<ULONG>(hash.size()))>=0
        && hash==expected;
}
bool executableAddress(uintptr_t address) {
    MEMORY_BASIC_INFORMATION region{};
    if(!VirtualQuery(reinterpret_cast<void*>(address),&region,sizeof(region)) || region.State!=MEM_COMMIT
        || (region.Protect&(PAGE_GUARD|PAGE_NOACCESS))) return false;
    return (region.Protect&(PAGE_EXECUTE|PAGE_EXECUTE_READ|PAGE_EXECUTE_READWRITE|PAGE_EXECUTE_WRITECOPY))!=0;
}
}
std::optional<Dx9DeviceTargets> existingRbrDx9Targets() {
    wchar_t path[32768]{};auto length=GetModuleFileNameW(nullptr,path,32768);
    if(!length || length>=32768) throw std::runtime_error("Cannot identify host for safe DX9 discovery");
    auto name=std::filesystem::path(path).filename().wstring();
    bool rbr=_wcsicmp(name.c_str(),L"RichardBurnsRally_SSE.exe")==0 || _wcsicmp(name.c_str(),L"RichardBurnsRally.exe")==0;
    if(!rbr) {
        if(GetModuleHandleW(L"GaugerPlugin.dll")) throw std::runtime_error("DX9 probe blocked: GaugerPlugin requires an existing supported RBR device");
        return {};
    }
    if(sizeof(void*)!=4 || reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr))!=0x400000 || !verifiedExecutable(path))
        throw std::runtime_error("RBR executable layout is unsupported; DX9 device probing was skipped for safety");
    auto targets=readRbrDx9Targets([](uintptr_t address,void* output,size_t size) {
        SIZE_T count=0;
        return ReadProcessMemory(GetCurrentProcess(),reinterpret_cast<const void*>(address),output,size,&count) && count==size;
    });
    if(!targets || !executableAddress(targets->reset) || !executableAddress(targets->present))
        throw std::runtime_error("RBR's existing DX9 device is not ready; no probe device was created");
    return targets;
}
}
