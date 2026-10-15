#include <windows.h>
#include "../Wrc7Probe/FixedEntryHook.h"
#include <array>
#include <atomic>
#include <cstdio>
#include <cstring>
namespace {
using Play = unsigned long(__fastcall*)(void*,void*,void*,void*,bool,bool,unsigned long);
Play original;
// WRC9 Steam build 6335125, FModPlaySound export RVA 0xadcd30.
// Whole position-independent instructions: mov rax,rsp; push rbp; push rdi;
// lea rbp,[rax-0xa8]; sub rsp,0x198. Never include the following RIP-relative load.
constexpr unsigned char entry[] = {0x48,0x8b,0xc4,0x55,0x57,0x48,0x8d,0xa8,0x58,0xff,0xff,0xff,0x48,0x81,0xec,0x98,0x01,0,0};
struct Record {char text[768];};
std::array<Record,512> records{};
SRWLOCK mutex=SRWLOCK_INIT;
size_t head=0,tail=0;
std::atomic<unsigned> dropped{0};
bool nameOf(void* sound,char (&name)[512]) noexcept {
    __try {
        if(!sound)return false;
        auto bytes=static_cast<const unsigned char*>(sound);
        auto length=*reinterpret_cast<const size_t*>(bytes+16);
        auto capacity=*reinterpret_cast<const size_t*>(bytes+24);
        if(!length || length>=sizeof(name) || capacity<length)return false;
        auto source=capacity<16?reinterpret_cast<const char*>(bytes):*reinterpret_cast<const char* const*>(bytes);
        memcpy(name,source,length);name[length]=0;return true;
    } __except(EXCEPTION_EXECUTE_HANDLER){return false;}
}
unsigned long __fastcall play(void* driver,void* sound,void* position,void* description,bool flag1,bool paused,unsigned long flags) {
    char name[512]{};bool named=nameOf(sound,name);auto tick=GetTickCount64();
    auto result=original(driver,sound,position,description,flag1,paused,flags);
    if(!TryAcquireSRWLockExclusive(&mutex)){++dropped;return result;}
    auto next=(head+1)%records.size();
    if(next==tail)++dropped;
    else {_snprintf_s(records[head].text,sizeof(records[head].text),_TRUNCATE,
        "%llu\tresult=%lu\tflag1=%u\tpaused=%u\tflags=%lu\t%s\r\n",tick,result,flag1,paused,flags,named?name:"<unresolved>");head=next;}
    ReleaseSRWLockExclusive(&mutex);return result;
}
DWORD WINAPI run(void* module) {
    wchar_t path[32768]{};GetModuleFileNameW(nullptr,path,32768);
    auto leaf=wcsrchr(path,L'\\');if(_wcsicmp(leaf?leaf+1:path,L"WRC9.exe"))return 1;
    GetModuleFileNameW(static_cast<HMODULE>(module),path,32768);
    auto end=wcsrchr(path,L'.');if(!end)return 2;
    wcscpy_s(end,path+32768-end,L".log");
    HANDLE file=CreateFileW(path,FILE_APPEND_DATA,FILE_SHARE_READ|FILE_SHARE_WRITE,nullptr,OPEN_ALWAYS,FILE_ATTRIBUTE_NORMAL,nullptr);
    if(file==INVALID_HANDLE_VALUE)return 3;
    auto write=[&](const char* text){DWORD n;WriteFile(file,text,static_cast<DWORD>(strlen(text)),&n,nullptr);};
    auto target=reinterpret_cast<unsigned char*>(GetModuleHandleW(nullptr))+0xadcd30;
    if(!Wrc7FixedEntry::installVerifiedEntry(target,reinterpret_cast<void*>(&play),reinterpret_cast<void**>(&original),static_cast<HMODULE>(module),entry)) {
        char message[128];sprintf_s(message,"ERROR: verified entry hook failed: %lu\r\n",GetLastError());write(message);CloseHandle(file);return 4;
    }
    write("READY: WRC9 sound driver observer. Original audio unchanged.\r\n");
    for(;;){Record batch[32];size_t count=0;AcquireSRWLockExclusive(&mutex);
        while(tail!=head&&count<32){batch[count++]=records[tail];tail=(tail+1)%records.size();}
        ReleaseSRWLockExclusive(&mutex);
        for(size_t i=0;i<count;++i)write(batch[i].text);
        auto lost=dropped.exchange(0);if(lost){char text[64];sprintf_s(text,"DROPPED: %u\r\n",lost);write(text);}Sleep(25);
    }
}
}
BOOL WINAPI DllMain(HMODULE module,DWORD reason,LPVOID) {
    if(reason==DLL_PROCESS_ATTACH){DisableThreadLibraryCalls(module);auto worker=CreateThread(nullptr,0,run,module,0,nullptr);if(worker)CloseHandle(worker);}return TRUE;
}
