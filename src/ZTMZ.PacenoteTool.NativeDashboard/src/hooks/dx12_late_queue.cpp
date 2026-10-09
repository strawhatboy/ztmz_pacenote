#include "hooks/dx12_late_queue.hpp"
#include <dxgi1_4.h>
#include <MinHook.h>
#include <array>
#include <algorithm>
#include <mutex>
#include <vector>
#include <stdexcept>
#include <atomic>
#include <thread>
namespace ztmz {
namespace {
using Microsoft::WRL::ComPtr;
using Execute=void(STDMETHODCALLTYPE*)(ID3D12CommandQueue*,UINT,ID3D12CommandList* const*);
struct State {
    std::mutex mutex;
    std::atomic<bool> started{false};
    Execute original=nullptr;
    std::vector<size_t> offsets;
    std::vector<ComPtr<ID3D12CommandQueue>> candidates;
    HMODULE dxgi=nullptr;
    std::atomic<bool> ready{false};
};
State& state(){static auto* s=new State;return *s;}
bool sameObject(IUnknown* a,IUnknown* b) {
    ComPtr<IUnknown> x,y;
    return a&&b&&SUCCEEDED(a->QueryInterface(IID_PPV_ARGS(&x)))&&
        SUCCEEDED(b->QueryInterface(IID_PPV_ARGS(&y)))&&x.Get()==y.Get();
}
bool readPointer(const void* base,size_t offset,uintptr_t& value) {
    SIZE_T bytes=0;
    return ReadProcessMemory(GetCurrentProcess(),reinterpret_cast<const BYTE*>(base)+offset,
        &value,sizeof(value),&bytes)&&bytes==sizeof(value);
}
HMODULE ownerOfVtable(IUnknown* object) {
    uintptr_t vtable=0;HMODULE owner=nullptr;
    if(readPointer(object,0,vtable)) GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|
        GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,reinterpret_cast<LPCWSTR>(vtable),&owner);
    return owner;
}
void STDMETHODCALLTYPE execute(ID3D12CommandQueue* queue,UINT count,ID3D12CommandList* const* lists) {
    auto& s=state();
    // Observe genuine application submissions, not pointers obtained by scanning.
    try {
        if(count&&queue->GetDesc().Type==D3D12_COMMAND_LIST_TYPE_DIRECT) {
            std::lock_guard lock(s.mutex);
            auto found=std::find_if(s.candidates.begin(),s.candidates.end(),[&](auto& q){return q.Get()==queue;});
            if(found==s.candidates.end()) {
                if(s.candidates.size()==64) s.candidates.erase(s.candidates.begin());
                s.candidates.emplace_back(queue);
            }
        }
    } catch(...) {} // Never let observation failure affect the game's submission.
    s.original(queue,count,lists);
}
void check(HRESULT hr){if(FAILED(hr)) throw std::runtime_error("DX12 late queue calibration failed");}
void initialize(ID3D12Device* device) {
    auto& s=state();
    // Failed calibration is terminal for this process, not a repeated frame stall.
    try {
        ComPtr<IDXGIFactory4> factory;check(CreateDXGIFactory1(IID_PPV_ARGS(&factory)));
        std::array<ComPtr<ID3D12CommandQueue>,2> queues;
        D3D12_COMMAND_QUEUE_DESC q{};q.Type=D3D12_COMMAND_LIST_TYPE_DIRECT;
        for(auto& queue:queues) check(device->CreateCommandQueue(&q,IID_PPV_ARGS(&queue)));
        struct Windows {
            HWND a=CreateWindowExW(0,L"STATIC",L"ZTMZ queue probe",WS_OVERLAPPEDWINDOW,-30000,-30000,128,128,nullptr,nullptr,GetModuleHandleW(nullptr),nullptr);
            HWND b=CreateWindowExW(0,L"STATIC",L"ZTMZ queue probe",WS_OVERLAPPEDWINDOW,-30000,-30000,128,128,nullptr,nullptr,GetModuleHandleW(nullptr),nullptr);
            ~Windows(){if(a)DestroyWindow(a);if(b)DestroyWindow(b);}
        } windows;
        std::array<ComPtr<IDXGISwapChain1>,2> chains;
        DXGI_SWAP_CHAIN_DESC1 desc{};desc.Width=64;desc.Height=64;desc.Format=DXGI_FORMAT_R8G8B8A8_UNORM;
        desc.SampleDesc.Count=1;desc.BufferCount=2;desc.BufferUsage=DXGI_USAGE_RENDER_TARGET_OUTPUT;desc.SwapEffect=DXGI_SWAP_EFFECT_FLIP_DISCARD;
        check(factory->CreateSwapChainForHwnd(queues[0].Get(),windows.a,&desc,nullptr,nullptr,&chains[0]));
        check(factory->CreateSwapChainForHwnd(queues[1].Get(),windows.b,&desc,nullptr,nullptr,&chains[1]));
        // Normalize the interface view used in both calibration and lookup.
        std::array<ComPtr<IDXGISwapChain>,2> base;
        check(chains[0].As(&base[0]));check(chains[1].As(&base[1]));
        for(size_t offset=sizeof(void*);offset<512*sizeof(void*);offset+=sizeof(void*)) {
            uintptr_t first=0,second=0;
            if(readPointer(base[0].Get(),offset,first)&&readPointer(base[1].Get(),offset,second)&&
                first==reinterpret_cast<uintptr_t>(queues[0].Get())&&second==reinterpret_cast<uintptr_t>(queues[1].Get()))
                s.offsets.push_back(offset);
        }
        // A single incidental pointer is insufficient evidence for this fallback.
        if(s.offsets.size()<2) return;
        s.dxgi=ownerOfVtable(base[0].Get());
        if(!s.dxgi||s.dxgi!=ownerOfVtable(base[1].Get())) return;
        auto status=MH_Initialize();if(status!=MH_OK&&status!=MH_ERROR_ALREADY_INITIALIZED) return;
        auto target=(*reinterpret_cast<void***>(queues[0].Get()))[10];
        if(MH_CreateHook(target,reinterpret_cast<void*>(&execute),reinterpret_cast<void**>(&s.original))!=MH_OK) return;
        if(MH_EnableHook(target)!=MH_OK) {MH_RemoveHook(target);return;}
        s.ready=true;
    } catch(...) {OutputDebugStringA("ZTMZ: DX12 late queue calibration unavailable\n");}
}
}
ComPtr<ID3D12CommandQueue> findLateDx12Queue(IDXGISwapChain* swap) {
    ComPtr<ID3D12Device> device;ComPtr<IDXGISwapChain> base;
    if(!swap||FAILED(swap->GetDevice(IID_PPV_ARGS(&device)))||FAILED(swap->QueryInterface(IID_PPV_ARGS(&base)))) return {};
    auto& s=state();
    if(!s.started.exchange(true)) {
        try {std::thread([device]{initialize(device.Get());}).detach();}
        catch(...) {s.started=false;throw;}
    }
    if(!s.ready||ownerOfVtable(base.Get())!=s.dxgi||FAILED(device->GetDeviceRemovedReason())) return {};
    // Every calibrated slot must agree; never chase or call an untrusted pointer.
    uintptr_t expected=0;
    for(auto offset:s.offsets) {
        uintptr_t value=0;if(!readPointer(base.Get(),offset,value)||!value||(expected&&value!=expected)) return {};
        expected=value;
    }
    ComPtr<ID3D12CommandQueue> result;
    {
        std::lock_guard lock(s.mutex);
        for(auto& queue:s.candidates) if(reinterpret_cast<uintptr_t>(queue.Get())==expected) {result=queue;break;}
    }
    ComPtr<ID3D12Device> queueDevice;
    if(!result||FAILED(result->GetDevice(IID_PPV_ARGS(&queueDevice)))||!sameObject(device.Get(),queueDevice.Get())) return {};
    return result;
}
}
