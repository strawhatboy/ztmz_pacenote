#include <windows.h>
#include <tlhelp32.h>
#include <filesystem>
#include <iostream>
#include <stdexcept>
#ifndef WRCG_DLL_NAME
#define WRCG_DLL_NAME L"WrcGenerationsTrace.dll"
#endif
#ifndef KT_TARGET_EXE
#define KT_TARGET_EXE L"WRCG.exe"
#endif
namespace {
void check(bool ok,const char* message){if(!ok)throw std::runtime_error(std::string(message)+"; Win32="+std::to_string(GetLastError()));}
struct Handle {HANDLE h;~Handle(){if(h && h!=INVALID_HANDLE_VALUE)CloseHandle(h);}};
}
int wmain(int argc,wchar_t** argv){try{
    check(argc==2,"Usage: WrcGenerationsInject <PID>");
    size_t n=0;auto pid=std::stoul(argv[1],&n);check(pid && n==wcslen(argv[1]),"Invalid PID");
    Handle process{OpenProcess(PROCESS_CREATE_THREAD|PROCESS_QUERY_INFORMATION|PROCESS_VM_OPERATION|PROCESS_VM_WRITE|PROCESS_VM_READ,FALSE,pid)};
    check(process.h!=nullptr,"OpenProcess failed");
    wchar_t path[32768];DWORD size=32768;check(QueryFullProcessImageNameW(process.h,0,path,&size),"Cannot identify target");
    check(_wcsicmp(std::filesystem::path(path).filename().c_str(),KT_TARGET_EXE)==0,"Target executable does not match this bridge");
    USHORT machine=0,native=0;check(IsWow64Process2(process.h,&machine,&native)&&machine==IMAGE_FILE_MACHINE_UNKNOWN&&native==IMAGE_FILE_MACHINE_AMD64,"Target must be x64");
    check(GetModuleFileNameW(nullptr,path,32768)!=0,"Cannot locate probe");
    auto dll=std::filesystem::path(path).parent_path()/WRCG_DLL_NAME;
    check(std::filesystem::is_regular_file(dll),"Adjacent probe DLL missing");
    auto loader=GetProcAddress(GetModuleHandleW(L"kernel32.dll"),"LoadLibraryW");HMODULE owner=nullptr;
    check(loader && GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,reinterpret_cast<LPCWSTR>(loader),&owner),"Cannot resolve loader");
    check(GetModuleFileNameW(owner,path,32768)!=0,"Cannot locate loader module");
    auto offset=reinterpret_cast<uintptr_t>(loader)-reinterpret_cast<uintptr_t>(owner);
    Handle modules{CreateToolhelp32Snapshot(TH32CS_SNAPMODULE,pid)};check(modules.h!=INVALID_HANDLE_VALUE,"Cannot enumerate modules");
    MODULEENTRY32W entry{};entry.dwSize=sizeof(entry);uintptr_t remote=0;
    if(Module32FirstW(modules.h,&entry))do{
        if(_wcsicmp(entry.szModule,WRCG_DLL_NAME)==0){std::cout<<"Bridge module already loaded.\n";return 0;}
        if(_wcsicmp(entry.szExePath,path)==0 && offset<entry.modBaseSize)remote=reinterpret_cast<uintptr_t>(entry.modBaseAddr)+offset;
    }while(Module32NextW(modules.h,&entry));
    check(remote!=0,"Matching loader module unavailable");
    auto text=dll.wstring();size_t bytes=(text.size()+1)*sizeof(wchar_t);
    void* memory=VirtualAllocEx(process.h,nullptr,bytes,MEM_COMMIT|MEM_RESERVE,PAGE_READWRITE);check(memory!=nullptr,"Allocation failed");
    bool running=false;
    try{
        SIZE_T written=0;check(WriteProcessMemory(process.h,memory,text.c_str(),bytes,&written)&&written==bytes,"Write failed");
        Handle thread{CreateRemoteThread(process.h,nullptr,0,reinterpret_cast<LPTHREAD_START_ROUTINE>(remote),memory,0,nullptr)};check(thread.h!=nullptr,"Load thread failed");
        running=true;check(WaitForSingleObject(thread.h,10000)==WAIT_OBJECT_0,"Load timed out; argument retained until game exit");running=false;
        // Verify the full-width module address by enumeration, not a truncated thread exit code.
        Handle loaded{CreateToolhelp32Snapshot(TH32CS_SNAPMODULE,pid)};entry.dwSize=sizeof(entry);bool found=false;
        if(Module32FirstW(loaded.h,&entry))do{if(_wcsicmp(entry.szExePath,text.c_str())==0)found=true;}while(Module32NextW(loaded.h,&entry));
        check(found,"Probe DLL not loaded");
    }catch(...){if(!running)VirtualFreeEx(process.h,memory,0,MEM_RELEASE);throw;}
    VirtualFreeEx(process.h,memory,0,MEM_RELEASE);
    std::cout<<"DLL loaded. Verify bridge readiness separately. Exit game to unload.\n";
    return 0;
}catch(const std::exception& e){std::cerr<<e.what()<<'\n';return 1;}}
