#include <winsock2.h>
#include <windows.h>
#include <tlhelp32.h>

#include <atomic>
#include <cstdint>
#include <filesystem>
#include <iostream>
#include <mutex>
#include <string>
#include <thread>
#include <vector>

#pragma comment(lib, "Ws2_32.lib")

namespace
{
constexpr wchar_t PipePath[] = L"\\\\.\\pipe\\ZTMZPacenote.ACR.Live.v1";
constexpr wchar_t BridgeFileName[] = L"ACRFmodLivePacenoteBridge.dll";
constexpr uint8_t ProtocolVersion = 1;
constexpr size_t GraphicsSize = 1588;
constexpr size_t PhysicsSize = 800;
constexpr size_t StaticSize = 820;

enum class FrameType : uint8_t
{
    Hello = 1,
    Telemetry = 2,
    Pacenote = 3,
    Status = 4
};

std::atomic_bool Running = true;
std::mutex SocketWriteLock;
std::mutex PipeHandleLock;
SOCKET HostSocket = INVALID_SOCKET;
HANDLE ActivePipe = INVALID_HANDLE_VALUE;

bool RegisterActivePipe(HANDLE pipe)
{
    std::lock_guard<std::mutex> lock(PipeHandleLock);
    if (!Running)
    {
        return false;
    }
    ActivePipe = pipe;
    return true;
}

void CloseActivePipe(HANDLE pipe)
{
    std::lock_guard<std::mutex> lock(PipeHandleLock);
    if (ActivePipe == pipe)
    {
        ActivePipe = INVALID_HANDLE_VALUE;
    }
    CloseHandle(pipe);
}

void CancelActivePipeIo()
{
    std::lock_guard<std::mutex> lock(PipeHandleLock);
    if (ActivePipe != INVALID_HANDLE_VALUE)
    {
        CancelIoEx(ActivePipe, nullptr);
    }
}

bool SendAll(const void* data, size_t size)
{
    const auto* cursor = static_cast<const char*>(data);
    while (size > 0)
    {
        const int chunk = send(HostSocket, cursor, static_cast<int>(size), 0);
        if (chunk <= 0)
        {
            Running = false;
            return false;
        }
        cursor += chunk;
        size -= static_cast<size_t>(chunk);
    }
    return true;
}

bool SendFrame(FrameType type, const void* payload, uint32_t payloadSize)
{
    const uint8_t header[12] = {
        'Z', 'A', 'R', '1',
        ProtocolVersion,
        static_cast<uint8_t>(type),
        0, 0,
        static_cast<uint8_t>((payloadSize >> 24) & 0xff),
        static_cast<uint8_t>((payloadSize >> 16) & 0xff),
        static_cast<uint8_t>((payloadSize >> 8) & 0xff),
        static_cast<uint8_t>(payloadSize & 0xff)
    };

    std::lock_guard<std::mutex> lock(SocketWriteLock);
    return SendAll(header, sizeof(header)) && (payloadSize == 0 || SendAll(payload, payloadSize));
}

bool SendTextFrame(FrameType type, const std::string& payload)
{
    return SendFrame(type, payload.data(), static_cast<uint32_t>(payload.size()));
}

std::wstring GetExecutableDirectory()
{
    std::vector<wchar_t> buffer(32768);
    const DWORD length = GetModuleFileNameW(nullptr, buffer.data(), static_cast<DWORD>(buffer.size()));
    if (length == 0 || length >= buffer.size())
    {
        return {};
    }
    return std::filesystem::path(std::wstring(buffer.data(), length)).parent_path().wstring();
}

DWORD FindProcessId(const wchar_t* executableName)
{
    const HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (snapshot == INVALID_HANDLE_VALUE)
    {
        return 0;
    }

    PROCESSENTRY32W entry{sizeof(entry)};
    DWORD result = 0;
    if (Process32FirstW(snapshot, &entry))
    {
        do
        {
            if (_wcsicmp(entry.szExeFile, executableName) == 0)
            {
                result = entry.th32ProcessID;
                break;
            }
        } while (Process32NextW(snapshot, &entry));
    }
    CloseHandle(snapshot);
    return result;
}

uintptr_t FindRemoteModuleBase(DWORD processId, const wchar_t* moduleName)
{
    const HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, processId);
    if (snapshot == INVALID_HANDLE_VALUE)
    {
        return 0;
    }

