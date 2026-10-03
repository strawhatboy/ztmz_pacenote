#include "transport.hpp"
#include "graphics/render_session.hpp"
#include "graphics/dx11/backend.hpp"
#include <windows.h>
#include <dxgi.h>
#include <wrl/client.h>
#include <imgui_impl_win32.h>
#include <thread>
#include <chrono>
#include <iostream>
#include <stdexcept>
using Microsoft::WRL::ComPtr;
extern IMGUI_IMPL_API LRESULT ImGui_ImplWin32_WndProcHandler(HWND,UINT,WPARAM,LPARAM);
namespace {
unsigned pendingWidth{},pendingHeight{};
ztmz::RenderSession* inputSession=nullptr;
LRESULT CALLBACK windowProc(HWND hwnd,UINT msg,WPARAM w,LPARAM l) {
    LRESULT handled=0;
    if(inputSession) inputSession->withContext([&]{handled=ImGui_ImplWin32_WndProcHandler(hwnd,msg,w,l);});
    if(handled) return handled;
    if(msg==WM_SIZE && w!=SIZE_MINIMIZED){pendingWidth=LOWORD(l);pendingHeight=HIWORD(l);return 0;}
    if(msg==WM_DESTROY){PostQuitMessage(0);return 0;}
    return DefWindowProc(hwnd,msg,w,l);
}
struct Platform {
    ztmz::RenderSession& session;
    Platform(ztmz::RenderSession& s,HWND hwnd):session(s) {
        bool initialized=false;session.withContext([&]{initialized=ImGui_ImplWin32_Init(hwnd);});
        if(!initialized) throw std::runtime_error("Win32 platform initialization failed");
        inputSession=&session;
    }
    ~Platform(){inputSession=nullptr;session.withContext([]{ImGui_ImplWin32_Shutdown();});}
};
void run(HWND hwnd,unsigned short port) {
    DXGI_SWAP_CHAIN_DESC desc{};desc.BufferCount=2;desc.BufferDesc.Format=DXGI_FORMAT_R8G8B8A8_UNORM;
    desc.BufferUsage=DXGI_USAGE_RENDER_TARGET_OUTPUT;desc.OutputWindow=hwnd;desc.SampleDesc.Count=1;
    desc.Windowed=TRUE;desc.SwapEffect=DXGI_SWAP_EFFECT_DISCARD;
    ComPtr<ID3D11Device> device;ComPtr<ID3D11DeviceContext> context;ComPtr<IDXGISwapChain> swap;
    if(FAILED(D3D11CreateDeviceAndSwapChain(nullptr,D3D_DRIVER_TYPE_HARDWARE,nullptr,0,nullptr,0,D3D11_SDK_VERSION,&desc,&swap,&device,nullptr,&context)))
        throw std::runtime_error("Cannot create DX11 viewer device");
    ComPtr<ID3D11RenderTargetView> target;
    unsigned width=0,height=0;
    auto createTarget=[&]{
        ComPtr<ID3D11Texture2D> buffer;
        if(FAILED(swap->GetBuffer(0,IID_PPV_ARGS(&buffer)))) return false;
        D3D11_TEXTURE2D_DESC size{};buffer->GetDesc(&size);width=size.Width;height=size.Height;
        return SUCCEEDED(device->CreateRenderTargetView(buffer.Get(),nullptr,&target));
    };
    if(!createTarget()) throw std::runtime_error("Cannot create viewer render target");
    ztmz::RenderSession renderer(ztmz::createDx11Backend(device.Get(),context.Get()));
    Platform platform(renderer,hwnd);
    ztmz::SharedScene shared;
    std::jthread network([&](std::stop_token stop){ztmz::receive(stop,shared,port);});
    ShowWindow(hwnd,SW_SHOWDEFAULT);
    auto previous=std::chrono::steady_clock::now();
    bool running=true;
    while(running){
        MSG msg{};while(PeekMessage(&msg,nullptr,0,0,PM_REMOVE)){TranslateMessage(&msg);DispatchMessage(&msg);if(msg.message==WM_QUIT) running=false;}
        if(!running) break;
        if(IsIconic(hwnd)){Sleep(20);continue;}
        if(pendingWidth && pendingHeight){
            context->OMSetRenderTargets(0,nullptr,nullptr);target.Reset();renderer.invalidateDeviceObjects();
            if(FAILED(swap->ResizeBuffers(0,pendingWidth,pendingHeight,DXGI_FORMAT_UNKNOWN,0)) || !createTarget()) break;
            pendingWidth=pendingHeight=0;
        }
        ztmz::Scene scene;std::string status;std::shared_ptr<const ztmz::ImageAssets> assets;
        {std::lock_guard lock(shared.mutex);scene=shared.scene;status=shared.status;assets=shared.images;}
        renderer.withContext([]{ImGui_ImplWin32_NewFrame();});
        auto now=std::chrono::steady_clock::now();
        const float clear[4]{0.04f,0.04f,0.04f,1};context->ClearRenderTargetView(target.Get(),clear);
        ztmz::Dx11Frame frame(target.Get());
        renderer.render({width,height,std::chrono::duration<float>(now-previous).count()},frame,scene,*assets,
            [&](const std::set<std::string>& warnings){
                ImGui::Begin("Native dashboard status");ImGui::TextUnformatted(status.c_str());
                ImGui::Text("Backend: %s | 127.0.0.1:%u | scene %lld",ztmz::apiName(renderer.api()).data(),port,static_cast<long long>(scene.revision));
                for(const auto& warning:warnings) ImGui::Text("Unsupported: %s",warning.c_str());
                ImGui::End();
            });
        previous=now;
        if(FAILED(swap->Present(1,0))) break;
    }
}
}
int main(int argc,char** argv) {
    unsigned short port=8080;
    try {if(argc>1){auto value=std::stoi(argv[1]);if(value<1 || value>65535) throw std::runtime_error("Invalid port");port=static_cast<unsigned short>(value);}}
    catch(const std::exception& e){std::cerr<<e.what()<<"\n";return 1;}
    auto instance=GetModuleHandle(nullptr);
    WNDCLASS wc{};wc.lpfnWndProc=windowProc;wc.hInstance=instance;wc.lpszClassName=L"ZtmzDashboardViewer";
    if(!RegisterClass(&wc)) return 1;
    auto hwnd=CreateWindow(wc.lpszClassName,L"ZTMZ Native Dashboard Viewer (DX11)",WS_OVERLAPPEDWINDOW,CW_USEDEFAULT,CW_USEDEFAULT,1280,720,nullptr,nullptr,instance,nullptr);
    if(!hwnd){UnregisterClass(wc.lpszClassName,instance);return 1;}
    int result=0;
    try {run(hwnd,port);}
    catch(const std::exception& e){std::cerr<<e.what()<<"\n";result=1;}
    if(IsWindow(hwnd)) DestroyWindow(hwnd);
    UnregisterClass(wc.lpszClassName,instance);return result;
}
