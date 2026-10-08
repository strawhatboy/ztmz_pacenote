// Graphics-neutral DLL lifecycle. API adapters/hooks are registered separately.
#include "hooks/overlay_hook.hpp"
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
}
extern "C" __declspec(dllexport) BOOL WINAPI ZtmzDashboardStart(unsigned short port) {
    try {
        if(!port) return FALSE;
        auto& s=state();std::lock_guard lock(s.controlMutex);
        if(s.network.joinable()) return TRUE;
        auto preference=requestedApi();
        s.router.configure(preference,ztmz::compiledOverlayApis());
        HMODULE pinned=nullptr;
        if(!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_PIN,
            reinterpret_cast<LPCWSTR>(&ZtmzDashboardStart),&pinned)) return FALSE;
        if(s.hooks.empty()) s.hooks=ztmz::createOverlayHooks(s.shared,s.router);
        const auto candidates=s.router.candidates();
        try {
            std::vector<ztmz::RenderApi> installed;
            for(auto& hook:s.hooks) if(requested(*hook,candidates)) {
                try {hook->install();for(auto api:hook->apis()) if(std::find(candidates.begin(),candidates.end(),api)!=candidates.end()) installed.push_back(api);}
                catch(const std::exception& e){if(preference!=ztmz::RenderApi::Auto) throw;OutputDebugStringA(e.what());}
            }
            s.router.configure(preference,installed);
            s.network=std::jthread([&s,port](std::stop_token stop){ztmz::receive(stop,s.shared,port);});
            for(auto& hook:s.hooks) if(requested(*hook,s.router.candidates())) hook->enable();
        } catch(...) {for(auto& hook:s.hooks) hook->stop();throw;}
        return TRUE;
    } catch(const std::exception& e){OutputDebugStringA(e.what());return FALSE;}
}
extern "C" __declspec(dllexport) void WINAPI ZtmzDashboardStop() {
    auto& s=state();std::lock_guard lock(s.controlMutex);
    for(auto& hook:s.hooks) hook->stop();
    s.network.request_stop();if(s.network.joinable()) s.network.join();
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
    unsigned short port=8080;wchar_t value[16]{};
    auto length=GetEnvironmentVariableW(L"ZTMZ_DASHBOARD_PORT",value,16);
    if(length>0 && length<16) {
        wchar_t* end=nullptr;auto parsed=std::wcstoul(value,&end,10);
        if(end!=value && *end==L'\0' && parsed>0 && parsed<=65535) port=static_cast<unsigned short>(parsed);
    }
    ZtmzDashboardStart(port);return 0;
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
