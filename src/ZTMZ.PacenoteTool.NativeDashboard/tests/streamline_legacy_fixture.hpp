#pragma once
#include <windows.h>
#include <d3d12.h>
#include <filesystem>
#include <stdexcept>
#include <vector>

// Optional local reproduction with the game's Streamline 1.x installation.
// No NVIDIA binaries are downloaded, copied, or shipped with the tests.
// Legacy ABI: NVIDIA-RTX/Streamline, v1.1.1 include/sl.h (Preferences/slInit).
inline auto legacyStreamlineDeviceFactory(const wchar_t* dllPath) {
    DWORD ignored=0;
    auto size=GetFileVersionInfoSizeW(dllPath,&ignored);
    std::vector<BYTE> version(size);
    VS_FIXEDFILEINFO* info=nullptr;UINT infoSize=0;
    if(!size||!GetFileVersionInfoW(dllPath,0,size,version.data())||
        !VerQueryValueW(version.data(),L"\\",reinterpret_cast<void**>(&info),&infoSize)||
        infoSize<sizeof(*info)||HIWORD(info->dwFileVersionMS)!=1)
        throw std::runtime_error("This optional fixture requires a Streamline 1.x DLL (legacy ABI)");
    using Factory=HRESULT(WINAPI*)(IUnknown*,D3D_FEATURE_LEVEL,REFIID,void**);
    struct Preferences {
        bool console=false; int logLevel=0;
        const wchar_t** paths=nullptr; unsigned pathCount=0;
        const wchar_t* logPath=nullptr;
        void* allocate=nullptr; void* release=nullptr; void* log=nullptr;
        unsigned long long flags=0;
        const unsigned* features=nullptr; unsigned featureCount=0;
        void* extension=nullptr;
    } prefs;
    const auto directory=std::filesystem::path(dllPath).parent_path().wstring();
    const wchar_t* path=directory.c_str();
    prefs.paths=&path; prefs.pathCount=1;
    auto module=LoadLibraryExW(dllPath,nullptr,LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR|LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
    if(!module) throw std::runtime_error("Cannot load local Streamline fixture");
    auto init=reinterpret_cast<bool(*)(const Preferences&,int)>(GetProcAddress(module,"slInit"));
    auto factory=reinterpret_cast<Factory>(GetProcAddress(module,"D3D12CreateDevice"));
    if(!init||!factory||!init(prefs,0)) throw std::runtime_error("Legacy Streamline fixture initialization failed");
    // Keep the module alive while its process-wide hooks and COM objects exist.
    return factory;
}
