// Diagnostic only: evaluate private-layout discovery without using it to render.
// No code from this target is linked into the shipped overlay.
#include <windows.h>
#include <d3d12.h>
#include <dxgi1_4.h>
#include <wrl/client.h>
#include <array>
#include <iostream>
#include <stdexcept>
#include <vector>
#include "hooks/dx12_late_queue.hpp"
using Microsoft::WRL::ComPtr;
namespace {
void check(HRESULT hr,const char* message){if(FAILED(hr)) throw std::runtime_error(message);}
struct Window {
    HWND value=CreateWindowExW(0,L"STATIC",L"DX12 association probe",WS_OVERLAPPEDWINDOW,
        -30000,-30000,320,240,nullptr,nullptr,GetModuleHandleW(nullptr),nullptr);
    ~Window(){if(value) DestroyWindow(value);}
};
std::vector<size_t> matches(IDXGISwapChain* chain,ID3D12CommandQueue* queue) {
    std::vector<size_t> offsets;
    // Copy one pointer at a time through the OS; never dereference private memory.
    for(size_t slot=0;slot<512;++slot) {
        uintptr_t value=0;SIZE_T read=0;
        if(!ReadProcessMemory(GetCurrentProcess(),reinterpret_cast<const BYTE*>(chain)+slot*sizeof(value),
            &value,sizeof(value),&read) || read!=sizeof(value)) break;
        if(value==reinterpret_cast<uintptr_t>(queue)) offsets.push_back(slot*sizeof(value));
    }
    return offsets;
}
}
int wmain(int argc,wchar_t** argv) {
    try {
        bool warp=argc<2 || std::wstring_view(argv[1])!=L"hardware";
        ComPtr<IDXGIFactory4> factory;check(CreateDXGIFactory1(IID_PPV_ARGS(&factory)),"Factory");
        ComPtr<IDXGIAdapter1> adapter;
        if(warp) check(factory->EnumWarpAdapter(IID_PPV_ARGS(&adapter)),"WARP adapter");
        else check(factory->EnumAdapters1(0,&adapter),"Hardware adapter");
        DXGI_ADAPTER_DESC1 adapterDesc{};adapter->GetDesc1(&adapterDesc);
        std::wcout<<L"Adapter: "<<adapterDesc.Description<<L'\n';
        ComPtr<ID3D12Device> device;check(D3D12CreateDevice(adapter.Get(),D3D_FEATURE_LEVEL_11_0,IID_PPV_ARGS(&device)),"Device");
        std::array<ComPtr<ID3D12CommandQueue>,3> queues;
        D3D12_COMMAND_QUEUE_DESC q{};q.Type=D3D12_COMMAND_LIST_TYPE_DIRECT;
        for(auto& queue:queues) check(device->CreateCommandQueue(&q,IID_PPV_ARGS(&queue)),"Queue");
        Window windows[2];std::array<ComPtr<IDXGISwapChain1>,2> chains;
        DXGI_SWAP_CHAIN_DESC1 desc{};desc.Width=64;desc.Height=64;desc.Format=DXGI_FORMAT_R8G8B8A8_UNORM;
        desc.BufferUsage=DXGI_USAGE_RENDER_TARGET_OUTPUT;desc.BufferCount=2;
        desc.SampleDesc.Count=1;desc.SwapEffect=DXGI_SWAP_EFFECT_FLIP_DISCARD;
        for(size_t i=0;i<chains.size();++i)
            check(factory->CreateSwapChainForHwnd(queues[i].Get(),windows[i].value,&desc,nullptr,nullptr,&chains[i]),"Swapchain");
        auto inspect=[&](const char* phase) {
            for(size_t i=0;i<chains.size();++i) for(size_t j=0;j<queues.size();++j) {
                auto offsets=matches(chains[i].Get(),queues[j].Get());
                std::cout<<phase<<" chain="<<i<<" candidate="<<j<<" matches="<<offsets.size();
                for(auto offset:offsets) std::cout<<" +0x"<<std::hex<<offset<<std::dec;
                std::cout<<'\n';
                if((i==j)!=!offsets.empty()) throw std::runtime_error("Private-layout association missing or false positive");
            }
        };
        inspect("created-before-discovery");
        for(auto& chain:chains) check(chain->ResizeBuffers(2,128,96,DXGI_FORMAT_UNKNOWN,0),"Resize");
        inspect("after-resize");
        if(ztmz::findLateDx12Queue(chains[0].Get())) throw std::runtime_error("Queue accepted before observation");
        ComPtr<ID3D12CommandAllocator> allocator;ComPtr<ID3D12GraphicsCommandList> list;
        check(device->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT,IID_PPV_ARGS(&allocator)),"Allocator");
        check(device->CreateCommandList(0,D3D12_COMMAND_LIST_TYPE_DIRECT,allocator.Get(),nullptr,IID_PPV_ARGS(&list)),"List");
        check(list->Close(),"Close list");
        auto submit=[&](ID3D12CommandQueue* queue) {
            ID3D12CommandList* lists[]{list.Get()};queue->ExecuteCommandLists(1,lists);
            ComPtr<ID3D12Fence> fence;check(device->CreateFence(0,D3D12_FENCE_FLAG_NONE,IID_PPV_ARGS(&fence)),"Fence");
            check(queue->Signal(fence.Get(),1),"Signal");
            auto event=CreateEventW(nullptr,FALSE,FALSE,nullptr);
            if(!event) throw std::runtime_error("Event");
            auto hr=fence->SetEventOnCompletion(1,event);
            auto wait=SUCCEEDED(hr)?WaitForSingleObject(event,5000):WAIT_FAILED;
            CloseHandle(event);
            if(wait!=WAIT_OBJECT_0) throw std::runtime_error("GPU completion");
        };
        submit(queues[2].Get());
        if(ztmz::findLateDx12Queue(chains[0].Get())) throw std::runtime_error("Unrelated direct queue selected");
        submit(queues[1].Get());
        if(ztmz::findLateDx12Queue(chains[0].Get())) throw std::runtime_error("Other swapchain queue selected");
        if(ztmz::findLateDx12Queue(chains[1].Get()).Get()!=queues[1].Get()) throw std::runtime_error("Second swapchain discovery failed");
        submit(queues[0].Get());
        if(ztmz::findLateDx12Queue(chains[0].Get()).Get()!=queues[0].Get()) throw std::runtime_error("First swapchain discovery failed");
        ComPtr<IDXGIAdapter1> otherAdapter;
        if(warp) check(factory->EnumAdapters1(0,&otherAdapter),"Other hardware adapter");
        else check(factory->EnumWarpAdapter(IID_PPV_ARGS(&otherAdapter)),"Other WARP adapter");
        ComPtr<ID3D12Device> otherDevice;check(D3D12CreateDevice(otherAdapter.Get(),D3D_FEATURE_LEVEL_11_0,IID_PPV_ARGS(&otherDevice)),"Other device");
        ComPtr<ID3D12CommandQueue> otherQueue;check(otherDevice->CreateCommandQueue(&q,IID_PPV_ARGS(&otherQueue)),"Other device queue");
        // Exercise a different device independently; its queue must never replace
        // an association already established on this device.
        ComPtr<ID3D12CommandAllocator> otherAllocator;ComPtr<ID3D12GraphicsCommandList> otherList;
        check(otherDevice->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT,IID_PPV_ARGS(&otherAllocator)),"Other allocator");
        check(otherDevice->CreateCommandList(0,D3D12_COMMAND_LIST_TYPE_DIRECT,otherAllocator.Get(),nullptr,IID_PPV_ARGS(&otherList)),"Other list");
        check(otherList->Close(),"Other close");
        ID3D12CommandList* otherLists[]{otherList.Get()};otherQueue->ExecuteCommandLists(1,otherLists);
        ComPtr<ID3D12Fence> otherFence;check(otherDevice->CreateFence(0,D3D12_FENCE_FLAG_NONE,IID_PPV_ARGS(&otherFence)),"Other fence");
        check(otherQueue->Signal(otherFence.Get(),1),"Other signal");
        auto otherEvent=CreateEventW(nullptr,FALSE,FALSE,nullptr);
        if(!otherEvent) throw std::runtime_error("Other event");
        auto otherHr=otherFence->SetEventOnCompletion(1,otherEvent);
        auto otherWait=SUCCEEDED(otherHr)?WaitForSingleObject(otherEvent,5000):WAIT_FAILED;
        CloseHandle(otherEvent);
        if(otherWait!=WAIT_OBJECT_0) throw std::runtime_error("Other GPU completion");
        if(ztmz::findLateDx12Queue(chains[0].Get()).Get()!=queues[0].Get()) throw std::runtime_error("Other device changed association");
        chains[0].Reset();
        desc.BufferCount=3;
        check(factory->CreateSwapChainForHwnd(queues[2].Get(),windows[0].value,&desc,nullptr,nullptr,&chains[0]),"Replace swapchain");
        if(ztmz::findLateDx12Queue(chains[0].Get()).Get()!=queues[2].Get()) throw std::runtime_error("Replacement retained old queue");
        std::cout<<"Dynamic hook: unrelated queues rejected, both queues resolved, replacement resolved.\n";
        // Demonstrate why a bounded readable-region scan is still not an object
        // ownership contract: memory beyond a COM interface has no public layout.
        std::cout<<"Probe passed in this runtime only; not a production support guarantee.\n";
        return 0;
    } catch(const std::exception& e) {std::cerr<<e.what()<<'\n';return 1;}
}
