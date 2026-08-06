#include <windows.h>
#include <MinHook.h>

#include <atomic>
#include <string>

namespace
{
constexpr char FmodStudioModule[] = "fmodstudio.dll";
constexpr char GetSoundInfoExport[] = "?getSoundInfo@System@Studio@FMOD@@QEBA?AW4FMOD_RESULT@@PEBDPEAUFMOD_STUDIO_SOUND_INFO@@@Z";
constexpr wchar_t LogPath[] = L"C:\\Users\\Andy\\lab\\ztmz_pacenote\\tmp\\acr_fmod_sound_info_trace.log";

using SystemGetSoundInfoFn = int(__fastcall*)(void* system, const char* nameOrId, void* soundInfo);

SystemGetSoundInfoFn OriginalGetSoundInfo = nullptr;
HANDLE LogFile = INVALID_HANDLE_VALUE;
std::atomic<uint64_t> Sequence = 0;
std::atomic_flag LogLock = ATOMIC_FLAG_INIT;

void WriteTrace(const char* nameOrId)
{
    if (LogFile == INVALID_HANDLE_VALUE)
    {
        return;
    }

    while (LogLock.test_and_set(std::memory_order_acquire))
    {
    }

    const std::string line = std::to_string(++Sequence) + " | sound=" + (nameOrId ? nameOrId : "<null>") + "\r\n";
    DWORD written = 0;
    WriteFile(LogFile, line.data(), static_cast<DWORD>(line.size()), &written, nullptr);
    LogLock.clear(std::memory_order_release);
}

int __fastcall DetourGetSoundInfo(void* system, const char* nameOrId, void* soundInfo)
{
    // This is a read-only observation of FMOD's programmer-sound lookup name.
    WriteTrace(nameOrId);
    return OriginalGetSoundInfo(system, nameOrId, soundInfo);
}

DWORD WINAPI InitializeTrace(void*)
{
    LogFile = CreateFileW(LogPath, FILE_APPEND_DATA, FILE_SHARE_READ, nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (LogFile == INVALID_HANDLE_VALUE)
    {
        return 1;
    }

    const HMODULE fmod = GetModuleHandleA(FmodStudioModule);
    const auto getSoundInfo = fmod ? GetProcAddress(fmod, GetSoundInfoExport) : nullptr;
    if (!getSoundInfo)
    {
        WriteTrace("trace-init-failed: FMOD getSoundInfo export unavailable");
        return 2;
    }

    if (MH_Initialize() != MH_OK ||
        MH_CreateHook(getSoundInfo, DetourGetSoundInfo, reinterpret_cast<void**>(&OriginalGetSoundInfo)) != MH_OK ||
        MH_EnableHook(getSoundInfo) != MH_OK)
    {
        MH_Uninitialize();
        WriteTrace("trace-init-failed: MinHook setup failed");
        return 3;
    }

    WriteTrace("trace-started: FMOD System::getSoundInfo");
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
