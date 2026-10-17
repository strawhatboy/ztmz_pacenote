#include "hooks/overlay_hook.hpp"
#include "graphics/dx9/backend.hpp"
#include "graphics/render_session.hpp"
#include "graphics/async_preparation.hpp"
#include "hooks/rbr_dx9_device.hpp"
#include <wrl/client.h>
#include <MinHook.h>
#include <atomic>
#include <chrono>
#include <array>
#include <stdexcept>
namespace ztmz {
namespace {
using Microsoft::WRL::ComPtr;
class Dx9Hook final:public OverlayHook {
    using Present=HRESULT (STDMETHODCALLTYPE*)(IDirect3DDevice9*,const RECT*,const RECT*,HWND,const RGNDATA*);
    using Reset=HRESULT (STDMETHODCALLTYPE*)(IDirect3DDevice9*,D3DPRESENT_PARAMETERS*);
    using PresentEx=HRESULT (STDMETHODCALLTYPE*)(IDirect3DDevice9Ex*,const RECT*,const RECT*,HWND,const RGNDATA*,DWORD);
    using ResetEx=HRESULT (STDMETHODCALLTYPE*)(IDirect3DDevice9Ex*,D3DPRESENT_PARAMETERS*,D3DDISPLAYMODEEX*);
    static inline Dx9Hook* instance_=nullptr;
    static inline thread_local bool inPresent_=false;
    Present present_=nullptr;Reset reset_=nullptr;PresentEx presentEx_=nullptr;ResetEx resetEx_=nullptr;
    SharedScene& shared_;RenderApiRouter& router_;std::recursive_mutex mutex_;std::atomic<bool> enabled_{false};
    bool installed_=false;IDirect3DDevice9* selected_=nullptr;
    std::unique_ptr<RenderSession> renderer_;
    AsyncPreparation<PreparedContext> preparation_;
    std::chrono::steady_clock::time_point previous_;
    void clear() noexcept {preparation_.cancel();renderer_.reset();selected_=nullptr;router_.release(RenderApi::Dx9);}
    void render(IDirect3DDevice9* device) {
        std::lock_guard lock(mutex_);
        if(!enabled_ || (selected_ && selected_!=device) || FAILED(device->TestCooperativeLevel())) return;
        D3DDEVICE_CREATION_PARAMETERS window{};
        if(FAILED(device->GetCreationParameters(&window))) return;
        if(auto target=shared_.targetWindow.load();target && target!=reinterpret_cast<uintptr_t>(window.hFocusWindow)) return;
        if(!renderer_) {
            D3DDEVICE_CREATION_PARAMETERS params{};
            if(FAILED(device->GetCreationParameters(&params)) || !IsWindowVisible(params.hFocusWindow) || !router_.tryActivate(RenderApi::Dx9)) return;
            selected_=device;
            if(!preparation_.started())preparation_.start([]{return std::make_unique<PreparedContext>();});
            auto prepared=preparation_.take();if(!prepared)return;
            renderer_=std::make_unique<RenderSession>(createDx9Backend(device),std::move(prepared));previous_=std::chrono::steady_clock::now();
        }
        ComPtr<IDirect3DSurface9> surface;if(FAILED(device->GetBackBuffer(0,0,D3DBACKBUFFER_TYPE_MONO,&surface))) return;
        D3DSURFACE_DESC desc{};surface->GetDesc(&desc);
        Scene scene;std::shared_ptr<const ImageAssets> images;
        {std::lock_guard lock(shared_.mutex);scene=shared_.scene;images=shared_.images;}
        auto now=std::chrono::steady_clock::now();Dx9Frame frame(surface.Get());
        renderer_->render({desc.Width,desc.Height,std::chrono::duration<float>(now-previous_).count()},frame,scene,*images);previous_=now;
        if(desc.Width && desc.Height && frame.submitted) {
            auto& progress=shared_.progress;std::lock_guard status(progress.mutex);
            ++progress.submittedFrames;progress.window=reinterpret_cast<uintptr_t>(window.hFocusWindow);
            progress.lastFrameTick=GetTickCount64();progress.error.clear();
        }
    }
    static void draw(IDirect3DDevice9* device) noexcept {
        try{instance_->render(device);}catch(const std::exception& e){
            {auto& progress=instance_->shared_.progress;std::lock_guard status(progress.mutex);progress.error=e.what();progress.lastFrameTick=0;}
            instance_->stop();
        }
    }
    struct Guard {bool previous=inPresent_;Guard(){inPresent_=true;}~Guard(){inPresent_=previous;}};
    static HRESULT STDMETHODCALLTYPE present(IDirect3DDevice9* d,const RECT* a,const RECT* b,HWND w,const RGNDATA* dirty) {
        Guard guard;if(!guard.previous) draw(d);
        return instance_->present_(d,a,b,w,dirty);
    }
    static HRESULT STDMETHODCALLTYPE presentEx(IDirect3DDevice9Ex* d,const RECT* a,const RECT* b,HWND w,const RGNDATA* dirty,DWORD flags) {
        Guard guard;if(!guard.previous) draw(d);
        return instance_->presentEx_(d,a,b,w,dirty,flags);
    }
    static HRESULT STDMETHODCALLTYPE reset(IDirect3DDevice9* d,D3DPRESENT_PARAMETERS* p) {
        std::lock_guard lock(instance_->mutex_);if(instance_->selected_==d) instance_->clear();
        return instance_->reset_(d,p);
    }
    static HRESULT STDMETHODCALLTYPE resetEx(IDirect3DDevice9Ex* d,D3DPRESENT_PARAMETERS* p,D3DDISPLAYMODEEX* mode) {
        std::lock_guard lock(instance_->mutex_);if(instance_->selected_==d) instance_->clear();
        return instance_->resetEx_(d,p,mode);
    }
public:
    Dx9Hook(SharedScene& s,RenderApiRouter& r):shared_(s),router_(r){}
    std::span<const RenderApi> apis() const noexcept override {static constexpr std::array a{RenderApi::Dx9};return a;}
    void enable() noexcept override {enabled_=true;}
    void stop() noexcept override {enabled_=false;std::lock_guard lock(mutex_);clear();}
    void install() override {
        if(installed_) return;instance_=this;
        // Do this before either Direct3DCreate9 variant: GaugerPlugin re-hooks
        // CreateDevice on every API creation and can destroy its own trampoline.
        const auto existing=existingRbrDx9Targets();
        struct Window {HWND h=nullptr;~Window(){if(h) DestroyWindow(h);}} window;
        ComPtr<IDirect3D9> api;
        D3DPRESENT_PARAMETERS p{};p.Windowed=TRUE;p.SwapEffect=D3DSWAPEFFECT_DISCARD;p.hDeviceWindow=window.h;
        // A tiny overlapped window can have a zero-sized client area. Do not
        // let D3D9 infer an invalid back buffer from that non-visible probe.
        p.BackBufferWidth=64;p.BackBufferHeight=64;
        ComPtr<IDirect3DDevice9> device;
        if(!existing) {
            window.h=CreateWindowExW(0,L"STATIC",L"ZTMZ DX9 probe",WS_OVERLAPPEDWINDOW,0,0,128,128,nullptr,nullptr,GetModuleHandleW(nullptr),nullptr);
            if(!window.h) throw std::runtime_error("Cannot create DX9 probe window");
            p.hDeviceWindow=window.h;
            api.Attach(Direct3DCreate9(D3D_SDK_VERSION));if(!api) throw std::runtime_error("Cannot create D3D9");
            if(FAILED(api->CreateDevice(D3DADAPTER_DEFAULT,D3DDEVTYPE_HAL,window.h,D3DCREATE_SOFTWARE_VERTEXPROCESSING,&p,&device))) throw std::runtime_error("Cannot create D3D9 probe device");
        }
        auto result=MH_Initialize();if(result!=MH_OK && result!=MH_ERROR_ALREADY_INITIALIZED) throw std::runtime_error("MinHook initialization failed");
        std::vector<void*> addresses;
        auto hook=[&](void* target,void* callback,void** original){
            if(MH_CreateHook(target,callback,original)!=MH_OK) throw std::runtime_error("Cannot create D3D9 hook");addresses.push_back(target);
        };
        try {
            auto targets=existing.value_or(Dx9DeviceTargets{});
            if(!existing) {
                auto** v=*reinterpret_cast<void***>(device.Get());
                targets={reinterpret_cast<uintptr_t>(v[16]),reinterpret_cast<uintptr_t>(v[17])};
            }
            hook(reinterpret_cast<void*>(targets.present),reinterpret_cast<void*>(&present),reinterpret_cast<void**>(&present_));
            hook(reinterpret_cast<void*>(targets.reset),reinterpret_cast<void*>(&reset),reinterpret_cast<void**>(&reset_));
            ComPtr<IDirect3D9Ex> ex;ComPtr<IDirect3DDevice9Ex> exDevice;
            if(!existing && SUCCEEDED(Direct3DCreate9Ex(D3D_SDK_VERSION,&ex)) && SUCCEEDED(ex->CreateDeviceEx(D3DADAPTER_DEFAULT,D3DDEVTYPE_HAL,window.h,D3DCREATE_SOFTWARE_VERTEXPROCESSING,&p,nullptr,&exDevice))) {
                auto** ev=*reinterpret_cast<void***>(exDevice.Get());
                hook(ev[121],reinterpret_cast<void*>(&presentEx),reinterpret_cast<void**>(&presentEx_));
                hook(ev[132],reinterpret_cast<void*>(&resetEx),reinterpret_cast<void**>(&resetEx_));
            }
            for(auto address:addresses) if(MH_QueueEnableHook(address)!=MH_OK) throw std::runtime_error("Cannot queue D3D9 hook");
            if(MH_ApplyQueued()!=MH_OK) throw std::runtime_error("Cannot enable D3D9 hooks");
            installed_=true;
        } catch(...) {for(auto address:addresses){MH_QueueDisableHook(address);MH_DisableHook(address);MH_RemoveHook(address);}throw;}
    }
};
}
std::unique_ptr<OverlayHook> createDx9Hook(SharedScene& s,RenderApiRouter& r){return std::make_unique<Dx9Hook>(s,r);}
}
