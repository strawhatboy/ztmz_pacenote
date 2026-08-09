#include <windows.h>
#include <MinHook.h>

#include <atomic>
#include <cstdio>
#include <cstdint>
#include <string>
#include <vector>

namespace
{
constexpr char FmodStudioModule[] = "fmodstudio.dll";
constexpr char SetParameterByNameExport[] = "?setParameterByName@EventInstance@Studio@FMOD@@QEAA?AW4FMOD_RESULT@@PEBDM_N@Z";
constexpr char GetDescriptionExport[] = "?getDescription@EventInstance@Studio@FMOD@@QEBA?AW4FMOD_RESULT@@PEAPEAVEventDescription@23@@Z";
constexpr char GetPathExport[] = "?getPath@EventDescription@Studio@FMOD@@QEBA?AW4FMOD_RESULT@@PEADHPEAH@Z";
constexpr wchar_t ZtmzRegistryPath[] = L"Software\\ZTMZ Next Generation Pacenote Tool";
constexpr wchar_t ZtmzHomeValueName[] = L"ZTMZHome";
constexpr wchar_t TraceLogFileName[] = L"acr_fmod_pacenote_parameters.log";

using EventInstanceSetParameterByNameFn = int(__fastcall*)(void* eventInstance, const char* name, float value, bool ignoreSeekSpeed);
using EventInstanceGetDescriptionFn = int(__fastcall*)(void* eventInstance, void** description);
using EventDescriptionGetPathFn = int(__fastcall*)(void* description, char* path, int capacity, int* retrieved);

EventInstanceSetParameterByNameFn OriginalSetParameterByName = nullptr;
EventInstanceGetDescriptionFn GetDescription = nullptr;
EventDescriptionGetPathFn GetPath = nullptr;
HANDLE LogFile = INVALID_HANDLE_VALUE;
std::atomic<uint64_t> Sequence = 0;
std::atomic_flag LogLock = ATOMIC_FLAG_INIT;

std::string GetLogTimestamp()
{
    SYSTEMTIME time{};
    GetLocalTime(&time);
    char buffer[32]{};
    sprintf_s(buffer, "%04u-%02u-%02u %02u:%02u:%02u.%03u",
        time.wYear, time.wMonth, time.wDay,
        time.wHour, time.wMinute, time.wSecond, time.wMilliseconds);
    return buffer;
}

void WriteTrace(const std::string& line)
{
    if (LogFile == INVALID_HANDLE_VALUE)
    {
        return;
    }

    while (LogLock.test_and_set(std::memory_order_acquire))
    {
    }

    const std::string output = GetLogTimestamp() + " | " + std::to_string(++Sequence) + " | " + line + "\r\n";
    DWORD written = 0;
    WriteFile(LogFile, output.data(), static_cast<DWORD>(output.size()), &written, nullptr);
    LogLock.clear(std::memory_order_release);
}

std::wstring GetZtmzLogsDirectory()
{
    std::wstring ztmzHome;
    DWORD byteCount = 0;
    if (RegGetValueW(HKEY_CURRENT_USER, ZtmzRegistryPath, ZtmzHomeValueName, RRF_RT_REG_SZ, nullptr, nullptr, &byteCount) == ERROR_SUCCESS && byteCount > sizeof(wchar_t))
    {
        std::vector<wchar_t> buffer(byteCount / sizeof(wchar_t));
        if (RegGetValueW(HKEY_CURRENT_USER, ZtmzRegistryPath, ZtmzHomeValueName, RRF_RT_REG_SZ, nullptr, buffer.data(), &byteCount) == ERROR_SUCCESS && buffer[0])
        {
            ztmzHome = buffer.data();
        }
    }

    if (ztmzHome.empty())
    {
        const DWORD profileLength = GetEnvironmentVariableW(L"USERPROFILE", nullptr, 0);
        if (profileLength == 0)
        {
            return {};
        }

        std::vector<wchar_t> profile(profileLength);
        if (GetEnvironmentVariableW(L"USERPROFILE", profile.data(), profileLength) == 0 || !profile[0])
        {
            return {};
        }

        ztmzHome = std::wstring(profile.data()) + L"\\Documents\\My Games\\ZTMZClub_nextgen";
    }

    if (ztmzHome.back() != L'\\' && ztmzHome.back() != L'/')
    {
        ztmzHome.push_back(L'\\');
    }
    ztmzHome += L"logs";

    if (!CreateDirectoryW(ztmzHome.c_str(), nullptr) && GetLastError() != ERROR_ALREADY_EXISTS)
    {
        return {};
    }

    return ztmzHome;
}

void OpenLogFile()
{
    const std::wstring directory = GetZtmzLogsDirectory();
    if (!directory.empty())
    {
        const std::wstring logPath = directory + L"\\" + TraceLogFileName;
        LogFile = CreateFileW(logPath.c_str(), FILE_APPEND_DATA, FILE_SHARE_READ, nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    }
}

std::string DescribeEvent(void* eventInstance)
{
    void* description = nullptr;
    if (!eventInstance || !GetDescription || !GetPath || GetDescription(eventInstance, &description) != 0 || !description)
    {
        return {};
    }

    char path[1024]{};
    int retrieved = 0;
    if (GetPath(description, path, static_cast<int>(std::size(path)), &retrieved) != 0)
    {
        return {};
    }

    return path;
}

int __fastcall DetourSetParameterByName(void* eventInstance, const char* name, float value, bool ignoreSeekSpeed)
{
    const std::string eventPath = DescribeEvent(eventInstance);
    if (eventPath.find("Pacenote") != std::string::npos || eventPath.find("pacenote") != std::string::npos)
    {
        WriteTrace("event=" + eventPath + " | parameter=" + (name ? name : "<null>") + " | value=" + std::to_string(value));
    }

    return OriginalSetParameterByName(eventInstance, name, value, ignoreSeekSpeed);
}

DWORD WINAPI InitializeTrace(void*)
{
    OpenLogFile();

    const HMODULE fmod = GetModuleHandleA(FmodStudioModule);
    if (!fmod)
    {
        WriteTrace("trace-init-failed: fmodstudio.dll is not loaded");
        return 2;
    }

    GetDescription = reinterpret_cast<EventInstanceGetDescriptionFn>(GetProcAddress(fmod, GetDescriptionExport));
    GetPath = reinterpret_cast<EventDescriptionGetPathFn>(GetProcAddress(fmod, GetPathExport));
    const auto setParameterByName = GetProcAddress(fmod, SetParameterByNameExport);
    if (!GetDescription || !GetPath || !setParameterByName)
    {
        WriteTrace("trace-init-failed: required FMOD export unavailable");
        return 3;
    }

    if (MH_Initialize() != MH_OK ||
        MH_CreateHook(setParameterByName, DetourSetParameterByName, reinterpret_cast<void**>(&OriginalSetParameterByName)) != MH_OK ||
        MH_EnableHook(setParameterByName) != MH_OK)
    {
        MH_Uninitialize();
        WriteTrace("trace-init-failed: MinHook setup failed");
        return 4;
    }

    WriteTrace("trace-started: FMOD EventInstance::setParameterByName");
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
