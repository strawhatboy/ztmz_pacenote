#include <windows.h>
#include <array>
#include <atomic>
#include <cstdio>
#include <cstring>
#include "MinHook.h"

namespace {
HMODULE self;
using Sound=int(__fastcall*)(void*,const char*,void*);
using Start=int(__fastcall*)(void*);
using Description=int(__fastcall*)(void*,void**);
using Path=int(__fastcall*)(void*,char*,int,int*);
Sound originalSound; Start originalStart; Description description; Path path;
std::atomic<ULONGLONG> pendingStart{0};
struct Record { ULONGLONG time; char name[512]; };
std::array<Record,256> queue;
SRWLOCK mutex=SRWLOCK_INIT;
size_t head=0,tail=0;
std::atomic<unsigned> dropped{0};

int __fastcall onStart(void* event) {
    void* desc=nullptr; char name[256]{}; int len=0;
    bool relevant=description(event,&desc)==0 && desc && path(desc,name,sizeof(name),&len)==0 && strcmp(name,"event:/VOICE/Pacenotes")==0;
    if(relevant) pendingStart=GetTickCount64();
    int result=originalStart(event);
    if(relevant && result!=0) pendingStart=0;
    return result;
}
int __fastcall onSound(void* system,const char* name,void* info) {
    int result=originalSound(system,name,info);
    if(result!=0 || !name)return result;
    const char* leaf=strrchr(name,'/');leaf=leaf?leaf+1:name;
    if(strncmp(leaf,"Atom_",5)!=0 && strncmp(leaf,"Block_",6)!=0)return result;
    auto started=pendingStart.exchange(0);
    auto now=GetTickCount64();
    // WRCG's observed order is event start, followed by programmer-sound lookup.
    // Do not forward uncorrelated/preload lookups or duplicate lookups for one start.
    if(!started || now-started>2000)return result;
    if(!TryAcquireSRWLockExclusive(&mutex)){++dropped;return result;}
    auto next=(head+1)%queue.size();
    if(next==tail)++dropped;
    else {queue[head].time=now;strncpy_s(queue[head].name,name,_TRUNCATE);head=next;}
    ReleaseSRWLockExclusive(&mutex);
    return result;
}
DWORD WINAPI run(void*) {
    wchar_t exe[32768]{};GetModuleFileNameW(nullptr,exe,32768);
    auto leaf=wcsrchr(exe,L'\\');if(_wcsicmp(leaf?leaf+1:exe,L"WRCG.exe"))return 1;
    HMODULE fmod=GetModuleHandleW(L"fmodstudio.dll");if(!fmod)return 2;
    description=reinterpret_cast<Description>(GetProcAddress(fmod,"?getDescription@EventInstance@Studio@FMOD@@QEBA?AW4FMOD_RESULT@@PEAPEAVEventDescription@23@@Z"));
    path=reinterpret_cast<Path>(GetProcAddress(fmod,"?getPath@EventDescription@Studio@FMOD@@QEBA?AW4FMOD_RESULT@@PEADHPEAH@Z"));
    auto sound=GetProcAddress(fmod,"?getSoundInfo@System@Studio@FMOD@@QEBA?AW4FMOD_RESULT@@PEBDPEAUFMOD_STUDIO_SOUND_INFO@@@Z");
    auto start=GetProcAddress(fmod,"?start@EventInstance@Studio@FMOD@@QEAA?AW4FMOD_RESULT@@XZ");
    if(!description || !path || !sound || !start || MH_Initialize()!=MH_OK)return 3;
    if(MH_CreateHook(sound,&onSound,reinterpret_cast<void**>(&originalSound))!=MH_OK)return 4;
    if(MH_CreateHook(start,&onStart,reinterpret_cast<void**>(&originalStart))!=MH_OK){MH_RemoveHook(sound);return 5;}
    HMODULE pinned;
    if(!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_PIN,reinterpret_cast<LPCWSTR>(&run),&pinned))return 6;
    MH_QueueEnableHook(sound);MH_QueueEnableHook(start);
    if(MH_ApplyQueued()!=MH_OK){MH_DisableHook(sound);MH_DisableHook(start);return 7;}
    wchar_t pipe[128];swprintf_s(pipe,L"\\\\.\\pipe\\ZTMZ.WRCG.Live.v1.%lu",GetCurrentProcessId());
    HANDLE connection=INVALID_HANDLE_VALUE;
    ULONGLONG heartbeat=0;
    for(;;) {
        if(connection==INVALID_HANDLE_VALUE) {
            connection=CreateFileW(pipe,GENERIC_WRITE,0,nullptr,OPEN_EXISTING,0,nullptr);
            // Never replay accumulated calls after reconnecting/restarting ZTMZ.
            AcquireSRWLockExclusive(&mutex);tail=head;ReleaseSRWLockExclusive(&mutex);
            if(connection==INVALID_HANDLE_VALUE){Sleep(100);continue;}
            DWORD written;const char* ready="READY\n";
            if(!WriteFile(connection,ready,6,&written,nullptr) || written!=6){CloseHandle(connection);connection=INVALID_HANDLE_VALUE;continue;}
        }
        auto lost=dropped.exchange(0);
        if(lost || GetTickCount64()-heartbeat>=1000) {
            char status[64];int length=lost?sprintf_s(status,"DROPPED %u\n",lost):sprintf_s(status,"PING\n");DWORD written;
            heartbeat=GetTickCount64();
            if(!WriteFile(connection,status,length,&written,nullptr) || written!=length){CloseHandle(connection);connection=INVALID_HANDLE_VALUE;continue;}
        }
        Record record{};bool have=false;
        AcquireSRWLockExclusive(&mutex);
        if(tail!=head){record=queue[tail];tail=(tail+1)%queue.size();have=true;}
        ReleaseSRWLockExclusive(&mutex);
        if(!have){Sleep(5);continue;}
        if(GetTickCount64()-record.time>500)continue;
        char line[640];int size=sprintf_s(line,"%llu\t%s\n",record.time,record.name);DWORD written;
        // Blocking IO is exclusively on this worker, never on the game's threads.
        if(!WriteFile(connection,line,size,&written,nullptr) || written!=size){CloseHandle(connection);connection=INVALID_HANDLE_VALUE;}
    }
}
}
BOOL APIENTRY DllMain(HMODULE module,DWORD reason,LPVOID) {
    if(reason==DLL_PROCESS_ATTACH){self=module;DisableThreadLibraryCalls(module);auto thread=CreateThread(nullptr,0,run,nullptr,0,nullptr);if(thread)CloseHandle(thread);}
    return TRUE;
}
