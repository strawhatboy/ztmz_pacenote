#include <windows.h>

#include <cwchar>
#include <iostream>

int wmain(int argc, wchar_t* argv[])
{
    if (argc != 3)
    {
        std::wcerr << L"Usage: ACRPacenoteTraceInjector <pid> <absolute-dll-path>\n";
        return 1;
    }

    const DWORD processId = static_cast<DWORD>(wcstoul(argv[1], nullptr, 10));
    const size_t bytes = (wcslen(argv[2]) + 1) * sizeof(wchar_t);
    HANDLE process = OpenProcess(
        PROCESS_CREATE_THREAD | PROCESS_QUERY_INFORMATION | PROCESS_VM_OPERATION | PROCESS_VM_WRITE | PROCESS_VM_READ,
        FALSE,
        processId);
    if (!process)
    {
        std::wcerr << L"OpenProcess failed: " << GetLastError() << L"\n";
        return 2;
    }

    void* remotePath = VirtualAllocEx(process, nullptr, bytes, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    if (!remotePath)
    {
        std::wcerr << L"VirtualAllocEx failed: " << GetLastError() << L"\n";
        CloseHandle(process);
        return 3;
    }

    SIZE_T written = 0;
    if (!WriteProcessMemory(process, remotePath, argv[2], bytes, &written) || written != bytes)
    {
        std::wcerr << L"WriteProcessMemory failed: " << GetLastError() << L"\n";
        VirtualFreeEx(process, remotePath, 0, MEM_RELEASE);
        CloseHandle(process);
        return 4;
    }

    const HMODULE kernel32 = GetModuleHandleW(L"kernel32.dll");
    const auto loadLibraryW = reinterpret_cast<LPTHREAD_START_ROUTINE>(GetProcAddress(kernel32, "LoadLibraryW"));
    HANDLE thread = CreateRemoteThread(process, nullptr, 0, loadLibraryW, remotePath, 0, nullptr);
    if (!thread)
    {
        std::wcerr << L"CreateRemoteThread failed: " << GetLastError() << L"\n";
        VirtualFreeEx(process, remotePath, 0, MEM_RELEASE);
        CloseHandle(process);
        return 5;
    }

    WaitForSingleObject(thread, 10000);
    DWORD moduleHandle = 0;
    GetExitCodeThread(thread, &moduleHandle);
    CloseHandle(thread);
    VirtualFreeEx(process, remotePath, 0, MEM_RELEASE);
    CloseHandle(process);

    if (!moduleHandle)
    {
        std::wcerr << L"LoadLibraryW returned null.\n";
        return 6;
    }

    std::wcout << L"Injected ACR pacenote trace DLL.\n";
    return 0;
}
