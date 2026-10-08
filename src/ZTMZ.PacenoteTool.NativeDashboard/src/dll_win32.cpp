// Graphics-neutral DLL lifecycle. API adapters/hooks are registered separately.
#include "hooks/overlay_hook.hpp"
#include "control_pipe_win32.hpp"
#include <windows.h>
#include <thread>
#include <mutex>
#include <algorithm>
#include <stdexcept>
#include <cwchar>
namespace {
struct State {
    std::mutex controlMutex;
    std::jthread network;
    ztmz::SharedScene shared;
    ztmz::RenderApiRouter router;
    std::vector<std::unique_ptr<ztmz::OverlayHook>> hooks;
    unsigned short port=0;
    ztmz::RenderApi preference=ztmz::RenderApi::Auto;
};
// Installed callbacks require process-lifetime storage; the module is pinned.
State& state(){static auto* value=new State;return *value;}
bool requested(const ztmz::OverlayHook& hook,const std::vector<ztmz::RenderApi>& candidates) {
    return std::any_of(hook.apis().begin(),hook.apis().end(),[&](auto api){return std::find(candidates.begin(),candidates.end(),api)!=candidates.end();});
}
ztmz::RenderApi requestedApi() {
    char value[32]{};auto size=GetEnvironmentVariableA("ZTMZ_RENDER_API",value,sizeof(value));
    if(!size) return ztmz::RenderApi::Auto;
    if(size>=sizeof(value)) throw std::invalid_argument("ZTMZ_RENDER_API is too long");
    return ztmz::parseRenderApi(value);
}
std::string controlRequest(std::string_view request);
void ensureControl() {
    static std::once_flag initialized;
    std::call_once(initialized,[]{ztmz::startControlPipe(controlRequest);});
}
BOOL start(unsigned short port,ztmz::RenderApi preference,uintptr_t targetWindow=0) {
    try {
        if(!port) return FALSE;
        auto& s=state();std::lock_guard lock(s.controlMutex);
        if(s.network.joinable()) {
            if(s.port!=port || s.preference!=preference || s.shared.targetWindow.load()!=targetWindow) throw std::runtime_error("Stop before changing overlay configuration");
            return TRUE;
        }
        s.router.configure(preference,ztmz::compiledOverlayApis());
        HMODULE pinned=nullptr;
        if(!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_PIN,
            reinterpret_cast<LPCWSTR>(&start),&pinned)) return FALSE;
        ensureControl();
        if(s.hooks.empty()) s.hooks=ztmz::createOverlayHooks(s.shared,s.router);
        const auto candidates=s.router.candidates();
        try {
            std::vector<ztmz::RenderApi> installed;
            for(auto& hook:s.hooks) if(requested(*hook,candidates)) {
                try {hook->install();for(auto api:hook->apis()) if(std::find(candidates.begin(),candidates.end(),api)!=candidates.end()) installed.push_back(api);}
                catch(const std::exception& e){if(preference!=ztmz::RenderApi::Auto) throw;OutputDebugStringA(e.what());}
            }
            s.router.configure(preference,installed);
            s.shared.progress.reset();
            s.shared.targetWindow.store(targetWindow);
            s.network=std::jthread([&s,port](std::stop_token stop){ztmz::receive(stop,s.shared,port);});
            for(auto& hook:s.hooks) if(requested(*hook,s.router.candidates())) hook->enable();
            s.port=port;s.preference=preference;
        } catch(...) {
            for(auto& hook:s.hooks) hook->stop();
            s.network.request_stop();if(s.network.joinable()) s.network.join();
            throw;
        }
        return TRUE;
    } catch(const std::exception& e){OutputDebugStringA(e.what());return FALSE;}
}
}
extern "C" __declspec(dllexport) BOOL WINAPI ZtmzDashboardStart(unsigned short port) {
    try {return start(port,requestedApi());}
    catch(const std::exception& e){OutputDebugStringA(e.what());return FALSE;}
}
extern "C" __declspec(dllexport) void WINAPI ZtmzDashboardStop() {
    auto& s=state();std::lock_guard lock(s.controlMutex);
    for(auto& hook:s.hooks) hook->stop();
    s.network.request_stop();if(s.network.joinable()) s.network.join();
    s.shared.progress.reset();
}
namespace {
std::string controlRequest(std::string_view wire) {
    ztmz::Json response={{"version",1},{"moduleVersion",1},{"pid",GetCurrentProcessId()},{"instance",ztmz::controlInstance()},{"ok",false}};
    try {
        auto request=ztmz::Json::parse(wire);
        if(!request.is_object() || !request.contains("version") || !request["version"].is_number_integer() || request["version"]!=1)
            throw std::invalid_argument("Unsupported control protocol version");
        auto command=request.at("command").get<std::string>();
        if(command!="status") {
            if(request.at("instance").get<std::string>()!=response["instance"].get<std::string>())
                throw std::invalid_argument("Stale overlay process instance");
            if(command=="start") {
                const auto& port=request.at("port");
                if(!port.is_number_integer() || port<1 || port>65535) throw std::invalid_argument("Invalid loopback port");
                auto api=ztmz::parseRenderApi(request.at("api").get<std::string>());
                uintptr_t target=0;
                if(request.contains("targetWindow")) {
                    const auto& value=request.at("targetWindow");
                    if(!value.is_number_unsigned()) throw std::invalid_argument("Invalid target window");
                    target=value.get<uintptr_t>();
                    DWORD owner=0;
                    if(target && (!GetWindowThreadProcessId(reinterpret_cast<HWND>(target),&owner) || owner!=GetCurrentProcessId()))
                        throw std::invalid_argument("Target window does not belong to overlay process");
                }
                if(!start(port.get<unsigned short>(),api,target)) throw std::runtime_error("Overlay start failed; verify API availability and stop before reconfiguration");
            } else if(command=="stop") ZtmzDashboardStop();
            else throw std::invalid_argument("Unknown control command");
        }
        response["ok"]=true;
    } catch(const std::exception& e) {response["error"]=std::string(e.what()).substr(0,512);}
    auto& s=state();std::lock_guard lock(s.controlMutex);
    response["started"]=s.network.joinable();
    response["port"]=s.port;
    response["requestedApi"]=ztmz::apiName(s.preference);
    response["selectedApi"]=ztmz::apiName(s.router.active());
    {
        auto& progress=s.shared.progress;std::lock_guard frameLock(progress.mutex);
        response["submittedFrames"]=progress.submittedFrames;
        response["window"]=progress.window;
        response["lastFrameAgeMs"]=progress.lastFrameTick?static_cast<int64_t>(GetTickCount64()-progress.lastFrameTick):-1;
        response["renderError"]=progress.error;
    }
    // Submission acknowledges CPU/backend work, not display scan-out or dashboard data arrival.
    return response.dump();
}
}
#ifdef ZTMZ_NATIVE_HOOK_TEST
// Only the controlled-host DLL exposes fixtures. Never compiled into the product DLL.
extern "C" __declspec(dllexport) int WINAPI ZtmzDashboardTestActiveApi() {
    return static_cast<int>(state().router.active());
}
extern "C" __declspec(dllexport) void WINAPI ZtmzDashboardTestScene() {
    auto& s=state();std::lock_guard lock(s.shared.mutex);
    s.shared.scene.width=1920;s.shared.scene.height=1080;
    s.shared.scene.layers={{"fixture","dynamic",ztmz::Json::array({
        {{"type","fillRectangle"},{"x",96},{"y",96},{"width",192},{"height",192},{"color",0xffff0000u}}
    })}};
}
#endif
namespace {
DWORD WINAPI bootstrap(void*) {
    try {
        HMODULE pinned=nullptr;
        if(GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_PIN,
            reinterpret_cast<LPCWSTR>(&bootstrap),&pinned)) ensureControl();
    } catch(const std::exception& e){OutputDebugStringA(e.what());}
    // Loading only opens control. The host must authorize endpoint/API/window before hooking.
    return 0;
}
}
BOOL WINAPI DllMain(HINSTANCE module,DWORD reason,LPVOID) {
    if(reason==DLL_PROCESS_ATTACH) {
        DisableThreadLibraryCalls(module);
#ifndef ZTMZ_NATIVE_HOOK_TEST
        auto thread=CreateThread(nullptr,0,bootstrap,nullptr,0,nullptr);
        if(thread) CloseHandle(thread);
#endif
    }
    return TRUE;
}
