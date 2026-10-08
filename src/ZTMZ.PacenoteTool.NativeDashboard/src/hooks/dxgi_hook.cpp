#include "hooks/overlay_hook.hpp"
#include "hooks/dxgi_presenter.hpp"
#include <d3d11.h>
#include <algorithm>
#include <windows.h>
#include <dxgi1_4.h>
#ifdef ZTMZ_HAS_DX12
#include "hooks/dx12_queue.hpp"
#endif
#include <wrl/client.h>
#include <MinHook.h>
#include <atomic>
#include <chrono>
#include <stdexcept>
namespace ztmz {
namespace {
using Microsoft::WRL::ComPtr;
class DxgiHook final : public OverlayHook {
    using Present=HRESULT (STDMETHODCALLTYPE*)(IDXGISwapChain*,UINT,UINT);
    using Resize=HRESULT (STDMETHODCALLTYPE*)(IDXGISwapChain*,UINT,UINT,UINT,DXGI_FORMAT,UINT);
    using Present1=HRESULT (STDMETHODCALLTYPE*)(IDXGISwapChain1*,UINT,UINT,const DXGI_PRESENT_PARAMETERS*);
    using Resize1=HRESULT (STDMETHODCALLTYPE*)(IDXGISwapChain3*,UINT,UINT,UINT,DXGI_FORMAT,UINT,const UINT*,IUnknown* const*);
    static inline DxgiHook* instance_=nullptr;
    static inline thread_local bool presenting_=false;
    struct Guard {bool previous=presenting_;Guard(){presenting_=true;}~Guard(){presenting_=previous;}};
    Present1 originalPresent1_=nullptr;
    Resize1 originalResize1_=nullptr;
    Present originalPresent_=nullptr;
    Resize originalResize_=nullptr;
    SharedScene& shared_;
    RenderApiRouter& router_;
    std::atomic<bool> enabled_{false};
    bool installed_=false,attempted_=false;
    std::recursive_mutex renderMutex_;
    IDXGISwapChain* selected_=nullptr; // identity only, not a retained game object
    HWND selectedWindow_=nullptr;
    std::vector<std::unique_ptr<DxgiPresenter>> presenters_;
    std::vector<RenderApi> apis_;
    DxgiPresenter* presenter_=nullptr;
    std::chrono::steady_clock::time_point lastFrame_;
    void clearRenderer() noexcept {
        if(presenter_) {auto api=presenter_->api();presenter_->releaseDevice();router_.release(api);}
        presenter_=nullptr;selected_=nullptr;selectedWindow_=nullptr;
    }
    bool render(IDXGISwapChain* swap) {
        std::lock_guard lock(renderMutex_);
        if(!enabled_.load()) return false;
        DXGI_SWAP_CHAIN_DESC desc{};
        if(FAILED(swap->GetDesc(&desc)) || !IsWindowVisible(desc.OutputWindow)) return false;
        auto target=shared_.targetWindow.load();
        if(target && reinterpret_cast<uintptr_t>(desc.OutputWindow)!=target) return false;
        if(selected_ && selected_!=swap) {
            // Fullscreen/mode changes can replace both swap chain and device. Never
            // dereference the previous chain: it may already have been destroyed.
            if(desc.OutputWindow!=selectedWindow_) return false;
            const auto candidates=router_.candidates();
            bool supported=std::any_of(presenters_.begin(),presenters_.end(),[&](auto& candidate){
                return std::find(candidates.begin(),candidates.end(),candidate->api())!=candidates.end() && candidate->readyFor(swap);
            });
            if(!supported) return false;
            clearRenderer();
        }
        if(!presenter_) {
            for(auto& candidate:presenters_) {
                if(std::find(apis_.begin(),apis_.end(),candidate->api())!=apis_.end() && candidate->readyFor(swap) && router_.tryActivate(candidate->api())) {
                    presenter_=candidate.get();selected_=swap;selectedWindow_=desc.OutputWindow;lastFrame_=std::chrono::steady_clock::now();break;
                }
            }
            if(!presenter_) return false;
        }
        auto now=std::chrono::steady_clock::now();
        auto delta=std::chrono::duration<float>(now-lastFrame_).count();lastFrame_=now;
        Scene scene;std::shared_ptr<const ImageAssets> images;
        {std::lock_guard shared(shared_.mutex);scene=shared_.scene;images=shared_.images;}
        auto before=presenter_->submittedFrames();
        presenter_->render(swap,scene,*images,delta);
        if(presenter_->submittedFrames()!=before) {
            DXGI_SWAP_CHAIN_DESC desc{};
            if(SUCCEEDED(swap->GetDesc(&desc))) {
                auto& progress=shared_.progress;std::lock_guard status(progress.mutex);
                ++progress.submittedFrames;progress.window=reinterpret_cast<uintptr_t>(desc.OutputWindow);
                progress.lastFrameTick=GetTickCount64();progress.error.clear();
            }
        }
        return true;
    }
    void reportFailure(const char* error) {
        auto& progress=shared_.progress;std::lock_guard status(progress.mutex);
        progress.error=std::string(error).substr(0,512);progress.lastFrameTick=0;
    }
    static HRESULT STDMETHODCALLTYPE present(IDXGISwapChain* swap,UINT sync,UINT flags) {
        auto& self=*instance_;Guard guard;
        if(!guard.previous && !(flags&DXGI_PRESENT_TEST) && self.enabled_.load()) {
            try {self.render(swap);}
            catch(const std::exception& e){OutputDebugStringA(e.what());self.reportFailure(e.what());self.stop();}
        }
        auto result=self.originalPresent_(swap,sync,flags);
        if(result==DXGI_ERROR_DEVICE_REMOVED || result==DXGI_ERROR_DEVICE_RESET) {
            std::lock_guard lock(self.renderMutex_);
            if(self.selected_==swap) {self.reportFailure("DXGI device removed or reset");self.clearRenderer();}
        }
        return result;
    }
    static HRESULT STDMETHODCALLTYPE present1(IDXGISwapChain1* swap,UINT sync,UINT flags,const DXGI_PRESENT_PARAMETERS* params) {
        auto& self=*instance_;Guard guard;bool overlaid=false;
        if(!guard.previous && !(flags&DXGI_PRESENT_TEST) && self.enabled_.load()) {
            try{overlaid=self.render(swap);}catch(const std::exception& e){OutputDebugStringA(e.what());self.reportFailure(e.what());self.stop();}
        }
        DXGI_PRESENT_PARAMETERS fullFrame{}; // HUD changes may fall outside the game's dirty rectangles.
        auto result=self.originalPresent1_(swap,sync,flags,overlaid?&fullFrame:params);
        if(result==DXGI_ERROR_DEVICE_REMOVED || result==DXGI_ERROR_DEVICE_RESET){std::lock_guard lock(self.renderMutex_);if(self.selected_==swap) {self.reportFailure("DXGI device removed or reset");self.clearRenderer();}}
        return result;
    }
    static HRESULT STDMETHODCALLTYPE resize1(IDXGISwapChain3* swap,UINT count,UINT width,UINT height,DXGI_FORMAT format,UINT flags,const UINT* masks,IUnknown* const* queues) {
        auto& self=*instance_;std::lock_guard lock(self.renderMutex_);
        try{if(self.selected_==swap && self.presenter_) self.presenter_->beforeResize();}catch(const std::exception& e){OutputDebugStringA(e.what());self.reportFailure(e.what());self.enabled_=false;self.clearRenderer();}
        auto result=self.originalResize1_(swap,count,width,height,format,flags,masks,queues);
#ifdef ZTMZ_HAS_DX12
        if(SUCCEEDED(result) && queues) {
            DXGI_SWAP_CHAIN_DESC desc{};swap->GetDesc(&desc);bool same=true;
            for(UINT i=1;i<desc.BufferCount;++i) if(queues[i]!=queues[0]) same=false;
            setDx12QueueFor(swap,same?queues[0]:nullptr);
        }
#endif
        return result;
    }
    static HRESULT STDMETHODCALLTYPE resize(IDXGISwapChain* swap,UINT count,UINT width,UINT height,DXGI_FORMAT format,UINT flags) {
        auto& self=*instance_;std::lock_guard lock(self.renderMutex_);
        try {if(self.selected_==swap && self.presenter_) self.presenter_->beforeResize();}
        catch(const std::exception& e){OutputDebugStringA(e.what());self.reportFailure(e.what());self.enabled_.store(false);self.clearRenderer();}
        return self.originalResize_(swap,count,width,height,format,flags);
    }
public:
    DxgiHook(SharedScene& shared,RenderApiRouter& router,std::vector<std::unique_ptr<DxgiPresenter>> presenters)
        :shared_(shared),router_(router),presenters_(std::move(presenters)) {
        for(const auto& presenter:presenters_) apis_.push_back(presenter->api());
    }
    std::span<const RenderApi> apis() const noexcept override {return apis_;}
    void enable() noexcept override {enabled_.store(true);}
    void stop() noexcept override {
        enabled_.store(false);std::lock_guard lock(renderMutex_);clearRenderer();
    }
    void install() override {
        const auto candidates=router_.candidates();
        for(auto& presenter:presenters_) if(std::find(candidates.begin(),candidates.end(),presenter->api())!=candidates.end()) {
            try{presenter->installAuxiliaryHooks();}
            catch(const std::exception& e){OutputDebugStringA(e.what());std::erase(apis_,presenter->api());}
        }
        if(std::none_of(apis_.begin(),apis_.end(),[&](auto api){return std::find(candidates.begin(),candidates.end(),api)!=candidates.end();})) throw std::runtime_error("No DXGI presenter could install its required hooks");
        if(installed_) return;
        if(attempted_) throw std::runtime_error("DXGI hook installation previously failed; restart the process before retrying");
        attempted_=true;instance_=this;
        struct Window {
            HWND handle=CreateWindowExW(0,L"STATIC",L"ZTMZ DXGI probe",WS_OVERLAPPEDWINDOW,0,0,8,8,nullptr,nullptr,GetModuleHandleW(nullptr),nullptr);
            ~Window(){if(handle) DestroyWindow(handle);}
        } window;
        if(!window.handle) throw std::runtime_error("Cannot create DXGI probe window");
        DXGI_SWAP_CHAIN_DESC desc{};desc.BufferCount=1;desc.BufferDesc.Format=DXGI_FORMAT_R8G8B8A8_UNORM;
        desc.BufferUsage=DXGI_USAGE_RENDER_TARGET_OUTPUT;desc.OutputWindow=window.handle;desc.SampleDesc.Count=1;desc.Windowed=TRUE;
        ComPtr<ID3D11Device> device;ComPtr<ID3D11DeviceContext> context;ComPtr<IDXGISwapChain> swap;
        // This probe discovers DXGI methods only. It never claims the active API.
        auto probe=D3D11CreateDeviceAndSwapChain(nullptr,D3D_DRIVER_TYPE_HARDWARE,nullptr,0,nullptr,0,D3D11_SDK_VERSION,&desc,&swap,&device,nullptr,&context);
        if(FAILED(probe)) probe=D3D11CreateDeviceAndSwapChain(nullptr,D3D_DRIVER_TYPE_WARP,nullptr,0,nullptr,0,D3D11_SDK_VERSION,&desc,&swap,&device,nullptr,&context);
        if(FAILED(probe)) throw std::runtime_error("Cannot create DXGI probe swap chain");
        auto result=MH_Initialize();
        if(result!=MH_OK && result!=MH_ERROR_ALREADY_INITIALIZED) throw std::runtime_error("MinHook initialization failed");
        std::vector<void*> addresses;
        auto hook=[&](void* target,void* callback,void** original){if(MH_CreateHook(target,callback,original)!=MH_OK) throw std::runtime_error("Cannot create DXGI hook");addresses.push_back(target);};
        try {
            auto** methods=*reinterpret_cast<void***>(swap.Get());
            hook(methods[8],reinterpret_cast<void*>(&present),reinterpret_cast<void**>(&originalPresent_));
            hook(methods[13],reinterpret_cast<void*>(&resize),reinterpret_cast<void**>(&originalResize_));
            ComPtr<IDXGISwapChain1> swap1;
            if(SUCCEEDED(swap.As(&swap1))){auto** v=*reinterpret_cast<void***>(swap1.Get());hook(v[22],reinterpret_cast<void*>(&present1),reinterpret_cast<void**>(&originalPresent1_));}
            ComPtr<IDXGISwapChain3> swap3;
            if(SUCCEEDED(swap.As(&swap3))){auto** v=*reinterpret_cast<void***>(swap3.Get());hook(v[39],reinterpret_cast<void*>(&resize1),reinterpret_cast<void**>(&originalResize1_));}
            for(auto target:addresses) if(MH_QueueEnableHook(target)!=MH_OK) throw std::runtime_error("Cannot queue DXGI hook");
            if(MH_ApplyQueued()!=MH_OK) throw std::runtime_error("Cannot enable DXGI hooks");
        } catch(...){for(auto target:addresses){MH_QueueDisableHook(target);MH_DisableHook(target);}throw;}
        installed_=true;
    }
};
}
std::unique_ptr<OverlayHook> createDxgiHook(SharedScene& shared,RenderApiRouter& router,std::vector<std::unique_ptr<DxgiPresenter>> presenters) {
    return std::make_unique<DxgiHook>(shared,router,std::move(presenters));
}
}
