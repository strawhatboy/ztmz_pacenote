// Read installed banks in a separate, silent FMOD system. Never inject into the game.
#include <windows.h>
#include <cstdio>
#include <string>
#include <stdexcept>

using One = int(*)(void*);
using Out = int(*)(void*, void**);
using IntOut = int(*)(void*, int*);
IntOut timeline;
using Name = int(*)(void*, char*, int);
Name soundName;
int callback(unsigned type, void* event, void* data) {
    int position = -1; timeline(event, &position);
    if (type == 0x800) {
        struct Marker { const char* name; int position; };
        auto m = static_cast<Marker*>(data);
        printf("marker position=%d name=%s\n", m->position, m->name);
    } else if (type == 0x2000) {
        char name[512]{}; soundName(data, name, sizeof(name));
        printf("sound position=%d name=%s\n", position, name);
    } else printf("callback=%x position=%d\n", type, position);
    return 0;
}
template<class T> T load(HMODULE module, const char* name) {
    auto p = GetProcAddress(module, name);
    if (!p) throw std::runtime_error(name);
    return reinterpret_cast<T>(p);
}
void check(int result) { if (result) throw std::runtime_error("FMOD result " + std::to_string(result)); }
int main(int argc, char** argv) {
    if (argc != 2) return 1;
    try {
        std::string root = argv[1];
        auto core = LoadLibraryExA((root+"/fmod.dll").c_str(), nullptr, LOAD_WITH_ALTERED_SEARCH_PATH);
        auto studio = LoadLibraryExA((root+"/fmodstudio.dll").c_str(), nullptr, LOAD_WITH_ALTERED_SEARCH_PATH);
        void* system{}; void* low{};
        check(load<int(*)(void**,unsigned)>(studio,"FMOD_Studio_System_Create")(&system,0x00020206));
        check(load<Out>(studio,"FMOD_Studio_System_GetCoreSystem")(system,&low));
        check(load<int(*)(void*,int)>(core,"FMOD_System_SetOutput")(low,2)); // NOSOUND
        check(load<int(*)(void*,int,unsigned,unsigned,void*)>(studio,"FMOD_Studio_System_Initialize")(system,64,6,0,nullptr)); // ALLOW_MISSING_PLUGINS | SYNCHRONOUS_UPDATE
        for (auto bank : {"Common.bank", "Common.strings.bank", "GUI.bank"}) {
            void* handle{};
            check(load<int(*)(void*,const char*,unsigned,void**)>(studio,"FMOD_Studio_System_LoadBankFile")(
                system,(root+"/WIN32/Sounds/"+bank).c_str(),0,&handle));
        }
        timeline=load<IntOut>(studio,"FMOD_Studio_EventInstance_GetTimelinePosition");
        soundName=load<Name>(core,"FMOD_Sound_GetName");
        for (auto name : {"event:/GUI/54321Go", "event:/GUI/321Go"}) {
            void* desc{}; void* event{}; int length{};
            check(load<int(*)(void*,const char*,void**)>(studio,"FMOD_Studio_System_GetEvent")(system,name,&desc));
            check(load<IntOut>(studio,"FMOD_Studio_EventDescription_GetLength")(desc,&length));
            printf("event=%s length=%d\n",name,length);
            check(load<Out>(studio,"FMOD_Studio_EventDescription_CreateInstance")(desc,&event));
            check(load<int(*)(void*,decltype(&callback),unsigned)>(studio,"FMOD_Studio_EventInstance_SetCallback")(event,callback,0x2828));
            check(load<One>(studio,"FMOD_Studio_EventInstance_Start")(event));
            auto end=GetTickCount64()+length+1000;
            while(GetTickCount64()<end) { check(load<One>(studio,"FMOD_Studio_System_Update")(system)); Sleep(5); }
            check(load<One>(studio,"FMOD_Studio_EventInstance_Release")(event));
        }
        check(load<One>(studio,"FMOD_Studio_System_Release")(system));
    } catch (const std::exception& error) { fprintf(stderr,"%s\n",error.what());return 2; }
}
