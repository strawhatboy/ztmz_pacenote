#include <windows.h>
#include <MinHook.h>

#include <atomic>
#include <cstdint>
#include <cstring>
#include <string>

namespace
{
constexpr char FmodStudioModule[] = "fmodstudio.dll";
constexpr char StartExport[] = "?start@EventInstance@Studio@FMOD@@QEAA?AW4FMOD_RESULT@@XZ";
constexpr char GetDescriptionExport[] = "?getDescription@EventInstance@Studio@FMOD@@QEBA?AW4FMOD_RESULT@@PEAPEAVEventDescription@23@@Z";
constexpr char GetPathExport[] = "?getPath@EventDescription@Studio@FMOD@@QEBA?AW4FMOD_RESULT@@PEADHPEAH@Z";
constexpr wchar_t LogPath[] = L"C:\\Users\\Andy\\lab\\ztmz_pacenote\\tmp\\acr_fmod_trace.log";

using EventInstanceStartFn = int(__fastcall*)(void* eventInstance);
using EventInstanceGetDescriptionFn = int(__fastcall*)(void* eventInstance, void** description);
using EventDescriptionGetPathFn = int(__fastcall*)(void* description, char* path, int capacity, int* retrieved);

EventInstanceStartFn OriginalStart = nullptr;
EventInstanceGetDescriptionFn GetDescription = nullptr;
EventDescriptionGetPathFn GetPath = nullptr;
HANDLE LogFile = INVALID_HANDLE_VALUE;
std::atomic<uint64_t> Sequence = 0;
std::atomic_flag LogLock = ATOMIC_FLAG_INIT;

void WriteTrace(const std::string& eventPath)
{
    if (LogFile == INVALID_HANDLE_VALUE)
    {
        return;
    }

    while (LogLock.test_and_set(std::memory_order_acquire))
    {
    }

    const std::string line = std::to_string(++Sequence) + " | event=" + eventPath + "\r\n";
    DWORD written = 0;
    WriteFile(LogFile, line.data(), static_cast<DWORD>(line.size()), &written, nullptr);
    LogLock.clear(std::memory_order_release);
}

std::string DescribeEvent(void* eventInstance)
{
    if (!eventInstance || !GetDescription || !GetPath)
    {
        return "<unresolved>";
    }

    void* description = nullptr;
    if (GetDescription(eventInstance, &description) != 0 || !description)
    {
        return "<description-unavailable>";
    }

    char path[1024]{};
    int retrieved = 0;
    if (GetPath(description, path, static_cast<int>(std::size(path)), &retrieved) != 0 || !path[0])
    {
        return "<path-unavailable>";
    }

    return path;
}

int __fastcall DetourStart(void* eventInstance)
{
    // Resolving a path only reads FMOD metadata before the original start call.
    WriteTrace(DescribeEvent(eventInstance));
    return OriginalStart(eventInstance);
}

DWORD WINAPI InitializeTrace(void*)
{
    LogFile = CreateFileW(LogPath, FILE_APPEND_DATA, FILE_SHARE_READ, nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (LogFile == INVALID_HANDLE_VALUE)
    {
        return 1;
    }

    const HMODULE fmod = GetModuleHandleA(FmodStudioModule);
    if (!fmod)
    {
        WriteTrace("trace-init-failed: fmodstudio.dll is not loaded");
        return 2;
    }

    GetDescription = reinterpret_cast<EventInstanceGetDescriptionFn>(GetProcAddress(fmod, GetDescriptionExport));
    GetPath = reinterpret_cast<EventDescriptionGetPathFn>(GetProcAddress(fmod, GetPathExport));
    const auto start = GetProcAddress(fmod, StartExport);
    if (!GetDescription || !GetPath || !start)
    {
        WriteTrace("trace-init-failed: required FMOD export unavailable");
        return 3;
    }

    if (MH_Initialize() != MH_OK ||
        MH_CreateHook(start, DetourStart, reinterpret_cast<void**>(&OriginalStart)) != MH_OK ||
        MH_EnableHook(start) != MH_OK)
    {
        MH_Uninitialize();
        WriteTrace("trace-init-failed: MinHook setup failed");
        return 4;
    }

    WriteTrace("trace-started: FMOD EventInstance::start");
    return 0;
}
}

BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        DisableThreadLibraryCalls(module);
        const HANDLE thread = CreateThread(nullptr, 0, InitializeTrace, nullptr, 0, nullptr);
        if (thread)
        {
            CloseHandle(thread);
        }
    }
    return TRUE;
}
