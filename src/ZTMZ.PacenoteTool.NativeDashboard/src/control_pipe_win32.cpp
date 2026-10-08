#include "control_pipe_win32.hpp"
#include <windows.h>
#include <sddl.h>
#include <array>
#include <stdexcept>
#include <thread>
#include <vector>
#include <memory>
namespace ztmz {
namespace {
struct Handle {
    HANDLE value;
    explicit Handle(HANDLE h):value(h){}
    ~Handle(){if(value && value!=INVALID_HANDLE_VALUE) CloseHandle(value);}
    Handle(const Handle&)=delete;
};
struct LocalMemory {void* value=nullptr;~LocalMemory(){if(value) LocalFree(value);}};
// Every overlapped operation is drained before its storage goes out of scope.
bool finish(HANDLE pipe,OVERLAPPED& operation,BOOL immediate,DWORD& bytes,DWORD timeout) {
    if(immediate) return true;
    if(GetLastError()!=ERROR_IO_PENDING) return false;
    if(WaitForSingleObject(operation.hEvent,timeout)!=WAIT_OBJECT_0) {
        CancelIoEx(pipe,&operation);
        GetOverlappedResult(pipe,&operation,&bytes,TRUE);
        return false;
    }
    return GetOverlappedResult(pipe,&operation,&bytes,FALSE)!=FALSE;
}
void serve(HANDLE raw,std::function<std::string(std::string_view)> handler) noexcept {
    Handle pipe(raw),event(CreateEventW(nullptr,TRUE,FALSE,nullptr));
    if(!event.value) return;
    for(;;) {
        ResetEvent(event.value);OVERLAPPED operation{};operation.hEvent=event.value;DWORD bytes=0;
        BOOL connected=ConnectNamedPipe(pipe.value,&operation);
        auto error=connected?ERROR_SUCCESS:GetLastError();
        if(error!=ERROR_PIPE_CONNECTED && !finish(pipe.value,operation,connected,bytes,INFINITE)) {
            DisconnectNamedPipe(pipe.value);continue;
        }
        ULONG client=0;DWORD ownSession=0,clientSession=0;
        bool allowed=GetNamedPipeClientProcessId(pipe.value,&client)
            && ProcessIdToSessionId(GetCurrentProcessId(),&ownSession)
            && ProcessIdToSessionId(client,&clientSession) && ownSession==clientSession;
        if(allowed) {
            std::array<char,4096> buffer{};ResetEvent(event.value);operation={};operation.hEvent=event.value;
            if(finish(pipe.value,operation,ReadFile(pipe.value,buffer.data(),static_cast<DWORD>(buffer.size()),&bytes,&operation),bytes,2000) && bytes) {
                try {
                    auto response=handler(std::string_view(buffer.data(),bytes));
                    if(!response.empty() && response.size()<=buffer.size()) {
                        ResetEvent(event.value);operation={};operation.hEvent=event.value;
                        if(finish(pipe.value,operation,WriteFile(pipe.value,response.data(),static_cast<DWORD>(response.size()),&bytes,&operation),bytes,2000)) {
                            // Client acknowledges receipt before disconnect discards buffered output.
                            // A deadline replaces FlushFileBuffers, which could block forever.
                            char ack=0;ResetEvent(event.value);operation={};operation.hEvent=event.value;
                            finish(pipe.value,operation,ReadFile(pipe.value,&ack,1,&bytes,&operation),bytes,2000);
                        }
                    }
                } catch(...) {OutputDebugStringA("ZTMZ control request failed");}
            }
        }
        DisconnectNamedPipe(pipe.value);
    }
}
}
std::string controlInstance() {
    FILETIME created{},exited{},kernel{},user{};
    if(!GetProcessTimes(GetCurrentProcess(),&created,&exited,&kernel,&user)) throw std::runtime_error("Cannot identify overlay process");
    const auto ticks=(static_cast<unsigned long long>(created.dwHighDateTime)<<32)|created.dwLowDateTime;
    return std::to_string(GetCurrentProcessId())+":"+std::to_string(ticks);
}
void startControlPipe(std::function<std::string(std::string_view)> handler) {
    HANDLE rawToken=nullptr;
    if(!OpenProcessToken(GetCurrentProcess(),TOKEN_QUERY,&rawToken)) throw std::runtime_error("Cannot read overlay identity");
    Handle token(rawToken);DWORD size=0;
    GetTokenInformation(token.value,TokenUser,nullptr,0,&size);
    std::vector<unsigned char> info(size);
    if(!GetTokenInformation(token.value,TokenUser,info.data(),size,&size)) throw std::runtime_error("Cannot read overlay user");
    LPWSTR sid=nullptr;
    if(!ConvertSidToStringSidW(reinterpret_cast<TOKEN_USER*>(info.data())->User.Sid,&sid)) throw std::runtime_error("Cannot encode overlay user");
    LocalMemory sidMemory{sid},descriptor;
    auto sddl=L"D:P(A;;GA;;;"+std::wstring(sid)+L")";
    if(!ConvertStringSecurityDescriptorToSecurityDescriptorW(sddl.c_str(),SDDL_REVISION_1,&descriptor.value,nullptr)) throw std::runtime_error("Cannot secure overlay control pipe");
    SECURITY_ATTRIBUTES security{sizeof(SECURITY_ATTRIBUTES),descriptor.value,FALSE};
    auto name=L"\\\\.\\pipe\\ztmz-dashboard-"+std::to_wstring(GetCurrentProcessId());
    HANDLE pipe=CreateNamedPipeW(name.c_str(),PIPE_ACCESS_DUPLEX|FILE_FLAG_OVERLAPPED|FILE_FLAG_FIRST_PIPE_INSTANCE,
        PIPE_TYPE_MESSAGE|PIPE_READMODE_MESSAGE|PIPE_WAIT|PIPE_REJECT_REMOTE_CLIENTS,1,4096,4096,0,&security);
    if(pipe==INVALID_HANDLE_VALUE) throw std::runtime_error("Cannot create exclusive overlay control pipe");
    try {std::thread(serve,pipe,std::move(handler)).detach();}
    catch(...) {CloseHandle(pipe);throw;}
}
}
