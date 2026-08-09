#include <windows.h>
#include <MinHook.h>

#include <atomic>
#include <cctype>
#include <cstdio>
#include <mutex>
#include <string>
#include <vector>

namespace
{
constexpr char FmodStudioModule[] = "fmodstudio.dll";
constexpr char GetSoundInfoExport[] = "?getSoundInfo@System@Studio@FMOD@@QEBA?AW4FMOD_RESULT@@PEBDPEAUFMOD_STUDIO_SOUND_INFO@@@Z";
constexpr wchar_t PipePath[] = L"\\\\.\\pipe\\ZTMZPacenote.ACR.Live.v1";
constexpr wchar_t ZtmzRegistryPath[] = L"Software\\ZTMZ Next Generation Pacenote Tool";
constexpr wchar_t ZtmzHomeValueName[] = L"ZTMZHome";
constexpr wchar_t BridgeLogFileName[] = L"acr_live_pacenote_bridge.log";

using SystemGetSoundInfoFn = int(__fastcall*)(void* system, const char* nameOrId, void* soundInfo);

SystemGetSoundInfoFn OriginalGetSoundInfo = nullptr;
HANDLE LogFile = INVALID_HANDLE_VALUE;
std::atomic<uint64_t> Sequence = 0;
std::atomic_bool PipeUnavailableLogged = false;
std::atomic_bool PipeWriteFailedLogged = false;
std::mutex PacenoteLock;
std::vector<std::string> CurrentTokens;

struct PacenoteLookup
{
    std::string rawSoundName;
    std::string token;
    std::string placement;
    std::string variant;
};

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

void WriteLog(const std::string& text)
{
    if (LogFile == INVALID_HANDLE_VALUE)
    {
        return;
    }

    const std::string line = GetLogTimestamp() + " | " + std::to_string(++Sequence) + " | " + text + "\r\n";
    DWORD written = 0;
    WriteFile(LogFile, line.data(), static_cast<DWORD>(line.size()), &written, nullptr);
}

void WritePipeUnavailableOnce(DWORD error)
{
    bool expected = false;
    if (PipeUnavailableLogged.compare_exchange_strong(expected, true))
    {
        WriteLog("pipe-unavailable: CreateFileW failed (error=" + std::to_string(error) + ")");
    }
}

void WritePipeWriteFailureOnce(DWORD error)
{
    bool expected = false;
    if (PipeWriteFailedLogged.compare_exchange_strong(expected, true))
    {
        WriteLog("pipe-write-failed: WriteFile failed (error=" + std::to_string(error) + ")");
    }
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

    std::wstring directory = ztmzHome;
    if (directory.back() != L'\\' && directory.back() != L'/')
    {
        directory.push_back(L'\\');
    }
    directory += L"logs";

    if (!CreateDirectoryW(directory.c_str(), nullptr) && GetLastError() != ERROR_ALREADY_EXISTS)
    {
        return {};
    }

    return directory;
}

void OpenLogFile()
{
    const std::wstring directory = GetZtmzLogsDirectory();
    if (directory.empty())
    {
        return;
    }

    const std::wstring logPath = directory + L"\\" + BridgeLogFileName;
    LogFile = CreateFileW(logPath.c_str(), FILE_APPEND_DATA, FILE_SHARE_READ, nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
}

bool TryParsePacenoteLookup(const char* soundName, PacenoteLookup& lookup)
{
    if (!soundName)
    {
        return false;
    }

    const std::string name(soundName);
    const size_t tokenPrefix = name.find("/PN_");
    if (tokenPrefix == std::string::npos)
    {
        return false;
    }

    lookup = {};
    lookup.rawSoundName = name;

    const size_t tokenStart = tokenPrefix + 4;
    const size_t tokenEnd = name.find("_Any_", tokenStart);
    if (tokenEnd == std::string::npos)
    {
        lookup.token = name.substr(tokenStart);
        return !lookup.token.empty();
    }

    if (tokenEnd == tokenStart)
    {
        return false;
    }

    lookup.token = name.substr(tokenStart, tokenEnd - tokenStart);
    const size_t positionStart = tokenEnd + 5;
    const size_t positionEnd = name.find('_', positionStart);
    if (positionEnd == std::string::npos)
    {
        lookup.placement = name.substr(positionStart);
        return true;
    }

    lookup.placement = name.substr(positionStart, positionEnd - positionStart);
    lookup.variant = name.substr(positionEnd + 1);
    return true;
}

bool IsPacenoteFragmentPlacement(const std::string& placement)
{
    return placement == "Start" || placement == "Mid" || placement == "End";
}

std::string EscapeJson(const std::string& value)
{
    std::string escaped;
    escaped.reserve(value.size());
    for (const char character : value)
    {
        if (character == '"' || character == '\\')
        {
            escaped.push_back('\\');
        }
        escaped.push_back(character);
    }
    return escaped;
}

void PublishTokens(const std::vector<std::string>& tokens, const PacenoteLookup& lookup)
{
    if (tokens.empty())
    {
        return;
    }

    std::string message = "{\"version\":1,\"id\":\"fmod-" + std::to_string(++Sequence) + "\",\"tokens\":[";
    for (size_t index = 0; index < tokens.size(); ++index)
    {
        if (index > 0)
        {
            message += ',';
        }
        message += "\"" + EscapeJson(tokens[index]) + "\"";
    }
    message += "],\"rawSoundName\":\"" + EscapeJson(lookup.rawSoundName) +
        "\",\"token\":\"" + EscapeJson(lookup.token) +
        "\",\"placement\":\"" + EscapeJson(lookup.placement) +
        "\",\"variant\":\"" + EscapeJson(lookup.variant) + "\"}\n";

    const HANDLE pipe = CreateFileW(PipePath, GENERIC_WRITE, 0, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (pipe == INVALID_HANDLE_VALUE)
    {
        WritePipeUnavailableOnce(GetLastError());
        return;
    }

    DWORD written = 0;
    const BOOL succeeded = WriteFile(pipe, message.data(), static_cast<DWORD>(message.size()), &written, nullptr);
    const DWORD error = succeeded ? ERROR_SUCCESS : GetLastError();
    CloseHandle(pipe);
    if (!succeeded)
    {
        WritePipeWriteFailureOnce(error);
    }
}

int __fastcall DetourGetSoundInfo(void* system, const char* nameOrId, void* soundInfo)
{
    PacenoteLookup lookup;
    if (TryParsePacenoteLookup(nameOrId, lookup))
    {
        if (IsPacenoteFragmentPlacement(lookup.placement))
        {
            std::lock_guard<std::mutex> lock(PacenoteLock);
            if (lookup.placement == "Start")
            {
                CurrentTokens.clear();
            }

            // Keep the normal Start/Mid/End sequence for diagnostics and future grouping,
            // but send every fragment immediately below.
            CurrentTokens.push_back(lookup.token);

            if (lookup.placement == "End")
            {
                CurrentTokens.clear();
            }
        }

        // Every /PN_ lookup is forwarded. Start/Mid/End only describes ordinary phrase
        // fragments; resources with any other placement remain standalone events.
        PublishTokens({ lookup.token }, lookup);
    }

    return OriginalGetSoundInfo(system, nameOrId, soundInfo);
}

DWORD WINAPI InitializeBridge(void*)
{
    // Diagnostics must never prevent the hook from serving live pacenotes.
    OpenLogFile();

    const HMODULE fmod = GetModuleHandleA(FmodStudioModule);
    const auto getSoundInfo = fmod ? GetProcAddress(fmod, GetSoundInfoExport) : nullptr;
    if (!getSoundInfo)
    {
        WriteLog("bridge-init-failed: FMOD getSoundInfo export unavailable");
        return 2;
    }

    if (MH_Initialize() != MH_OK ||
        MH_CreateHook(getSoundInfo, DetourGetSoundInfo, reinterpret_cast<void**>(&OriginalGetSoundInfo)) != MH_OK ||
        MH_EnableHook(getSoundInfo) != MH_OK)
    {
        MH_Uninitialize();
        WriteLog("bridge-init-failed: MinHook setup failed");
        return 3;
    }

    WriteLog("bridge-started");
    return 0;
}
}

BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        DisableThreadLibraryCalls(module);
        const HANDLE thread = CreateThread(nullptr, 0, InitializeBridge, nullptr, 0, nullptr);
        if (thread)
        {
            CloseHandle(thread);
        }
    }
    return TRUE;
}
