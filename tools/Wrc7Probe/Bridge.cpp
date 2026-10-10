#include <windows.h>
#include "FixedEntryHook.h"
#include <array>
#include <atomic>
#include <cstdio>
#include <cstring>
#include "Countdown.h"

extern "C" {
__declspec(dllexport) volatile LONG Wrc7BridgeStatus = 0;
__declspec(dllexport) volatile DWORD Wrc7BridgeError = 0;
__declspec(dllexport) volatile LONG Wrc7CountdownReady = 0;
}

namespace {
// Verified WRC7 x64 driver ABI; the worker performs all pipe I/O.
using Play = unsigned long(__fastcall*)(void*, void*, void*, void*, bool, bool, unsigned long);
Play original;
struct Record { ULONGLONG tick; char resource[512]; };
std::array<Record, 256> records{};
SRWLOCK mutex = SRWLOCK_INIT;
size_t head = 0, tail = 0;
std::atomic<unsigned> dropped{0};

bool resourceName(void* sound, char (&name)[512]) noexcept {
    __try {
        auto bytes = static_cast<const unsigned char*>(sound);
        auto length = *reinterpret_cast<const size_t*>(bytes + 16);
        auto capacity = *reinterpret_cast<const size_t*>(bytes + 24);
        if (length == 0 || length >= sizeof(name) || capacity < length) return false;
        auto source = capacity < 16 ? reinterpret_cast<const char*>(bytes) : *reinterpret_cast<const char* const*>(bytes);
        memcpy(name, source, length);
        name[length] = 0;
        return _strnicmp(name, "DATA:/SOUNDS/VOICE/", 18) == 0 && strstr(name, "/PACENOTES/") != nullptr;
    } __except(EXCEPTION_EXECUTE_HANDLER) { return false; }
}

unsigned long __fastcall play(void* driver, void* sound, void* position, void* description,
    bool flag1, bool initiallyPaused, unsigned long flags) {
    char name[512]{};
    const bool relevant = resourceName(sound, name);
    const auto tick = GetTickCount64();
    auto result = original(driver, sound, position, description, flag1, initiallyPaused, flags);
    // The verified driver returns zero on failure and releases the channel
    // before returning unless initiallyPaused is true. Do not emit preloads.
    if (!result || initiallyPaused || !relevant) return result;
    if (!TryAcquireSRWLockExclusive(&mutex)) { ++dropped; return result; }
    auto next = (head + 1) % records.size();
    if (next == tail) ++dropped;
    else { records[head].tick = tick; strcpy_s(records[head].resource, name); head = next; }
    ReleaseSRWLockExclusive(&mutex);
    return result;
}

DWORD WINAPI run(void* module) {
    Wrc7BridgeStatus = 1;
    wchar_t exe[32768]{};
    GetModuleFileNameW(nullptr, exe, 32768);
    auto leaf = wcsrchr(exe, L'\\');
    if (_wcsicmp(leaf ? leaf + 1 : exe, L"WRC7.exe")) return 1;
    auto target = reinterpret_cast<unsigned char*>(GetModuleHandleW(nullptr)) + 0x5385d0;
    if (!Wrc7FixedEntry::install(target, reinterpret_cast<void*>(&play), reinterpret_cast<void**>(&original), static_cast<HMODULE>(module))) {
        Wrc7BridgeError = GetLastError(); Wrc7BridgeStatus = -1; return 2;
    }
    Wrc7BridgeStatus = 2;
    Wrc7CountdownReady = Wrc7Countdown::install() ? 1 : -1;

    wchar_t pipe[128];
    swprintf_s(pipe, L"\\\\.\\pipe\\ZTMZ.WRC7.Live.v1.%lu", GetCurrentProcessId());
    HANDLE connection = INVALID_HANDLE_VALUE;
    ULONGLONG heartbeat = 0;
    auto write = [&](const char* text) {
        DWORD count = 0, length = static_cast<DWORD>(strlen(text));
        if (WriteFile(connection, text, length, &count, nullptr) && count == length) return true;
        CloseHandle(connection); connection = INVALID_HANDLE_VALUE; return false;
    };
    for (;;) {
        if (connection == INVALID_HANDLE_VALUE) {
            connection = CreateFileW(pipe, GENERIC_WRITE, 0, nullptr, OPEN_EXISTING, 0, nullptr);
            AcquireSRWLockExclusive(&mutex); tail = head; ReleaseSRWLockExclusive(&mutex);
            if (connection == INVALID_HANDLE_VALUE) { Sleep(100); continue; }
            if (!write("READY\n")) continue;
            Wrc7BridgeStatus = 3;
            Wrc7Countdown::reconnect();
            heartbeat = GetTickCount64();
        }
        Wrc7Countdown::poll([&](const char* cue, ULONGLONG tick) {
            char line[128]; sprintf_s(line, "%llu\t%s\n", tick, cue); write(line);
        });
        Record batch[32]; size_t count = 0;
        AcquireSRWLockExclusive(&mutex);
        while (tail != head && count < 32) { batch[count++] = records[tail]; tail = (tail + 1) % records.size(); }
        ReleaseSRWLockExclusive(&mutex);
        for (size_t i = 0; i < count && connection != INVALID_HANDLE_VALUE; ++i) {
            if (GetTickCount64() - batch[i].tick > 500) continue;
            char line[640]; sprintf_s(line, "%llu\t%s\n", batch[i].tick, batch[i].resource); write(line);
        }
        if (connection != INVALID_HANDLE_VALUE && GetTickCount64() - heartbeat >= 1000) {
            char line[80]; sprintf_s(line, "STATS dropped=%u countdown=%ld\n", dropped.exchange(0), Wrc7CountdownReady);
            write(line); heartbeat = GetTickCount64();
        }
        Sleep(5);
    }
}
}
BOOL WINAPI DllMain(HMODULE module, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) {
        DisableThreadLibraryCalls(module);
        auto worker = CreateThread(nullptr, 0, run, module, 0, nullptr);
        if (worker) CloseHandle(worker);
    }
    return TRUE;
}
