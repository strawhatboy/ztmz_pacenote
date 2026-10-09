#include "hooks/dx12_queue.hpp"
#include "hooks/dx12_late_queue.hpp"
#include <dxgi1_2.h>
#include <MinHook.h>
#include <mutex>
#include <stdexcept>
namespace ztmz {
namespace {
using Microsoft::WRL::ComPtr;
const GUID queueKey={0xa47e6321,0x69f3,0x4ee0,{0xa6,0x8a,0x96,0x0a,0x23,0x65,0x81,0x42}};
using Create=HRESULT (STDMETHODCALLTYPE*)(IDXGIFactory*,IUnknown*,DXGI_SWAP_CHAIN_DESC*,IDXGISwapChain**);
using Hwnd=HRESULT (STDMETHODCALLTYPE*)(IDXGIFactory2*,IUnknown*,HWND,const DXGI_SWAP_CHAIN_DESC1*,const DXGI_SWAP_CHAIN_FULLSCREEN_DESC*,IDXGIOutput*,IDXGISwapChain1**);
using Core=HRESULT (STDMETHODCALLTYPE*)(IDXGIFactory2*,IUnknown*,IUnknown*,const DXGI_SWAP_CHAIN_DESC1*,IDXGIOutput*,IDXGISwapChain1**);
using Composition=HRESULT (STDMETHODCALLTYPE*)(IDXGIFactory2*,IUnknown*,const DXGI_SWAP_CHAIN_DESC1*,IDXGIOutput*,IDXGISwapChain1**);
Create originalCreate=nullptr;Hwnd originalHwnd=nullptr;Core originalCore=nullptr;Composition originalComposition=nullptr;
void attach(IUnknown* device,IDXGISwapChain* swap) noexcept {
    ComPtr<ID3D12CommandQueue> queue;
    if(device && swap && SUCCEEDED(device->QueryInterface(IID_PPV_ARGS(&queue))) && queue->GetDesc().Type==D3D12_COMMAND_LIST_TYPE_DIRECT)
        swap->SetPrivateDataInterface(queueKey,queue.Get()); // lifetime follows the actual swap chain
}
HRESULT STDMETHODCALLTYPE create(IDXGIFactory* f,IUnknown* d,DXGI_SWAP_CHAIN_DESC* desc,IDXGISwapChain** out){auto r=originalCreate(f,d,desc,out);if(SUCCEEDED(r)) attach(d,*out);return r;}
HRESULT STDMETHODCALLTYPE hwnd(IDXGIFactory2* f,IUnknown* d,HWND w,const DXGI_SWAP_CHAIN_DESC1* desc,const DXGI_SWAP_CHAIN_FULLSCREEN_DESC* full,IDXGIOutput* restrictOutput,IDXGISwapChain1** out){auto r=originalHwnd(f,d,w,desc,full,restrictOutput,out);if(SUCCEEDED(r)) attach(d,*out);return r;}
HRESULT STDMETHODCALLTYPE core(IDXGIFactory2* f,IUnknown* d,IUnknown* w,const DXGI_SWAP_CHAIN_DESC1* desc,IDXGIOutput* restrictOutput,IDXGISwapChain1** out){auto r=originalCore(f,d,w,desc,restrictOutput,out);if(SUCCEEDED(r)) attach(d,*out);return r;}
HRESULT STDMETHODCALLTYPE composition(IDXGIFactory2* f,IUnknown* d,const DXGI_SWAP_CHAIN_DESC1* desc,IDXGIOutput* restrictOutput,IDXGISwapChain1** out){auto r=originalComposition(f,d,desc,restrictOutput,out);if(SUCCEEDED(r)) attach(d,*out);return r;}
}
void setDx12QueueFor(IDXGISwapChain* swap,IUnknown* queue) noexcept {
    if(!queue) swap->SetPrivateDataInterface(queueKey,nullptr);else attach(queue,swap);
}
void installDx12QueueCapture() {
    static std::once_flag once;
    std::call_once(once,[]{
        ComPtr<IDXGIFactory2> factory;if(FAILED(CreateDXGIFactory1(IID_PPV_ARGS(&factory)))) throw std::runtime_error("Cannot create DXGI factory for queue tracking");
        auto init=MH_Initialize();if(init!=MH_OK && init!=MH_ERROR_ALREADY_INITIALIZED) throw std::runtime_error("MinHook initialization failed");
        auto** v=*reinterpret_cast<void***>(factory.Get());
        void* targets[]{v[10],v[15],v[16],v[24]};
        void* callbacks[]{reinterpret_cast<void*>(&create),reinterpret_cast<void*>(&hwnd),reinterpret_cast<void*>(&core),reinterpret_cast<void*>(&composition)};
        void** originals[]{reinterpret_cast<void**>(&originalCreate),reinterpret_cast<void**>(&originalHwnd),reinterpret_cast<void**>(&originalCore),reinterpret_cast<void**>(&originalComposition)};
        int created=0;
        try {
            for(int i=0;i<4;++i){if(MH_CreateHook(targets[i],callbacks[i],originals[i])!=MH_OK) throw std::runtime_error("Cannot hook DXGI swap-chain creation");++created;}
            for(auto target:targets) if(MH_QueueEnableHook(target)!=MH_OK) throw std::runtime_error("Cannot queue swap-chain creation hook");
            if(MH_ApplyQueued()!=MH_OK) throw std::runtime_error("Cannot enable swap-chain creation hooks");
        } catch(...) {for(int i=0;i<created;++i){MH_QueueDisableHook(targets[i]);MH_DisableHook(targets[i]);}throw;}

    });
}
ComPtr<ID3D12CommandQueue> dx12QueueFor(IDXGISwapChain* swap) {
    ComPtr<ID3D12CommandQueue> queue;UINT size=sizeof(ID3D12CommandQueue*);
    if(FAILED(swap->GetPrivateData(queueKey,&size,queue.GetAddressOf()))) return findLateDx12Queue(swap);
    return queue;
}
const char* dx12QueueSource(IDXGISwapChain* swap) {
    ComPtr<ID3D12CommandQueue> queue;UINT size=sizeof(ID3D12CommandQueue*);
    return SUCCEEDED(swap->GetPrivateData(queueKey,&size,queue.GetAddressOf()))&&queue?
        "dxgi-creation-or-resize":"calibrated-private-layout";
}
}
