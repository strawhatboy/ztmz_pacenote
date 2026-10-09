// Same-bitness loader helper. The desktop host remains x64; only this small
// executable resolves the WOW64 system loader and loads our adjacent x86 DLL.
#include <windows.h>
#include <tlhelp32.h>
#include <filesystem>
#include <iostream>
#include <stdexcept>
#include <string>
#include <cwchar>

namespace {
struct Handle {
    HANDLE value;
    ~Handle(){if(value && value!=INVALID_HANDLE_VALUE) CloseHandle(value);}
};
void require(bool ok,const char* message) {
    if(!ok) throw std::runtime_error(std::string(message)+" (Win32 "+std::to_string(GetLastError())+")");
}
std::wstring modulePath(HMODULE module) {
    std::wstring path(32768,L'\0');
    auto size=GetModuleFileNameW(module,path.data(),static_cast<DWORD>(path.size()));
    require(size && size<path.size(),"Cannot resolve module path");path.resize(size);return path;
}
}
int wmain(int argc,wchar_t** argv) {
    try {
        require(argc==3,"Expected PID and process creation FILETIME");
        size_t consumed=0;auto pid=std::stoul(argv[1],&consumed);
        require(consumed==wcslen(argv[1]) && pid!=0,"Invalid PID");
        auto created=std::stoull(argv[2],&consumed);require(consumed==wcslen(argv[2]),"Invalid process creation time");
        Handle process{OpenProcess(PROCESS_CREATE_THREAD|PROCESS_QUERY_INFORMATION|PROCESS_VM_OPERATION|PROCESS_VM_WRITE|PROCESS_VM_READ,FALSE,pid)};
        require(process.value!=nullptr,"Cannot open target process");
        USHORT machine=0,native=0;
        require(IsWow64Process2(process.value,&machine,&native) && machine==IMAGE_FILE_MACHINE_I386 && native==IMAGE_FILE_MACHINE_AMD64,"Target must be x86 on x64 Windows");
        FILETIME birth{},exit{},kernel{},user{};
        require(GetProcessTimes(process.value,&birth,&exit,&kernel,&user),"Cannot read process creation time");
        require(((static_cast<unsigned long long>(birth.dwHighDateTime)<<32)|birth.dwLowDateTime)==created,"Target process incarnation changed");
        auto dll=std::filesystem::path(modulePath(nullptr)).parent_path()/L"ztmz_dashboard_overlay.dll";
        require(std::filesystem::is_regular_file(dll),"Adjacent native dashboard DLL is missing");
        auto loader=GetProcAddress(GetModuleHandleW(L"kernel32.dll"),"LoadLibraryW");HMODULE owner=nullptr;
        require(loader && GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,reinterpret_cast<LPCWSTR>(loader),&owner),"Cannot resolve loader owner");
        auto ownerPath=modulePath(owner);auto offset=reinterpret_cast<uintptr_t>(loader)-reinterpret_cast<uintptr_t>(owner);
        Handle modules{CreateToolhelp32Snapshot(TH32CS_SNAPMODULE,pid)};
        require(modules.value!=INVALID_HANDLE_VALUE,"Cannot enumerate target modules");
        MODULEENTRY32W entry{};entry.dwSize=sizeof(entry);uintptr_t remoteLoader=0;
        if(Module32FirstW(modules.value,&entry)) do {
            if(_wcsicmp(entry.szExePath,ownerPath.c_str())==0 && offset<entry.modBaseSize) {
                remoteLoader=reinterpret_cast<uintptr_t>(entry.modBaseAddr)+offset;break;
            }
        } while(Module32NextW(modules.value,&entry));
        require(remoteLoader!=0,"Matching x86 system loader is absent");
        auto path=dll.wstring();auto size=(path.size()+1)*sizeof(wchar_t);
        void* memory=VirtualAllocEx(process.value,nullptr,size,MEM_COMMIT|MEM_RESERVE,PAGE_READWRITE);
        require(memory!=nullptr,"Cannot allocate loader argument");
        bool mayBeRunning=false;
        try {
            SIZE_T written=0;require(WriteProcessMemory(process.value,memory,path.c_str(),size,&written) && written==size,"Cannot write loader argument");
            Handle thread{CreateRemoteThread(process.value,nullptr,0,reinterpret_cast<LPTHREAD_START_ROUTINE>(remoteLoader),memory,0,nullptr)};
            require(thread.value!=nullptr,"Cannot start loader");mayBeRunning=true;
            auto wait=WaitForSingleObject(thread.value,10000);
            require(wait==WAIT_OBJECT_0,"Loader timed out; exit the game before retrying");mayBeRunning=false;
            DWORD loaded=0;require(GetExitCodeThread(thread.value,&loaded) && loaded!=0,"Windows could not load the x86 DLL");
        } catch(...) {
            // A timed-out loader may still read its argument. Never terminate it
            // or free that memory until it has completed.
            if(!mayBeRunning) VirtualFreeEx(process.value,memory,0,MEM_RELEASE);
            throw;
        }
        VirtualFreeEx(process.value,memory,0,MEM_RELEASE);
        return 0;
    } catch(const std::exception& error) {std::cerr<<error.what()<<'\n';return 1;}
}
