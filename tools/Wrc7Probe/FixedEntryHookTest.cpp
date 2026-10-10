#include "FixedEntryHook.h"
#include <cstdio>
using Function = int(*)();
Function original;
int replacement() { return original() + 1; }
int main() {
    auto code = static_cast<unsigned char*>(VirtualAlloc(nullptr, 4096, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE));
    if (!code) return 1;
    memcpy(code, Wrc7FixedEntry::Entry, sizeof(Wrc7FixedEntry::Entry));
    // Restore the precise verified entry's stack changes, then return 42.
    constexpr unsigned char tail[] = {0x48,0x81,0xc4,0x78,0x01,0,0,0x5e,0x5d,0xb8,42,0,0,0,0xc3};
    memcpy(code + sizeof(Wrc7FixedEntry::Entry), tail, sizeof(tail));
    DWORD old;
    if (!VirtualProtect(code, 4096, PAGE_EXECUTE_READ, &old)) return 2;
    FlushInstructionCache(GetCurrentProcess(), code, 4096);
    auto function = reinterpret_cast<Function>(code);
    if (function() != 42) return 3;
    if (!Wrc7FixedEntry::install(code, reinterpret_cast<void*>(&replacement), reinterpret_cast<void**>(&original), GetModuleHandleW(nullptr))) {
        printf("Install failed: %lu\n", GetLastError()); return 4;
    }
    if (function() != 43 || original() != 42) return 5;
    void* unused;
    if (Wrc7FixedEntry::install(code, reinterpret_cast<void*>(&replacement), &unused, GetModuleHandleW(nullptr)) || GetLastError() != ERROR_REVISION_MISMATCH) return 6;
    puts("Fixed-entry hook: original call, replacement call, and double-install rejection passed.");
    return 0;
}
