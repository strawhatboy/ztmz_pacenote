#include <windows.h>
#include <MinHook.h>

#include <atomic>
#include <cstdint>
#include <cstring>
#include <cwchar>
#include <string>

namespace
{
constexpr uintptr_t ProcessEventOffset = 0x0139E770;
constexpr uintptr_t AppendStringOffset = 0x011B2470;
constexpr wchar_t LogPath[] = L"C:\\Users\\Andy\\lab\\ztmz_pacenote\\tmp\\acr_pacenote_trace.log";

struct FName
{
    int32_t ComparisonIndex;
    uint32_t Number;
};

struct FString
{
    wchar_t* Data;
    int32_t Count;
    int32_t Capacity;
};

// UE5 UObject's name and class fields are stable in the Dumper-7 SDK for this build.
struct UObjectPrefix
{
    void* VTable;
    uint32_t ObjectFlags;
    int32_t InternalIndex;
    void* Class;
    FName Name;
};

using ProcessEventFn = void(__fastcall*)(void* object, void* function, void* parameters);
using AppendStringFn = void(__fastcall*)(const FName* name, FString& output);

ProcessEventFn OriginalProcessEvent = nullptr;
AppendStringFn AppendString = nullptr;
HANDLE LogFile = INVALID_HANDLE_VALUE;
std::atomic<uint64_t> Sequence = 0;
std::atomic_flag LogLock = ATOMIC_FLAG_INIT;

bool ToName(void* value, std::wstring& result)
{
    if (!value || !AppendString)
    {
        return false;
    }

    const auto* object = static_cast<const UObjectPrefix*>(value);
    wchar_t buffer[512]{};
    FString output{ buffer, 0, static_cast<int32_t>(std::size(buffer)) };
    AppendString(&object->Name, output);
    if (output.Count <= 0 || output.Count > static_cast<int32_t>(std::size(buffer)))
    {
        return false;
    }

    result.assign(buffer, buffer + output.Count);
    if (!result.empty() && result.back() == L'\0')
    {
        result.pop_back();
    }
    return !result.empty();
}

bool ContainsInsensitive(const std::wstring& value, const wchar_t* term)
{
    std::wstring folded = value;
    for (auto& character : folded)
    {
        character = static_cast<wchar_t>(towlower(character));
    }
    return folded.find(term) != std::wstring::npos;
}

bool IsFmodCall(const std::wstring& className, const std::wstring& functionName)
{
    if (!ContainsInsensitive(className, L"fmod"))
    {
        return false;
    }

    return functionName == L"SetEvent" ||
        functionName == L"SetParameter" ||
        functionName == L"SetProgrammerSoundName" ||
        functionName == L"PlayEvent2D" ||
        functionName == L"PlayEventAtLocation" ||
        functionName == L"PlayEventAttached" ||
        functionName == L"EventInstancePlay" ||
        functionName == L"EventInstanceSetParameter";
}

bool ToFName(const FName& name, std::wstring& result)
{
    if (!AppendString)
    {
        return false;
    }

    wchar_t buffer[512]{};
    FString output{ buffer, 0, static_cast<int32_t>(std::size(buffer)) };
    AppendString(&name, output);
    if (output.Count <= 0 || output.Count > static_cast<int32_t>(std::size(buffer)))
    {
        return false;
    }

    result.assign(buffer, buffer + output.Count);
    if (!result.empty() && result.back() == L'\0')
    {
        result.pop_back();
    }
    return !result.empty();
}

void WriteTrace(const std::wstring& objectName, const std::wstring& className, const std::wstring& functionName,
    const std::wstring& details = L"")
{
    if (LogFile == INVALID_HANDLE_VALUE)
    {
        return;
    }

    while (LogLock.test_and_set(std::memory_order_acquire))
    {
    }

    const auto sequence = ++Sequence;
    const std::wstring line = std::to_wstring(sequence) + L" | object=" + objectName +
        L" | class=" + className + L" | function=" + functionName + details + L"\r\n";
    DWORD written = 0;
    WriteFile(LogFile, line.data(), static_cast<DWORD>(line.size() * sizeof(wchar_t)), &written, nullptr);
    LogLock.clear(std::memory_order_release);
}

void __fastcall DetourProcessEvent(void* object, void* function, void* parameters)
{
    std::wstring objectName;
    std::wstring functionName;
    std::wstring className;

    const bool hasObjectName = ToName(object, objectName);
    const bool hasFunctionName = ToName(function, functionName);
    const auto* objectPrefix = static_cast<const UObjectPrefix*>(object);
    ToName(objectPrefix ? objectPrefix->Class : nullptr, className);
    if (hasObjectName && hasFunctionName && IsFmodCall(className, functionName))
    {
        std::wstring details;

        if (parameters && (functionName == L"SetEvent" || functionName == L"PlayEventAttached"))
        {
            void* event = nullptr;
            std::memcpy(&event, parameters, sizeof(event));
            std::wstring eventName;
            if (ToName(event, eventName))
            {
                details = L" | event=" + eventName;
            }
        }
        else if (parameters && functionName == L"PlayEvent2D")
        {
            void* event = nullptr;
            std::memcpy(&event, static_cast<const unsigned char*>(parameters) + sizeof(void*), sizeof(event));
            std::wstring eventName;
            if (ToName(event, eventName))
            {
                details = L" | event=" + eventName;
            }
        }
        else if (parameters && functionName == L"SetParameter")
        {
            FName parameterName{};
            float value = 0.0F;
            std::memcpy(&parameterName, parameters, sizeof(parameterName));
            std::memcpy(&value, static_cast<const unsigned char*>(parameters) + sizeof(parameterName), sizeof(value));
            std::wstring parameterText;
            if (ToFName(parameterName, parameterText))
            {
                details = L" | parameter=" + parameterText + L" | value=" + std::to_wstring(value);
            }
        }

        WriteTrace(objectName, className, functionName, details);
    }

    OriginalProcessEvent(object, function, parameters);
}

DWORD WINAPI InitializeTrace(void*)
{
    LogFile = CreateFileW(LogPath, FILE_APPEND_DATA, FILE_SHARE_READ, nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (LogFile == INVALID_HANDLE_VALUE)
    {
        return 1;
    }

    const HMODULE acrModule = GetModuleHandleW(nullptr);
    if (!acrModule)
    {
        WriteTrace(L"trace-init-failed", L"primary-module-unavailable", L"");
        return 2;
    }

    AppendString = reinterpret_cast<AppendStringFn>(reinterpret_cast<uintptr_t>(acrModule) + AppendStringOffset);
    if (MH_Initialize() != MH_OK)
    {
        WriteTrace(L"trace-init-failed", L"MinHook-initialize", L"");
        return 3;
    }

    const auto processEvent = reinterpret_cast<void*>(reinterpret_cast<uintptr_t>(acrModule) + ProcessEventOffset);
    if (MH_CreateHook(processEvent, DetourProcessEvent, reinterpret_cast<void**>(&OriginalProcessEvent)) != MH_OK ||
        MH_EnableHook(processEvent) != MH_OK)
    {
        MH_Uninitialize();
        WriteTrace(L"trace-init-failed", L"MinHook-create-or-enable", L"");
        CloseHandle(LogFile);
        LogFile = INVALID_HANDLE_VALUE;
        return 4;
    }

    WriteTrace(L"trace-started", L"acr.exe", L"UObject::ProcessEvent");
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
