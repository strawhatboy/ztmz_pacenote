#include <windows.h>
#include <d3d11.h>
#include <dxgi.h>
#include <wrl/client.h>
#include <filesystem>
#include <iostream>
#include <memory>
#include <stdexcept>
#include <string>
#include <cstring>
#include "graphics/render_api.hpp"
using Microsoft::WRL::ComPtr;
namespace {
void require(bool ok,const char* message){if(!ok) throw std::runtime_error(message);}
void check(HRESULT hr,const char* message){if(FAILED(hr)) throw std::runtime_error(std::string(message)+" HRESULT="+std::to_string(hr));}
struct Hook {
    using Start=BOOL(WINAPI*)(unsigned short);
    using Stop=void(WINAPI*)();
    using Api=int(WINAPI*)();
    Start start=nullptr;Stop stop=nullptr;Api api=nullptr;
    explicit Hook(const wchar_t* path) {
        auto module=LoadLibraryExW(path,nullptr,LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR|LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
        require(module!=nullptr,"Cannot load controlled-host hook DLL");
        start=reinterpret_cast<Start>(GetProcAddress(module,"ZtmzDashboardStart"));
        stop=reinterpret_cast<Stop>(GetProcAddress(module,"ZtmzDashboardStop"));
        api=reinterpret_cast<Api>(GetProcAddress(module,"ZtmzDashboardTestActiveApi"));
        auto fixture=reinterpret_cast<Stop>(GetProcAddress(module,"ZtmzDashboardTestScene"));
        require(start&&stop&&api&&fixture,"Missing hook test exports");fixture();
    }
    ~Hook(){if(stop) stop();} // DLL is process-pinned; no hot FreeLibrary.
    void begin(){require(start(8080)!=FALSE,"Hook startup failed");}
};
struct Host {
    HWND window=nullptr;
    ComPtr<ID3D11Device> device;
    ComPtr<ID3D11DeviceContext> context;
    ComPtr<IDXGISwapChain> swap;
    ComPtr<ID3D11RenderTargetView> target;
    unsigned width=640,height=360;
    Host() {
        // Visible to DXGI's eligibility check but entirely outside the desktop.
        window=CreateWindowExW(WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE,L"STATIC",L"ZTMZ DX11 controlled hook test",
            WS_POPUP|WS_VISIBLE,-30000,-30000,width,height,nullptr,nullptr,GetModuleHandleW(nullptr),nullptr);
        require(window!=nullptr,"Cannot create controlled test window");
        DXGI_SWAP_CHAIN_DESC desc{};desc.BufferCount=1;desc.BufferDesc.Width=width;desc.BufferDesc.Height=height;
        desc.BufferDesc.Format=DXGI_FORMAT_R8G8B8A8_UNORM;desc.BufferUsage=DXGI_USAGE_RENDER_TARGET_OUTPUT;
        desc.OutputWindow=window;desc.SampleDesc.Count=1;desc.Windowed=TRUE;desc.SwapEffect=DXGI_SWAP_EFFECT_SEQUENTIAL;
        check(D3D11CreateDeviceAndSwapChain(nullptr,D3D_DRIVER_TYPE_HARDWARE,nullptr,0,nullptr,0,D3D11_SDK_VERSION,
            &desc,&swap,&device,nullptr,&context),"Create hardware DX11 device");
        createTarget();
    }
    ~Host(){if(context) context->ClearState();target.Reset();swap.Reset();context.Reset();device.Reset();if(window) DestroyWindow(window);}
    void createTarget() {
        ComPtr<ID3D11Texture2D> buffer;check(swap->GetBuffer(0,IID_PPV_ARGS(&buffer)),"Get back buffer");
        check(device->CreateRenderTargetView(buffer.Get(),nullptr,&target),"Create render target");
    }
    void resize() {
        context->OMSetRenderTargets(0,nullptr,nullptr);target.Reset();width=800;height=450;
        check(swap->ResizeBuffers(1,width,height,DXGI_FORMAT_UNKNOWN,0),"ResizeBuffers after hook rendering");createTarget();
    }
    void frame(bool overlay) {
        const float blue[]={0,0,1,1};context->ClearRenderTargetView(target.Get(),blue);
        auto* view=target.Get();context->OMSetRenderTargets(1,&view,nullptr);
        D3D11_VIEWPORT viewport{7,9,123,111,0.2f,0.8f};context->RSSetViewports(1,&viewport);
        context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_LINELIST);
        ComPtr<ID3D11Texture2D> buffer;check(swap->GetBuffer(0,IID_PPV_ARGS(&buffer)),"Get present buffer");
        check(swap->Present(0,0),"Present through hook");
        ComPtr<ID3D11RenderTargetView> restored;context->OMGetRenderTargets(1,&restored,nullptr);
        require(restored.Get()==target.Get(),"Hook did not restore the host render target");
        UINT count=1;D3D11_VIEWPORT actual{};context->RSGetViewports(&count,&actual);
        require(count==1 && std::memcmp(&actual,&viewport,sizeof(viewport))==0,"Hook changed host viewport");
        D3D11_PRIMITIVE_TOPOLOGY topology;context->IAGetPrimitiveTopology(&topology);
        require(topology==D3D11_PRIMITIVE_TOPOLOGY_LINELIST,"Hook changed host primitive topology");
        D3D11_TEXTURE2D_DESC desc{};buffer->GetDesc(&desc);desc.Usage=D3D11_USAGE_STAGING;
        desc.BindFlags=0;desc.CPUAccessFlags=D3D11_CPU_ACCESS_READ;desc.MiscFlags=0;
        ComPtr<ID3D11Texture2D> staging;check(device->CreateTexture2D(&desc,nullptr,&staging),"Create readback");
        context->CopyResource(staging.Get(),buffer.Get());D3D11_MAPPED_SUBRESOURCE data{};
        check(context->Map(staging.Get(),0,D3D11_MAP_READ,0,&data),"Read back hook pixels");
        auto pixel=[&](unsigned x,unsigned y){return static_cast<const unsigned char*>(data.pData)+y*data.RowPitch+x*4;};
        // Logical fixture [96,288] maps to [width/20, width*3/20].
        const auto* drawn=pixel(width/10,width/10);const auto* untouched=pixel(width/2,height/2);
        bool correct=overlay?(drawn[0]>240 && drawn[1]<10 && drawn[2]<10):(drawn[0]<10 && drawn[1]<10 && drawn[2]>240);
        bool background=untouched[0]<10 && untouched[1]<10 && untouched[2]>240;
        context->Unmap(staging.Get(),0);
        require(correct,overlay?"Hook rectangle missing from GPU readback":"Stopped hook still drew pixels");
        require(background,"Hook damaged the host background");
    }
};
}
int wmain(int argc,wchar_t** argv) {
    try {
        require(argc>=2,"Usage: dx11_hook_host <test-dll> [early]");
        SetEnvironmentVariableW(L"ZTMZ_RENDER_API",L"auto");
        std::unique_ptr<Hook> hook;
        if(argc>2) {hook=std::make_unique<Hook>(argv[1]);hook->begin();}
        Host host;
        if(!hook) {host.frame(false);hook=std::make_unique<Hook>(argv[1]);hook->begin();}
        require(hook->api()==static_cast<int>(ztmz::RenderApi::Auto),"API selected before a presentation");
        host.frame(true);
        require(hook->api()==static_cast<int>(ztmz::RenderApi::Dx11),"Auto detection did not select DX11");
        host.resize();host.frame(true);
        for(int cycle=0;cycle<3;++cycle) {
            hook->stop();host.frame(false);
            require(hook->api()==static_cast<int>(ztmz::RenderApi::Auto),"Stop retained renderer ownership");
            hook->begin();host.frame(true);
        }
        hook->stop();host.frame(false);
        std::cout<<"DX11 hook passed: auto detection, GPU pixel readback, host state restoration, resize, 3 stop/start cycles ("
                 <<(argc>2?"early":"late")<<" loading).\n";
        return 0;
    } catch(const std::exception& e){std::cerr<<e.what()<<"\n";return 1;}
}
