#include <windows.h>
#include <MinHook.h>
#include <array>
#include <atomic>
#include <cstdio>
#include <cstring>

namespace {
HMODULE self;
using Play = int(__fastcall*)(void*, void*, void*, bool, void**);
using Name = int(__fastcall*)(void*, char*, int);
Play originalPlay;
Name getName;
#ifdef KT_ENGINE_TRACE
using EnginePlay = unsigned long(__fastcall*)(void*, void*, void*, void*, bool, bool, unsigned long);
EnginePlay originalEnginePlay;
#endif
struct Record { char text[640]; };
std::array<Record, 512> queue{};
SRWLOCK mutex = SRWLOCK_INIT;
size_t head = 0, tail = 0;
std::atomic<unsigned> dropped{0};

#ifdef KT_ENGINE_TRACE
// BaseSound remains alive during its driver's call. Snapshot on the caller;
// never dereference a potentially expired game pointer in the logging worker.
void describe(void* sound, char* text, size_t size) noexcept {
    __try {
        auto words = static_cast<const unsigned long long*>(sound);
        _snprintf_s(text, size, _TRUNCATE, "object=%p first=%016llx,%016llx,%016llx,%016llx type=%u",
            sound, words[0], words[1], words[2], words[3], *reinterpret_cast<const unsigned*>(words + 4));
    } __except(EXCEPTION_EXECUTE_HANDLER) { strcpy_s(text, size, "unreadable BaseSound"); }
}
unsigned long __fastcall enginePlay(void* driver, void* sound, void* position, void* description,
    bool flag1, bool flag2, unsigned long flags) {
    char detail[512]{};
    describe(sound, detail, sizeof(detail));
    auto result = originalEnginePlay(driver, sound, position, description, flag1, flag2, flags);
    if (!TryAcquireSRWLockExclusive(&mutex)) { ++dropped; return result; }
    auto next = (head + 1) % queue.size();
    if (next == tail) ++dropped;
    else {
        _snprintf_s(queue[head].text, sizeof(queue[head].text), _TRUNCATE,
            "%llu\tengine-play\tresult=%lu\t%s\r\n", GetTickCount64(), result, detail);
        head = next;
    }
    ReleaseSRWLockExclusive(&mutex);
    return result;
}
#endif

int __fastcall play(void* system, void* sound, void* group, bool paused, void** channel) {
    char name[512]{};
    int named = sound ? getName(sound, name, sizeof(name)) : -1;
    auto result = originalPlay(system, sound, group, paused, channel);
    if (!TryAcquireSRWLockExclusive(&mutex)) { ++dropped; return result; }
    auto next = (head + 1) % queue.size();
    if (next == tail) ++dropped;
    else {
        _snprintf_s(queue[head].text, sizeof(queue[head].text), _TRUNCATE,
            "%llu\tplay\tresult=%d\tpaused=%d\tnameResult=%d\t%s\r\n",
            GetTickCount64(), result, paused, named, name);
        head = next;
    }
    ReleaseSRWLockExclusive(&mutex);
    return result;
}

DWORD WINAPI run(void*) {
    wchar_t exe[32768]{};
    GetModuleFileNameW(nullptr, exe, 32768);
    auto leaf = wcsrchr(exe, L'\\');
    if (_wcsicmp(leaf ? leaf + 1 : exe, L"WRC7.exe")) return 1;
    wchar_t path[32768]{};
    GetModuleFileNameW(self, path, 32768);
    auto end = wcsrchr(path, L'.');
    if (!end) return 2;
    wcscpy_s(end, static_cast<size_t>(path + 32768 - end), L".log");
    HANDLE file = CreateFileW(path, FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE,
        nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return 3;
    auto write = [&](const char* text) { DWORD n; WriteFile(file, text, static_cast<DWORD>(strlen(text)), &n, nullptr); };
#ifdef KT_ENGINE_TRACE
    auto target = reinterpret_cast<unsigned char*>(GetModuleHandleW(nullptr)) + 0x5385d0;
    constexpr unsigned char expected[] = {0x4c,0x8b,0xdc,0x55,0x56,0x48,0x8d,0x6c,0x24,0x88,0x48,0x81,0xec,0x78,0x01,0,0};
    if (memcmp(target, expected, sizeof(expected)) != 0) {
        write("ERROR: engine function bytes differ\r\n"); CloseHandle(file); return 4;
    }
    auto initialized = MH_Initialize();
    auto created = initialized == MH_OK ? MH_CreateHook(target, &enginePlay,
        reinterpret_cast<void**>(&originalEnginePlay)) : initialized;
    if (created != MH_OK) {
        char text[160]; sprintf_s(text, "ERROR: engine hook at %p: init=%s create=%s\r\n",
            target, MH_StatusToString(initialized), MH_StatusToString(created));
        write(text); CloseHandle(file); return 4;
    }
#else
    auto fmod = GetModuleHandleW(L"fmod64.dll");
    auto target = fmod ? GetProcAddress(fmod,
        "?playSound@System@FMOD@@QEAA?AW4FMOD_RESULT@@PEAVSound@2@PEAVChannelGroup@2@_NPEAPEAVChannel@2@@Z") : nullptr;
    getName = fmod ? reinterpret_cast<Name>(GetProcAddress(fmod,
        "?getName@Sound@FMOD@@QEAA?AW4FMOD_RESULT@@PEADH@Z")) : nullptr;
    if (!target || !getName || MH_Initialize() != MH_OK ||
        MH_CreateHook(target, &play, reinterpret_cast<void**>(&originalPlay)) != MH_OK) {
        write("ERROR: required core exports or hook setup failed\r\n"); CloseHandle(file); return 4;
    }
#endif
    HMODULE pinned;
    if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN,
        reinterpret_cast<LPCWSTR>(&run), &pinned) || MH_EnableHook(target) != MH_OK) {
        write("ERROR: cannot enable core hook\r\n"); CloseHandle(file); return 5;
    }
    write("READY: WRC7 sound observer; original playback unchanged.\r\n");
    for (;;) {
        Record batch[32]; size_t count = 0;
        AcquireSRWLockExclusive(&mutex);
        while (tail != head && count < 32) { batch[count++] = queue[tail]; tail = (tail + 1) % queue.size(); }
        ReleaseSRWLockExclusive(&mutex);
        for (size_t i = 0; i < count; ++i) write(batch[i].text);
        auto lost = dropped.exchange(0);
        if (lost) { char text[64]; sprintf_s(text, "DROPPED: %u\r\n", lost); write(text); }
        Sleep(25);
    }
}
}
BOOL WINAPI DllMain(HMODULE current, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) {
        self = current; DisableThreadLibraryCalls(current);
        auto worker = CreateThread(nullptr, 0, run, nullptr, 0, nullptr);
        if (worker) CloseHandle(worker);
    }
    return TRUE;
}
