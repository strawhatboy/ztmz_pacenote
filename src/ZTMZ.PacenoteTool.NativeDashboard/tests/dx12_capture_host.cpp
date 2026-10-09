#include <windows.h>
#include <d3d12.h>
#include <dxgi1_4.h>
#include <wrl/client.h>
#include <iostream>
#include <stdexcept>
using Microsoft::WRL::ComPtr;
namespace {
void check(HRESULT value,const char* message) {if(FAILED(value)) throw std::runtime_error(message);}
void require(bool value,const char* message) {if(!value) throw std::runtime_error(message);}
const GUID queueKey={0xa47e6321,0x69f3,0x4ee0,{0xa6,0x8a,0x96,0x0a,0x23,0x65,0x81,0x42}};
struct Window {
    HWND value=CreateWindowExW(0,L"STATIC",L"ZTMZ DX12 capture test",WS_OVERLAPPEDWINDOW,
        -30000,-30000,320,240,nullptr,nullptr,GetModuleHandleW(nullptr),nullptr);
    ~Window(){if(value) DestroyWindow(value);}
};
ComPtr<ID3D12CommandQueue> captured(IDXGISwapChain* swap) {
    ComPtr<ID3D12CommandQueue> result;UINT size=sizeof(ID3D12CommandQueue*);
    swap->GetPrivateData(queueKey,&size,result.GetAddressOf());return result;
}
}
int wmain(int argc,wchar_t** argv) {
    try {
        require(argc==2,"Expected production overlay path");
        ComPtr<IDXGIFactory4> factory;check(CreateDXGIFactory1(IID_PPV_ARGS(&factory)),"Factory");
        ComPtr<IDXGIAdapter> adapter;check(factory->EnumWarpAdapter(IID_PPV_ARGS(&adapter)),"WARP adapter");
        ComPtr<ID3D12Device> device;
        check(D3D12CreateDevice(adapter.Get(),D3D_FEATURE_LEVEL_11_0,IID_PPV_ARGS(&device)),"DX12 device");
        D3D12_COMMAND_QUEUE_DESC q{};q.Type=D3D12_COMMAND_LIST_TYPE_DIRECT;
        ComPtr<ID3D12CommandQueue> first,second;
        check(device->CreateCommandQueue(&q,IID_PPV_ARGS(&first)),"First queue");
        check(device->CreateCommandQueue(&q,IID_PPV_ARGS(&second)),"Second queue");
        Window a,b;require(a.value&&b.value,"Windows");
        DXGI_SWAP_CHAIN_DESC1 desc{};desc.Width=64;desc.Height=64;desc.Format=DXGI_FORMAT_R8G8B8A8_UNORM;
        desc.BufferUsage=DXGI_USAGE_RENDER_TARGET_OUTPUT;desc.BufferCount=2;
        desc.SampleDesc.Count=1;desc.SwapEffect=DXGI_SWAP_EFFECT_FLIP_DISCARD;
        ComPtr<IDXGISwapChain1> before;
        check(factory->CreateSwapChainForHwnd(first.Get(),a.value,&desc,nullptr,nullptr,&before),"Preexisting swapchain");
        auto dll=LoadLibraryExW(argv[1],nullptr,LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR|LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
        require(dll!=nullptr,"Load overlay");
        auto prepare=reinterpret_cast<BOOL(WINAPI*)(UINT)>(GetProcAddress(dll,"ZtmzDashboardPrepare"));
        auto stop=reinterpret_cast<void(WINAPI*)()>(GetProcAddress(dll,"ZtmzDashboardStop"));
        require(prepare&&stop,"Lifecycle exports");
        require(!prepare(99),"Unknown preparation ABI accepted");
        require(prepare(1)&&prepare(1),"Capture preparation/idempotence");
        require(!captured(before.Get()),"Preexisting queue must not be guessed");
        ComPtr<IDXGISwapChain1> after;
        check(factory->CreateSwapChainForHwnd(second.Get(),b.value,&desc,nullptr,nullptr,&after),"Inactive capture");
        require(captured(after.Get()).Get()==second.Get(),"Captured wrong direct queue");
        stop(); // No Start/network/render was ever requested.
        require(captured(after.Get()).Get()==second.Get(),"Stop lost swapchain association");
        after.Reset();
        check(factory->CreateSwapChainForHwnd(first.Get(),b.value,&desc,nullptr,nullptr,&after),"Capture after stop");
        require(captured(after.Get()).Get()==first.Get(),"Capture stopped or retained stale queue");
        std::cout<<"DX12 inactive capture passed: ABI, idempotence, distinct queues, Stop and replacement.\n";
        return 0;
    } catch(const std::exception& e) {std::cerr<<e.what()<<'\n';return 1;}
}
