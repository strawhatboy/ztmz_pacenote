#include <windows.h>
#include <intrin.h>
#include <MinHook.h>
#include <mutex>
#include <string>

// Development-only observation of the loaded bridge's outgoing JSON. Does not
// open/consume its pipe or change the application's messages.
namespace {
using WriteFn = BOOL(WINAPI*)(HANDLE, LPCVOID, DWORD, LPDWORD, LPOVERLAPPED);
WriteFn original = nullptr;
HANDLE logFile = INVALID_HANDLE_VALUE;
uintptr_t bridgeBegin = 0, bridgeEnd = 0;
std::mutex logLock;
BOOL WINAPI Observe(HANDLE file, LPCVOID data, DWORD size, LPDWORD written, LPOVERLAPPED async)
{
    const auto caller = reinterpret_cast<uintptr_t>(_ReturnAddress());
    const BOOL result = original(file, data, size, written, async);
    const DWORD error = GetLastError();
    if (caller >= bridgeBegin && caller < bridgeEnd && data && size < 65536 && size > 0 &&
        static_cast<const char*>(data)[0] == '{')
    {
        std::lock_guard<std::mutex> guard(logLock);
        const std::string prefix = std::to_string(GetTickCount64()) + " result=" +
            std::to_string(result) + " bytes=" + std::to_string(written ? *written : 0) + " ";
        DWORD count;
        original(logFile, prefix.data(), static_cast<DWORD>(prefix.size()), &count, nullptr);
        original(logFile, data, size, &count, nullptr);
    }
    SetLastError(error);
    return result;
}
DWORD WINAPI Start(void*)
{
    const HMODULE bridge = GetModuleHandleW(L"ACRFmodLivePacenoteBridge.dll");
    if (!bridge) return 1;
    bridgeBegin = reinterpret_cast<uintptr_t>(bridge);
    const auto dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(bridge);
    const auto nt = reinterpret_cast<const IMAGE_NT_HEADERS*>(bridgeBegin + dos->e_lfanew);
    bridgeEnd = bridgeBegin + nt->OptionalHeader.SizeOfImage;
    logFile = CreateFileW(L"C:\\Users\\Andy\\lab\\ztmz_pacenote\\tmp\\acr_bridge_pipe_trace.log",
        FILE_APPEND_DATA, FILE_SHARE_READ, nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (logFile == INVALID_HANDLE_VALUE) return 2;
    auto target = GetProcAddress(GetModuleHandleW(L"kernel32.dll"), "WriteFile");
    if (MH_Initialize() != MH_OK || MH_CreateHook(target, Observe,
        reinterpret_cast<void**>(&original)) != MH_OK || MH_EnableHook(target) != MH_OK) return 3;
    const char started[] = "pipe-trace-started\r\n";
    DWORD count;
    original(logFile, started, sizeof(started) - 1, &count, nullptr);
    return 0;
}
}
BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH) {
        DisableThreadLibraryCalls(module);
        HANDLE thread = CreateThread(nullptr, 0, Start, nullptr, 0, nullptr);
        if (thread) CloseHandle(thread);
    }
    return TRUE;
}
