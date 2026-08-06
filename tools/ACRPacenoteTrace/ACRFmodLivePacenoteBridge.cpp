#include <windows.h>
#include <MinHook.h>

#include <atomic>
#include <cctype>
#include <mutex>
#include <string>
#include <vector>

namespace
{
constexpr char FmodStudioModule[] = "fmodstudio.dll";
constexpr char GetSoundInfoExport[] = "?getSoundInfo@System@Studio@FMOD@@QEBA?AW4FMOD_RESULT@@PEBDPEAUFMOD_STUDIO_SOUND_INFO@@@Z";
constexpr wchar_t PipePath[] = L"\\\\.\\pipe\\ZTMZPacenote.ACR.Live.v1";
constexpr wchar_t LogPath[] = L"C:\\Users\\Andy\\lab\\ztmz_pacenote\\tmp\\acr_live_pacenote_bridge.log";

using SystemGetSoundInfoFn = int(__fastcall*)(void* system, const char* nameOrId, void* soundInfo);

SystemGetSoundInfoFn OriginalGetSoundInfo = nullptr;
HANDLE LogFile = INVALID_HANDLE_VALUE;
std::atomic<uint64_t> Sequence = 0;
std::mutex PacenoteLock;
std::vector<std::string> CurrentTokens;

enum class TokenPosition
{
    Start,
    Mid,
    End,
};

void WriteLog(const std::string& text)
{
    if (LogFile == INVALID_HANDLE_VALUE)
    {
        return;
    }

    const std::string line = std::to_string(++Sequence) + " | " + text + "\r\n";
    DWORD written = 0;
    WriteFile(LogFile, line.data(), static_cast<DWORD>(line.size()), &written, nullptr);
}

bool TryParsePacenoteToken(const char* soundName, std::string& token, TokenPosition& position)
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

    const size_t tokenStart = tokenPrefix + 4;
    const size_t cadence = name.find("_Any_", tokenStart);
    if (cadence == std::string::npos || cadence == tokenStart)
    {
        return false;
    }

    const size_t positionStart = cadence + 5;
    const size_t positionEnd = name.find('_', positionStart);
    const std::string positionName = name.substr(positionStart, positionEnd - positionStart);
    if (positionName == "Start")
    {
        position = TokenPosition::Start;
    }
    else if (positionName == "Mid")
    {
        position = TokenPosition::Mid;
    }
    else if (positionName == "End")
    {
        position = TokenPosition::End;
    }
    else
    {
        return false;
    }

    token = name.substr(tokenStart, cadence - tokenStart);
    return true;
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

void PublishTokens(const std::vector<std::string>& tokens)
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
    message += "]}\n";

    const HANDLE pipe = CreateFileW(PipePath, GENERIC_WRITE, 0, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (pipe == INVALID_HANDLE_VALUE)
    {
        WriteLog("pipe-unavailable: " + message);
        return;
    }

    DWORD written = 0;
    const BOOL succeeded = WriteFile(pipe, message.data(), static_cast<DWORD>(message.size()), &written, nullptr);
    CloseHandle(pipe);
    WriteLog(succeeded ? "published: " + message : "pipe-write-failed: " + message);
}

int __fastcall DetourGetSoundInfo(void* system, const char* nameOrId, void* soundInfo)
{
    std::string token;
    TokenPosition position{};
    if (TryParsePacenoteToken(nameOrId, token, position))
    {
        std::vector<std::string> fragmentTokens;
        {
            std::lock_guard<std::mutex> lock(PacenoteLock);
            if (position == TokenPosition::Start)
            {
                CurrentTokens.clear();
            }

            if (!CurrentTokens.empty() || position == TokenPosition::Start)
            {
                CurrentTokens.push_back(token);
                fragmentTokens.push_back(token);
            }

            if (position == TokenPosition::End)
            {
                CurrentTokens.clear();
            }
        }

        // Do not wait for the final fragment before queuing external audio. ACR resolves
        // each word before starting FMOD playback, so forwarding fragments preserves their
        // order while giving the external mixer a useful head start.
        PublishTokens(fragmentTokens);
    }

    return OriginalGetSoundInfo(system, nameOrId, soundInfo);
}

DWORD WINAPI InitializeBridge(void*)
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