    MODULEENTRY32W entry{sizeof(entry)};
    uintptr_t result = 0;
    if (Module32FirstW(snapshot, &entry))
    {
        do
        {
            if (_wcsicmp(entry.szModule, moduleName) == 0)
            {
                result = reinterpret_cast<uintptr_t>(entry.modBaseAddr);
                break;
            }
        } while (Module32NextW(snapshot, &entry));
    }
    CloseHandle(snapshot);
    return result;
}

bool IsModuleLoaded(DWORD processId, const wchar_t* moduleName)
{
    return FindRemoteModuleBase(processId, moduleName) != 0;
}

bool InjectBridge(DWORD processId, const std::wstring& bridgePath, std::string& error)
{
    if (IsModuleLoaded(processId, BridgeFileName))
    {
        return true;
    }

    const HANDLE process = OpenProcess(
        PROCESS_CREATE_THREAD | PROCESS_QUERY_INFORMATION | PROCESS_VM_OPERATION | PROCESS_VM_WRITE | PROCESS_VM_READ,
        FALSE,
        processId);
    if (!process)
    {
        error = "OpenProcess failed: " + std::to_string(GetLastError());
        return false;
    }

    const HMODULE localKernel32 = GetModuleHandleW(L"kernel32.dll");
    const auto localLoadLibrary = reinterpret_cast<uintptr_t>(GetProcAddress(localKernel32, "LoadLibraryW"));
    const auto remoteKernel32 = FindRemoteModuleBase(processId, L"kernel32.dll");
    if (!localKernel32 || !localLoadLibrary || !remoteKernel32)
    {
        error = "Cannot resolve LoadLibraryW in the target process.";
        CloseHandle(process);
        return false;
    }

    const auto loadLibraryOffset = localLoadLibrary - reinterpret_cast<uintptr_t>(localKernel32);
    const auto remoteLoadLibrary = reinterpret_cast<LPTHREAD_START_ROUTINE>(remoteKernel32 + loadLibraryOffset);
    const size_t pathBytes = (bridgePath.size() + 1) * sizeof(wchar_t);
    void* remotePath = VirtualAllocEx(process, nullptr, pathBytes, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    if (!remotePath)
    {
        error = "VirtualAllocEx failed: " + std::to_string(GetLastError());
        CloseHandle(process);
        return false;
    }

    SIZE_T written = 0;
    if (!WriteProcessMemory(process, remotePath, bridgePath.c_str(), pathBytes, &written) || written != pathBytes)
    {
        error = "WriteProcessMemory failed: " + std::to_string(GetLastError());
        VirtualFreeEx(process, remotePath, 0, MEM_RELEASE);
        CloseHandle(process);
        return false;
    }

    const HANDLE thread = CreateRemoteThread(process, nullptr, 0, remoteLoadLibrary, remotePath, 0, nullptr);
    if (!thread)
    {
        error = "CreateRemoteThread failed: " + std::to_string(GetLastError());
        VirtualFreeEx(process, remotePath, 0, MEM_RELEASE);
        CloseHandle(process);
        return false;
    }

    const DWORD waitResult = WaitForSingleObject(thread, 10000);
    DWORD moduleHandleLow = 0;
    GetExitCodeThread(thread, &moduleHandleLow);
    CloseHandle(thread);
    VirtualFreeEx(process, remotePath, 0, MEM_RELEASE);
    CloseHandle(process);

    if (waitResult != WAIT_OBJECT_0 || moduleHandleLow == 0)
    {
        error = waitResult == WAIT_TIMEOUT ? "LoadLibraryW timed out." : "LoadLibraryW returned null.";
        return false;
    }
    return true;
}

void RunPipeServer()
{
    std::string pending;
    std::vector<char> buffer(4096);
    while (Running)
    {
        const HANDLE pipe = CreateNamedPipeW(
            PipePath,
            PIPE_ACCESS_INBOUND,
            PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT,
            1,
            4096,
            4096,
            0,
            nullptr);
        if (pipe == INVALID_HANDLE_VALUE)
        {
            SendTextFrame(FrameType::Status, "{\"state\":\"pipe-error\"}");
            Sleep(1000);
            continue;
        }
        if (!RegisterActivePipe(pipe))
        {
            CloseHandle(pipe);
            return;
        }

        const BOOL connected = ConnectNamedPipe(pipe, nullptr) || GetLastError() == ERROR_PIPE_CONNECTED;
        if (!connected)
        {
            CloseActivePipe(pipe);
            Sleep(250);
            continue;
        }

        SendTextFrame(FrameType::Status, "{\"state\":\"bridge-connected\"}");
        DWORD read = 0;
        while (Running && ReadFile(pipe, buffer.data(), static_cast<DWORD>(buffer.size()), &read, nullptr) && read > 0)
        {
            pending.append(buffer.data(), read);
            size_t newline = 0;
            while ((newline = pending.find('\n')) != std::string::npos)
            {
                std::string line = pending.substr(0, newline);
                pending.erase(0, newline + 1);
                if (!line.empty() && line.back() == '\r')
                {
                    line.pop_back();
                }
                if (!line.empty() && !SendTextFrame(FrameType::Pacenote, line))
                {
                    break;
                }
            }
        }

        DisconnectNamedPipe(pipe);
        CloseActivePipe(pipe);
        pending.clear();
    }
}

struct MappingView
{
    HANDLE mapping = nullptr;
    const uint8_t* view = nullptr;

    bool Open(const wchar_t* name)
    {
        mapping = OpenFileMappingW(FILE_MAP_READ, FALSE, name);
        if (!mapping)
        {
            return false;
        }
        view = static_cast<const uint8_t*>(MapViewOfFile(mapping, FILE_MAP_READ, 0, 0, 0));
        if (!view)
        {
            CloseHandle(mapping);
            mapping = nullptr;
            return false;
        }
        return true;
    }

    void Close()
    {
        if (view)
        {
            UnmapViewOfFile(view);
            view = nullptr;
        }
        if (mapping)
        {
            CloseHandle(mapping);
            mapping = nullptr;
        }
    }

    ~MappingView()
    {
        Close();
    }
};

void RunTelemetry()
{
    MappingView graphics;
    MappingView physics;
    MappingView staticInfo;
    std::vector<uint8_t> payload(GraphicsSize + PhysicsSize + StaticSize);
    bool available = false;

    while (Running)
    {
        if (!graphics.view || !physics.view || !staticInfo.view)
        {
            graphics.Close();
            physics.Close();
            staticInfo.Close();
            if (!graphics.Open(L"Local\\acpmf_graphics") ||
                !physics.Open(L"Local\\acpmf_physics") ||
                !staticInfo.Open(L"Local\\acpmf_static"))
            {
                if (available)
                {
                    available = false;
                    SendTextFrame(FrameType::Status, "{\"state\":\"telemetry-unavailable\"}");
                }
                Sleep(500);
                continue;
            }
            available = true;
            SendTextFrame(FrameType::Status, "{\"state\":\"telemetry-available\"}");
        }

        memcpy(payload.data(), graphics.view, GraphicsSize);
        memcpy(payload.data() + GraphicsSize, physics.view, PhysicsSize);
        memcpy(payload.data() + GraphicsSize + PhysicsSize, staticInfo.view, StaticSize);
        if (!SendFrame(FrameType::Telemetry, payload.data(), static_cast<uint32_t>(payload.size())))
        {
            return;
        }
        Sleep(16);
    }
}

bool ConnectToHost(uint16_t port)
{
    HostSocket = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
    if (HostSocket == INVALID_SOCKET)
    {
        return false;
    }

    sockaddr_in address{};
    address.sin_family = AF_INET;
    address.sin_port = htons(port);
    address.sin_addr.s_addr = htonl(INADDR_LOOPBACK);
    return connect(HostSocket, reinterpret_cast<sockaddr*>(&address), sizeof(address)) == 0;
}
}

