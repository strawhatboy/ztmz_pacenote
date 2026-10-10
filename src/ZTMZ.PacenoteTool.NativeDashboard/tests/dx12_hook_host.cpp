#include <windows.h>
#include <d3d12.h>
#include <dxgi1_4.h>
#include <wrl/client.h>
#include <iostream>
#include <stdexcept>
#include <string_view>
#include <string>
#include <chrono>
#include <algorithm>
#include "streamline_legacy_fixture.hpp"
using Microsoft::WRL::ComPtr;
namespace {
void check(HRESULT hr){if(FAILED(hr)) throw std::runtime_error("DX12 HRESULT "+std::to_string(hr));}
void require(bool ok,const char* msg){if(!ok)throw std::runtime_error(msg);}
void printStatus() {
    auto name=L"\\\\.\\pipe\\ztmz-dashboard-"+std::to_wstring(GetCurrentProcessId());
    auto pipe=CreateFileW(name.c_str(),GENERIC_READ|GENERIC_WRITE,0,nullptr,OPEN_EXISTING,0,nullptr);
    if(pipe==INVALID_HANDLE_VALUE)return;
    const std::string request=R"({"version":1,"command":"status"})";
    DWORD bytes=0;char buffer[4096]{};
    if(WriteFile(pipe,request.data(),static_cast<DWORD>(request.size()),&bytes,nullptr)&&ReadFile(pipe,buffer,sizeof(buffer),&bytes,nullptr)) {
        std::cerr<<std::string(buffer,bytes)<<std::endl;
        char ack=1;WriteFile(pipe,&ack,1,&bytes,nullptr);
    }
    CloseHandle(pipe);
}
void barrier(ID3D12GraphicsCommandList* l,ID3D12Resource* r,D3D12_RESOURCE_STATES a,D3D12_RESOURCE_STATES b){
    D3D12_RESOURCE_BARRIER v{};v.Type=D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;v.Transition={r,D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES,a,b};l->ResourceBarrier(1,&v);
}
}
int wmain(int argc,wchar_t** argv){
    try {
        require(argc>=2,"Expected fixture DLL");
        auto createDevice=argc>3?legacyStreamlineDeviceFactory(argv[3]):&D3D12CreateDevice;
        auto createFactory=argc>3?reinterpret_cast<HRESULT(WINAPI*)(REFIID,void**)>(GetProcAddress(GetModuleHandleW(L"sl.interposer.dll"),"CreateDXGIFactory1")):&CreateDXGIFactory1;
        ComPtr<IDXGIFactory4> factory;check(createFactory(IID_PPV_ARGS(&factory)));
        ComPtr<IDXGIAdapter1> adapter;
        if(argc>2&&std::wstring_view(argv[2])==L"hardware") check(factory->EnumAdapters1(0,&adapter));
        else check(factory->EnumWarpAdapter(IID_PPV_ARGS(&adapter)));
        ComPtr<ID3D12Device> device;check(createDevice(adapter.Get(),D3D_FEATURE_LEVEL_11_0,IID_PPV_ARGS(&device)));
        D3D12_COMMAND_QUEUE_DESC q{};q.Type=D3D12_COMMAND_LIST_TYPE_DIRECT;
        ComPtr<ID3D12CommandQueue> queue;check(device->CreateCommandQueue(&q,IID_PPV_ARGS(&queue)));
        HWND window=CreateWindowExW(WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE,L"STATIC",L"DX12 hook test",WS_POPUP|WS_VISIBLE,-30000,-30000,640,360,nullptr,nullptr,GetModuleHandleW(nullptr),nullptr);
        require(window!=nullptr,"Window");
        DXGI_SWAP_CHAIN_DESC1 desc{};desc.Width=640;desc.Height=360;desc.Format=DXGI_FORMAT_R8G8B8A8_UNORM;
        desc.SampleDesc.Count=1;desc.BufferCount=2;desc.BufferUsage=DXGI_USAGE_RENDER_TARGET_OUTPUT;desc.SwapEffect=DXGI_SWAP_EFFECT_FLIP_DISCARD;
        ComPtr<IDXGISwapChain1> swap;check(factory->CreateSwapChainForHwnd(queue.Get(),window,&desc,nullptr,nullptr,&swap));
        ComPtr<IDXGISwapChain3> modern;check(swap.As(&modern));
        ComPtr<ID3D12Fence> fence;check(device->CreateFence(0,D3D12_FENCE_FLAG_NONE,IID_PPV_ARGS(&fence)));
        HANDLE event=CreateEventW(nullptr,FALSE,FALSE,nullptr);require(event!=nullptr,"Event");UINT64 sequence=0;
        ComPtr<ID3D12CommandAllocator> allocator;check(device->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT,IID_PPV_ARGS(&allocator)));
        ComPtr<ID3D12GraphicsCommandList> list;check(device->CreateCommandList(0,D3D12_COMMAND_LIST_TYPE_DIRECT,allocator.Get(),nullptr,IID_PPV_ARGS(&list)));check(list->Close());
        auto begin=[&]{check(allocator->Reset());check(list->Reset(allocator.Get(),nullptr));};
        auto submit=[&]{check(list->Close());ID3D12CommandList* lists[]{list.Get()};queue->ExecuteCommandLists(1,lists);check(queue->Signal(fence.Get(),++sequence));check(fence->SetEventOnCompletion(sequence,event));require(WaitForSingleObject(event,5000)==WAIT_OBJECT_0,"GPU fence timeout");};
        D3D12_DESCRIPTOR_HEAP_DESC hd{};hd.Type=D3D12_DESCRIPTOR_HEAP_TYPE_RTV;hd.NumDescriptors=1;
        ComPtr<ID3D12DescriptorHeap> heap;check(device->CreateDescriptorHeap(&hd,IID_PPV_ARGS(&heap)));
        bool textured=false;
        bool measureText=false;
        double maxTextPresentMs=0;
        double maxStartupPresentMs=0;
        auto frame=[&](bool expected,bool waiting=false){
            ComPtr<ID3D12Resource> buffer;check(swap->GetBuffer(modern->GetCurrentBackBufferIndex(),IID_PPV_ARGS(&buffer)));
            auto rtv=heap->GetCPUDescriptorHandleForHeapStart();device->CreateRenderTargetView(buffer.Get(),nullptr,rtv);
            begin();barrier(list.Get(),buffer.Get(),D3D12_RESOURCE_STATE_PRESENT,D3D12_RESOURCE_STATE_RENDER_TARGET);
            float blue[]{0,0,1,1};list->ClearRenderTargetView(rtv,blue,0,nullptr);
            barrier(list.Get(),buffer.Get(),D3D12_RESOURCE_STATE_RENDER_TARGET,D3D12_RESOURCE_STATE_PRESENT);submit();
            auto presentBegin=std::chrono::steady_clock::now();
            check(swap->Present(0,0));
            if(waiting)maxStartupPresentMs=std::max(maxStartupPresentMs,std::chrono::duration<double,std::milli>(std::chrono::steady_clock::now()-presentBegin).count());
            if(measureText)maxTextPresentMs=std::max(maxTextPresentMs,std::chrono::duration<double,std::milli>(std::chrono::steady_clock::now()-presentBegin).count());
            auto resourceDesc=buffer->GetDesc();D3D12_PLACED_SUBRESOURCE_FOOTPRINT footprint{};UINT64 bytes=0;
            device->GetCopyableFootprints(&resourceDesc,0,1,0,&footprint,nullptr,nullptr,&bytes);
            D3D12_HEAP_PROPERTIES props{};props.Type=D3D12_HEAP_TYPE_READBACK;
            D3D12_RESOURCE_DESC rd{};rd.Dimension=D3D12_RESOURCE_DIMENSION_BUFFER;rd.Width=bytes;rd.Height=1;rd.DepthOrArraySize=rd.MipLevels=1;rd.SampleDesc.Count=1;rd.Layout=D3D12_TEXTURE_LAYOUT_ROW_MAJOR;
            ComPtr<ID3D12Resource> readback;check(device->CreateCommittedResource(&props,D3D12_HEAP_FLAG_NONE,&rd,D3D12_RESOURCE_STATE_COPY_DEST,nullptr,IID_PPV_ARGS(&readback)));
            begin();barrier(list.Get(),buffer.Get(),D3D12_RESOURCE_STATE_PRESENT,D3D12_RESOURCE_STATE_COPY_SOURCE);
            D3D12_TEXTURE_COPY_LOCATION from{},to{};from.pResource=buffer.Get();from.Type=D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX;
            to.pResource=readback.Get();to.Type=D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT;to.PlacedFootprint=footprint;
            list->CopyTextureRegion(&to,0,0,0,&from,nullptr);
            barrier(list.Get(),buffer.Get(),D3D12_RESOURCE_STATE_COPY_SOURCE,D3D12_RESOURCE_STATE_PRESENT);submit();
            void* data=nullptr;check(readback->Map(0,nullptr,&data));
            auto* pixel=static_cast<BYTE*>(data)+footprint.Offset+64*footprint.Footprint.RowPitch+64*4;
            bool red=pixel[0]>240&&pixel[1]<10&&pixel[2]<10;
            bool bluePixel=pixel[0]<10&&pixel[1]<10&&pixel[2]>240;
            auto* imagePixel=static_cast<BYTE*>(data)+footprint.Offset+180*footprint.Footprint.RowPitch+320*4;
            bool green=imagePixel[0]<10&&imagePixel[1]>240&&imagePixel[2]<10;
            readback->Unmap(0,nullptr);require(waiting?(red||bluePixel):(expected?red:bluePixel),expected?"Overlay pixels missing":"Inactive overlay modified pixels");
            if(expected&&textured) require(green,"Font atlas overwrote image descriptor");
            return red;
        };
        auto awaitOverlay=[&]{
            auto deadline=std::chrono::steady_clock::now()+std::chrono::seconds(10);
            while(!frame(false,true)) {require(std::chrono::steady_clock::now()<deadline,"Background preparation timeout");Sleep(1);}
        };
        frame(false); // Device, swapchain and a rendered frame all precede attachment.
        auto dll=LoadLibraryExW(argv[1],nullptr,LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR|LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);require(dll!=nullptr,"Load fixture");
        auto start=reinterpret_cast<BOOL(WINAPI*)(USHORT)>(GetProcAddress(dll,"ZtmzDashboardStart"));
        auto stop=reinterpret_cast<void(WINAPI*)()>(GetProcAddress(dll,"ZtmzDashboardStop"));
        auto scene=reinterpret_cast<void(WINAPI*)()>(GetProcAddress(dll,"ZtmzDashboardTestScene"));
        require(start&&stop&&scene,"Fixture exports");scene();require(start(8080),"Start");
        frame(false,true);stop(); // cancellation must not join background preparation
        require(start(8080),"Restart during preparation");
        awaitOverlay(); // Game keeps presenting while calibration/resources prepare.
        frame(true);frame(true);
        auto textures=reinterpret_cast<void(WINAPI*)(UINT)>(GetProcAddress(dll,"ZtmzDashboardTestTextures"));
        require(textures!=nullptr,"Texture fixture export");
        textured=true;measureText=true;textures(0);
        for(int i=0;i<100;++i){frame(true);Sleep(1);}
        textures(1);
        for(int i=0;i<100;++i){frame(true);Sleep(1);}
        measureText=false;
        stop();frame(false);
        require(start(8080),"Restart");awaitOverlay();frame(true);stop();frame(false);
        check(swap->ResizeBuffers(2,640,360,DXGI_FORMAT_UNKNOWN,0));
        require(start(8080),"Start after resize");awaitOverlay();frame(true);
        // Vulkan/DXGI interop can destroy and replace the chain without calling
        // ResizeBuffers or stopping the overlay. The HWND must become reusable.
        for(int replacement=0;replacement<3;++replacement) {
            modern.Reset();swap.Reset();
            check(factory->CreateSwapChainForHwnd(queue.Get(),window,&desc,nullptr,nullptr,&swap));
            check(swap.As(&modern));awaitOverlay();frame(true);
        }
        stop();
        CloseHandle(event);DestroyWindow(window);
        std::cout<<"DX12 late hook GPU pixels, asynchronous font updates, Stop, restart and resize passed. Max startup Present: "<<maxStartupPresentMs<<" ms; max Present during text updates: "<<maxTextPresentMs<<" ms (controlled host).\n";return 0;
    } catch(const std::exception& e){std::cerr<<e.what()<<'\n';printStatus();return 1;}
}
