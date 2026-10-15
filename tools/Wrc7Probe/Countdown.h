#pragma once
#include "MinHook.h"

// FMOD 1.9.5 exports verified against WRC7's fmodstudio64.dll.
// The worker follows the event timeline, which stops when the event is paused.
namespace Wrc7Countdown {
using Start = int(__fastcall*)(void*);
using Description = int(__fastcall*)(void*, void**);
using Path = int(__fastcall*)(void*, char*, int, int*);
using IntValue = int(__fastcall*)(void*, int*);
inline Start original;
inline Description description;
inline Path path;
inline IntValue position, playback, paused;
inline SRWLOCK lock = SRWLOCK_INIT;
inline void* instance = nullptr;
inline unsigned generation = 0;
inline int length = 0;
inline unsigned observed = 0;
inline int lastStep = 5;

inline int __fastcall start(void* event) {
    void* desc = nullptr; char name[128]{}; int size = 0;
    int count = 0;
    if (description(event, &desc) == 0 && desc && path(desc, name, sizeof(name), &size) == 0) {
        if (strcmp(name, "event:/GUI/54321Go") == 0) count = 5;
        else if (strcmp(name, "event:/GUI/321Go") == 0) count = 3;
    }
    const int result = original(event);
    if (result == 0 && count) {
        AcquireSRWLockExclusive(&lock);
        instance = event; length = count; ++generation;
        ReleaseSRWLockExclusive(&lock);
    }
    return result;
}

inline bool install(const wchar_t* moduleName = L"fmodstudio64.dll") {
    const auto module = GetModuleHandleW(moduleName);
    if (!module) return false;
    description = reinterpret_cast<Description>(GetProcAddress(module, "FMOD_Studio_EventInstance_GetDescription"));
    path = reinterpret_cast<Path>(GetProcAddress(module, "FMOD_Studio_EventDescription_GetPath"));
    position = reinterpret_cast<IntValue>(GetProcAddress(module, "FMOD_Studio_EventInstance_GetTimelinePosition"));
    playback = reinterpret_cast<IntValue>(GetProcAddress(module, "FMOD_Studio_EventInstance_GetPlaybackState"));
    paused = reinterpret_cast<IntValue>(GetProcAddress(module, "FMOD_Studio_EventInstance_GetPaused"));
    const auto target = GetProcAddress(module, "?start@EventInstance@Studio@FMOD@@QEAA?AW4FMOD_RESULT@@XZ");
    if (!description || !path || !position || !playback || !paused || !target) return false;
    if (MH_Initialize() != MH_OK) return false;
    if (MH_CreateHook(target, &start, reinterpret_cast<void**>(&original)) != MH_OK) return false;
    if (MH_EnableHook(target) != MH_OK) { MH_RemoveHook(target); return false; }
    return true;
}

inline void reconnect() {
    AcquireSRWLockShared(&lock);
    observed = generation; lastStep = length;
    ReleaseSRWLockShared(&lock);
}

template<class Emit> void poll(Emit emit) {
    AcquireSRWLockShared(&lock);
    const auto event = instance; const auto current = generation; const int count = length;
    ReleaseSRWLockShared(&lock);
    if (current != observed) { observed = current; lastStep = -1; }
    if (!event || lastStep >= count) return;
    int state = 2, isPaused = 0, milliseconds = 0;
    if (playback(event, &state) != 0 || state == 2 || state == 4) { lastStep = count; return; }
    if (state != 0 || paused(event, &isPaused) != 0 || isPaused ||
        position(event, &milliseconds) != 0 || milliseconds < 0) return;
    const int step = milliseconds / 1000;
    if (step > count) { lastStep = count; return; }
    if (step <= lastStep) return;
    lastStep = step;
    if (milliseconds % 1000 >= 500) return; // Never burst through old numbers.
    AcquireSRWLockShared(&lock);
    const bool currentEvent = current == generation;
    ReleaseSRWLockShared(&lock);
    if (currentEvent) {
        char cue[64]; sprintf_s(cue, "ztmz:/countdown/%d", count - step);
        emit(cue, GetTickCount64());
    }
}
}