int wmain(int argc, wchar_t* argv[])
{
    if (argc < 3 || argc > 4)
    {
        std::wcerr << L"Usage: ACRProtonRelay <port> <session-token> [--probe-only]\n";
        return 1;
    }
    const bool probeOnly = argc == 4 && _wcsicmp(argv[3], L"--probe-only") == 0;

    const auto portValue = wcstoul(argv[1], nullptr, 10);
    if (portValue == 0 || portValue > 65535)
    {
        std::wcerr << L"Invalid port.\n";
        return 2;
    }

    WSADATA winsock{};
    if (WSAStartup(MAKEWORD(2, 2), &winsock) != 0)
    {
        return 3;
    }

    if (!ConnectToHost(static_cast<uint16_t>(portValue)))
    {
        WSACleanup();
        return 4;
    }

    std::string token;
    for (const wchar_t* cursor = argv[2]; *cursor; ++cursor)
    {
        token.push_back(*cursor <= 0x7f ? static_cast<char>(*cursor) : '?');
    }
    if (!SendTextFrame(FrameType::Hello, "{\"token\":\"" + token + "\",\"relayVersion\":1}"))
    {
        closesocket(HostSocket);
        WSACleanup();
        return 5;
    }

    if (probeOnly)
    {
        SendTextFrame(FrameType::Status, "{\"state\":\"probe-ready\"}");
        closesocket(HostSocket);
        WSACleanup();
        return 0;
    }

    const DWORD acrProcessId = FindProcessId(L"acr.exe");
    if (acrProcessId == 0)
    {
        SendTextFrame(FrameType::Status, "{\"state\":\"game-not-found\"}");
        closesocket(HostSocket);
        WSACleanup();
        return 6;
    }

    std::thread pipeThread(RunPipeServer);
    std::thread telemetryThread(RunTelemetry);
    Sleep(100);

    const std::wstring bridgePath = (std::filesystem::path(GetExecutableDirectory()) / BridgeFileName).wstring();
    std::string injectionError;
    if (!InjectBridge(acrProcessId, bridgePath, injectionError))
    {
        SendTextFrame(FrameType::Status, "{\"state\":\"injection-failed\",\"detail\":\"" + injectionError + "\"}");
        Running = false;
    }
    else
    {
        SendTextFrame(FrameType::Status, "{\"state\":\"injected\"}");
    }

    const HANDLE acrProcess = OpenProcess(SYNCHRONIZE, FALSE, acrProcessId);
    if (!acrProcess)
    {
        SendTextFrame(FrameType::Status, "{\"state\":\"process-watch-failed\"}");
        Running = false;
    }
    while (Running && WaitForSingleObject(acrProcess, 250) == WAIT_TIMEOUT)
    {
    }
    if (acrProcess)
    {
        CloseHandle(acrProcess);
    }

    Running = false;
    shutdown(HostSocket, SD_BOTH);
    CancelActivePipeIo();
    if (telemetryThread.joinable())
    {
        telemetryThread.join();
    }
    if (pipeThread.joinable())
    {
        pipeThread.join();
    }
    closesocket(HostSocket);
    WSACleanup();
    return injectionError.empty() ? 0 : 7;
}
