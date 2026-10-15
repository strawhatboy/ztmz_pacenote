#pragma once
#include <windows.h>
#include <tlhelp32.h>
#include <array>
#include <vector>
#include <cstring>
#include <cstdint>

// This is NOT a general instruction relocator. Each caller must supply verified,
// whole, position-independent instructions (WRC7: 17 bytes; WRC9: 19 bytes).
// The installed bytes must match exactly. An indirect absolute jump uses no
// scratch register and does not require executable memory near the game image.
namespace Wrc7FixedEntry {
inline constexpr unsigned char Entry[] = {0x4c,0x8b,0xdc,0x55,0x56,0x48,0x8d,0x6c,0x24,0x88,0x48,0x81,0xec,0x78,0x01,0,0};
inline void jump(unsigned char* destination, const void* target) {
    constexpr unsigned char opcode[] = {0xff,0x25,0,0,0,0};
    memcpy(destination, opcode, sizeof(opcode));
    memcpy(destination + sizeof(opcode), &target, sizeof(target));
}
struct Thread { HANDLE handle; bool suspended = false; };
template<size_t N>
inline bool installVerifiedEntry(unsigned char* target, void* replacement, void** original, HMODULE owner, const unsigned char (&entryBytes)[N]) {
    static_assert(N >= 14);
    if (memcmp(target, entryBytes, sizeof(entryBytes))) { SetLastError(ERROR_REVISION_MISMATCH); return false; }
    auto trampoline = static_cast<unsigned char*>(VirtualAlloc(nullptr, 4096, MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE));
    if (!trampoline) return false;
    memcpy(trampoline, entryBytes, sizeof(entryBytes));
    jump(trampoline + sizeof(entryBytes), target + sizeof(entryBytes));
    DWORD protection;
    if (!VirtualProtect(trampoline, 4096, PAGE_EXECUTE_READ, &protection)) {
        auto error = GetLastError(); VirtualFree(trampoline, 0, MEM_RELEASE); SetLastError(error); return false;
    }
    FlushInstructionCache(GetCurrentProcess(), trampoline, sizeof(entryBytes) + 14);
    std::array<unsigned char, sizeof(entryBytes)> patch;
    patch.fill(0x90); jump(patch.data(), replacement);
    std::vector<Thread> threads;
    HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPTHREAD, 0);
    if (snapshot == INVALID_HANDLE_VALUE) { VirtualFree(trampoline, 0, MEM_RELEASE); return false; }
    THREADENTRY32 entry{sizeof(THREADENTRY32)};
    DWORD error = ERROR_SUCCESS;
    if (!Thread32First(snapshot, &entry)) error = GetLastError();
    else do {
        if (entry.th32OwnerProcessID != GetCurrentProcessId() || entry.th32ThreadID == GetCurrentThreadId()) continue;
        HANDLE thread = OpenThread(THREAD_SUSPEND_RESUME | THREAD_GET_CONTEXT | THREAD_QUERY_LIMITED_INFORMATION, FALSE, entry.th32ThreadID);
        if (!thread) { error = GetLastError(); break; }
        threads.push_back({thread});
    } while (Thread32Next(snapshot, &entry));
    CloseHandle(snapshot);
    HMODULE pinned;
    if (!error && !GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN,
            reinterpret_cast<LPCWSTR>(owner), &pinned)) error = GetLastError();
    DWORD oldProtection = 0;
    if (!error && !VirtualProtect(target, sizeof(entryBytes), PAGE_EXECUTE_READWRITE, &oldProtection)) error = GetLastError();
    // Allocate/enumerate/open everything before freezing threads. No heap, file,
    // loader or logging operations are allowed while another thread is frozen.
    if (!error) {
        for (auto& thread : threads) {
            if (SuspendThread(thread.handle) == DWORD(-1)) { error = GetLastError(); break; }
            thread.suspended = true;
        }
        if (!error) for (auto& thread : threads) {
            CONTEXT context{}; context.ContextFlags = CONTEXT_CONTROL;
            if (!GetThreadContext(thread.handle, &context)) { error = GetLastError(); break; }
            auto ip = static_cast<uintptr_t>(context.Rip);
            auto start = reinterpret_cast<uintptr_t>(target);
            if (ip >= start && ip < start + sizeof(entryBytes)) { error = ERROR_BUSY; break; }
        }
        if (!error && memcmp(target, entryBytes, sizeof(entryBytes))) error = ERROR_REVISION_MISMATCH;
        if (!error) {
            *original = trampoline;
            MemoryBarrier();
            memcpy(target, patch.data(), patch.size());
            FlushInstructionCache(GetCurrentProcess(), target, patch.size());
        }
        for (auto& thread : threads) if (thread.suspended) ResumeThread(thread.handle);
    }
    if (oldProtection) { DWORD ignored; VirtualProtect(target, sizeof(entryBytes), oldProtection, &ignored); }
    for (auto& thread : threads) CloseHandle(thread.handle);
    if (error) { VirtualFree(trampoline, 0, MEM_RELEASE); SetLastError(error); return false; }
    return true; // Deliberately retained until game exit, together with pinned DLL.
}
inline bool install(unsigned char* target, void* replacement, void** original, HMODULE owner) {
    return installVerifiedEntry(target, replacement, original, owner, Entry);
}

}
