#include <windows.h>
#include <MinHook.h>
#include <atomic>
#include <array>
#include <cstdio>
#include <cstring>
#include <cstdint>
#ifdef KT_WRC7
#define KT_TRACE_EXE L"WRC7.exe"
#define KT_FMOD_MODULE L"fmodstudio64.dll"
#else
#define KT_TRACE_EXE L"WRCG.exe"
#define KT_FMOD_MODULE L"fmodstudio.dll"
#endif

namespace {
// Bounded, non-waiting producer queue: never write files on the game's audio thread.
struct Record { char text[1024]; };
std::array<Record,4096> records{};
SRWLOCK lock=SRWLOCK_INIT;
size_t head=0,tail=0;
std::atomic<unsigned> dropped{0};
HMODULE module;
using Sound=int(__fastcall*)(void*,const char*,void*);
using Start=int(__fastcall*)(void*);
using Parameter=int(__fastcall*)(void*,const char*,float,bool);
struct ParameterId {unsigned first,second;};
using ParameterById=int(__fastcall*)(void*,ParameterId,float,bool);
using Description=int(__fastcall*)(void*,void**);
using Path=int(__fastcall*)(void*,char*,int,int*);
Sound soundOriginal; Start startOriginal; Parameter parameterOriginal;
ParameterById parameterIdOriginal; Description getDescription; Path getPath;

void enqueue(const char* kind,const char* name,int result,void* object=nullptr,float value=0) noexcept {
    if(!TryAcquireSRWLockExclusive(&lock)){++dropped;return;}
    auto next=(head+1)%records.size();
    if(next==tail) ++dropped;
    else {
        _snprintf_s(records[head].text,sizeof(records[head].text),_TRUNCATE,
            "%llu\t%lu\t%s\t%p\t%d\t%.9g\t%.800s\r\n",
            GetTickCount64(),GetCurrentThreadId(),kind,object,result,value,name?name:"<null>");
        head=next;
    }
    ReleaseSRWLockExclusive(&lock);
}
bool pacenote(void* instance) noexcept {
    void* description=nullptr;char path[512]{};int length=0;
    return getDescription(instance,&description)==0 && description &&
        getPath(description,path,sizeof(path),&length)==0 &&
        (_stricmp(path,"event:/VOICE/Pacenotes")==0);
}
int __fastcall sound(void* system,const char* name,void* info) {
    int result=soundOriginal(system,name,info);
    enqueue("sound-info",name,result,system);return result;
}
int __fastcall start(void* instance) {
#ifdef KT_WRC7
    void* description=nullptr;char name[512]{};int length=0;
    bool resolved=getDescription(instance,&description)==0 && description && getPath(description,name,sizeof(name),&length)==0;
    // Capture order around start as WRC7 may perform the programmer lookup synchronously.
    bool relevant=true; // Discover actual WRC7 paths, including banks with stripped path metadata.
    if(!resolved)strcpy_s(name,"<event path unavailable>");
    if(relevant)enqueue("start-enter",name,0,instance);
    int result=startOriginal(instance);
    if(relevant)enqueue("start-return",name,result,instance);
    return result;
#else
    bool relevant=pacenote(instance);int result=startOriginal(instance);
    if(relevant)enqueue("pacenote-start","event:/VOICE/Pacenotes",result,instance);
    return result;
#endif
}
int __fastcall parameter(void* instance,const char* name,float value,bool ignore) {
    bool relevant=pacenote(instance);int result=parameterOriginal(instance,name,value,ignore);
    if(relevant)enqueue("pacenote-parameter",name,result,instance,value);
    return result;
}
int __fastcall parameterId(void* instance,ParameterId id,float value,bool ignore) {
    bool relevant=pacenote(instance);int result=parameterIdOriginal(instance,id,value,ignore);
    if(relevant){char name[64];sprintf_s(name,"%08x:%08x",id.first,id.second);enqueue("pacenote-parameter-id",name,result,instance,value);}
    return result;
}
DWORD WINAPI run(void*) {
    wchar_t exe[32768]{};GetModuleFileNameW(nullptr,exe,32768);
    auto leaf=wcsrchr(exe,L'\\');if(_wcsicmp(leaf?leaf+1:exe,KT_TRACE_EXE)!=0)return 1;
    wchar_t log[32768]{};GetModuleFileNameW(module,log,32768);
    auto end=wcsrchr(log,L'.');if(!end)return 2;wcscpy_s(end,static_cast<size_t>(log+32768-end),L".log");
    HANDLE file=CreateFileW(log,FILE_APPEND_DATA,FILE_SHARE_READ|FILE_SHARE_WRITE,nullptr,OPEN_ALWAYS,FILE_ATTRIBUTE_NORMAL,nullptr);
    if(file==INVALID_HANDLE_VALUE)return 3;
    auto write=[&](const char* text){DWORD n=0;WriteFile(file,text,static_cast<DWORD>(strlen(text)),&n,nullptr);};
    write("KT FMOD probe: tick-ms, thread, kind, instance, result, value, name. Original audio unchanged.\r\n");
    HMODULE fmod=nullptr;
    for(int i=0;i<600 && !(fmod=GetModuleHandleW(KT_FMOD_MODULE));++i)Sleep(100);
    if(!fmod){write("ERROR: fmodstudio.dll absent after 60 seconds\r\n");CloseHandle(file);return 4;}
    getDescription=reinterpret_cast<Description>(GetProcAddress(fmod,"?getDescription@EventInstance@Studio@FMOD@@QEBA?AW4FMOD_RESULT@@PEAPEAVEventDescription@23@@Z"));
    getPath=reinterpret_cast<Path>(GetProcAddress(fmod,"?getPath@EventDescription@Studio@FMOD@@QEBA?AW4FMOD_RESULT@@PEADHPEAH@Z"));
    const char* names[]{
        "?getSoundInfo@System@Studio@FMOD@@QEBA?AW4FMOD_RESULT@@PEBDPEAUFMOD_STUDIO_SOUND_INFO@@@Z",
        "?start@EventInstance@Studio@FMOD@@QEAA?AW4FMOD_RESULT@@XZ",
        "?setParameterByName@EventInstance@Studio@FMOD@@QEAA?AW4FMOD_RESULT@@PEBDM_N@Z",
        "?setParameterByID@EventInstance@Studio@FMOD@@QEAA?AW4FMOD_RESULT@@UFMOD_STUDIO_PARAMETER_ID@@M_N@Z"};
    void* callbacks[]{reinterpret_cast<void*>(&sound),reinterpret_cast<void*>(&start),reinterpret_cast<void*>(&parameter),reinterpret_cast<void*>(&parameterId)};
    void** originals[]{reinterpret_cast<void**>(&soundOriginal),reinterpret_cast<void**>(&startOriginal),reinterpret_cast<void**>(&parameterOriginal),reinterpret_cast<void**>(&parameterIdOriginal)};
    void* targets[4]{};int created=0;
    bool ok=getDescription && getPath && MH_Initialize()==MH_OK;
#ifdef KT_WRC7
    constexpr int hookCount=2; // FMOD 1.x has different parameter APIs; never use FMOD 2.x signatures.
#else
    constexpr int hookCount=4;
#endif
    for(int i=0;ok && i<hookCount;++i){targets[i]=reinterpret_cast<void*>(GetProcAddress(fmod,names[i]));ok=targets[i]&&MH_CreateHook(targets[i],callbacks[i],originals[i])==MH_OK;if(ok)++created;}
    // Keep hook code alive until game exit; no DLL unload while callbacks can run.
    HMODULE pinned=nullptr;
    if(ok)ok=GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_PIN,reinterpret_cast<LPCWSTR>(&run),&pinned)!=FALSE;
    for(int i=0;ok && i<created;++i)ok=MH_QueueEnableHook(targets[i])==MH_OK;
    if(ok)ok=MH_ApplyQueued()==MH_OK;
    if(!ok){for(int i=0;i<created;++i){MH_QueueDisableHook(targets[i]);MH_DisableHook(targets[i]);}write("ERROR: required export or hook setup failed\r\n");CloseHandle(file);return 5;}
    write("READY: FMOD C++ hooks installed; enter a stage and drive through several calls.\r\n");
    for(;;){
        Record batch[64];size_t count=0;
        AcquireSRWLockExclusive(&lock);
        while(tail!=head && count<64){batch[count++]=records[tail];tail=(tail+1)%records.size();}
        ReleaseSRWLockExclusive(&lock);
        for(size_t i=0;i<count;++i)write(batch[i].text);
        auto lost=dropped.exchange(0);if(lost){char text[80];sprintf_s(text,"DROPPED: %u (queue contention/full)\r\n",lost);write(text);}
        Sleep(50);
    }
}
}
BOOL WINAPI DllMain(HMODULE current,DWORD reason,LPVOID) {
    if(reason==DLL_PROCESS_ATTACH){module=current;DisableThreadLibraryCalls(current);auto worker=CreateThread(nullptr,0,run,nullptr,0,nullptr);if(worker)CloseHandle(worker);}
    return TRUE;
}
