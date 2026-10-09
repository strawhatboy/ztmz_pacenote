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
#include <nlohmann/json.hpp>
#include "graphics/render_api.hpp"
using Microsoft::WRL::ComPtr;
namespace {
void require(bool ok,const char* message){if(!ok) throw std::runtime_error(message);}
void check(HRESULT hr,const char* message){if(FAILED(hr)) throw std::runtime_error(std::string(message)+" HRESULT="+std::to_string(hr));}
using Json=nlohmann::json;
Json control(const std::string& request,DWORD process=GetCurrentProcessId()) {
    auto name=L"\\\\.\\pipe\\ztmz-dashboard-"+std::to_wstring(process);
    HANDLE pipe=INVALID_HANDLE_VALUE;const auto deadline=GetTickCount64()+3000;
    do {
        pipe=CreateFileW(name.c_str(),GENERIC_READ|GENERIC_WRITE,0,nullptr,OPEN_EXISTING,0,nullptr);
        if(pipe!=INVALID_HANDLE_VALUE) break;
        require(GetLastError()==ERROR_PIPE_BUSY,"Control pipe unavailable");
        WaitNamedPipeW(name.c_str(),100);
    } while(GetTickCount64()<deadline);
    require(pipe!=INVALID_HANDLE_VALUE,"Control pipe busy past deadline");
    struct Close {HANDLE pipe;~Close(){CloseHandle(pipe);}} close{pipe};
    DWORD mode=PIPE_READMODE_MESSAGE;require(SetNamedPipeHandleState(pipe,&mode,nullptr,nullptr)!=FALSE,"Set control message mode");
    DWORD count=0;require(WriteFile(pipe,request.data(),static_cast<DWORD>(request.size()),&count,nullptr)!=FALSE,"Write control request");
    char buffer[4096]{};require(ReadFile(pipe,buffer,sizeof(buffer),&count,nullptr)!=FALSE,"Read control response");
    auto response=Json::parse(buffer,buffer+count);
    char ack=1;require(WriteFile(pipe,&ack,1,&count,nullptr)!=FALSE,"Acknowledge control response");
    return response;
}
Json command(const char* verb,const std::string& instance) {
    return {{"version",1},{"command",verb},{"instance",instance},{"port",8080},{"api","auto"}};
}
void externalProbe(const wchar_t* managed=nullptr) {
    wchar_t path[32768]{};require(GetModuleFileNameW(nullptr,path,32768)!=0,"Get probe executable");
    const wchar_t* executable=managed?managed:path;
    std::wstring args=L"\""+std::wstring(executable)+L"\" --control-probe "+std::to_wstring(GetCurrentProcessId());
    STARTUPINFOW startup{};startup.cb=sizeof(startup);PROCESS_INFORMATION process{};
    require(CreateProcessW(executable,args.data(),nullptr,nullptr,FALSE,CREATE_NO_WINDOW,nullptr,nullptr,&startup,&process)!=FALSE,"Launch external control probe");
    CloseHandle(process.hThread);
    auto wait=WaitForSingleObject(process.hProcess,5000);DWORD code=1;
    if(wait!=WAIT_OBJECT_0) {TerminateProcess(process.hProcess,1);WaitForSingleObject(process.hProcess,5000);}
    GetExitCodeProcess(process.hProcess,&code);CloseHandle(process.hProcess);
    require(wait==WAIT_OBJECT_0 && code==0,"External control probe failed");
}
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
    void replaceSwapChain() {
        auto previous=swap; // Keep the old address alive to guarantee a distinct replacement.
        context->ClearState();target.Reset();swap.Reset();context.Reset();device.Reset();
        DXGI_SWAP_CHAIN_DESC desc{};desc.BufferCount=1;desc.BufferDesc.Width=width;desc.BufferDesc.Height=height;
        desc.BufferDesc.Format=DXGI_FORMAT_R8G8B8A8_UNORM;desc.BufferUsage=DXGI_USAGE_RENDER_TARGET_OUTPUT;
        desc.OutputWindow=window;desc.SampleDesc.Count=1;desc.Windowed=TRUE;desc.SwapEffect=DXGI_SWAP_EFFECT_SEQUENTIAL;
        check(D3D11CreateDeviceAndSwapChain(nullptr,D3D_DRIVER_TYPE_HARDWARE,nullptr,0,nullptr,0,D3D11_SDK_VERSION,
            &desc,&swap,&device,nullptr,&context),"Recreate DX11 device/swap chain for the same window");
        createTarget();
    }
    bool frame(bool overlay,bool waiting=false) {
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
        bool red=drawn[0]>240 && drawn[1]<10 && drawn[2]<10;
        bool bluePixel=drawn[0]<10 && drawn[1]<10 && drawn[2]>240;
        bool correct=waiting?(red||bluePixel):(overlay?red:bluePixel);
        bool background=untouched[0]<10 && untouched[1]<10 && untouched[2]>240;
        context->Unmap(staging.Get(),0);
        require(correct,overlay?"Hook rectangle missing from GPU readback":"Stopped hook still drew pixels");
        require(background,"Hook damaged the host background");
        return red;
    }
    void awaitOverlay() {
        auto deadline=GetTickCount64()+10000;
        while(!frame(false,true)){require(GetTickCount64()<deadline,"Background preparation timed out");Sleep(1);}
    }
};
}
int wmain(int argc,wchar_t** argv) {
    try {
        if(argc==4 && std::wstring_view(argv[1])==L"--injection") {
            Host host;
            std::wstring arguments=L"\""+std::wstring(argv[3])+L"\" --inject-probe "+std::to_wstring(GetCurrentProcessId())
                +L" \""+argv[2]+L"\" "+std::to_wstring(reinterpret_cast<uintptr_t>(host.window));
            STARTUPINFOW startup{};startup.cb=sizeof(startup);PROCESS_INFORMATION child{};
            require(CreateProcessW(argv[3],arguments.data(),nullptr,nullptr,FALSE,CREATE_NO_WINDOW,nullptr,nullptr,&startup,&child)!=FALSE,"Launch injection probe");
            CloseHandle(child.hThread);
            auto deadline=GetTickCount64()+20000;
            while(WaitForSingleObject(child.hProcess,0)==WAIT_TIMEOUT && GetTickCount64()<deadline) {
                const float blue[]={0,0,1,1};host.context->ClearRenderTargetView(host.target.Get(),blue);
                host.swap->Present(0,0);
                MSG message{};while(PeekMessageW(&message,nullptr,0,0,PM_REMOVE)){TranslateMessage(&message);DispatchMessageW(&message);}
                Sleep(10);
            }
            DWORD code=1;GetExitCodeProcess(child.hProcess,&code);
            if(code==STILL_ACTIVE){TerminateProcess(child.hProcess,1);WaitForSingleObject(child.hProcess,5000);}
            CloseHandle(child.hProcess);
            require(code==0,"Cross-process injection/control probe failed");
            std::cout<<"Production DLL cross-process loading, inert bootstrap, DX11 submission and stop passed.\n";
            return 0;
        }
        if(argc==3 && std::wstring_view(argv[1])==L"--control-probe") {
            auto pid=static_cast<DWORD>(std::stoul(argv[2]));
            auto status=control(R"({"version":1,"command":"status"})",pid);
            require(status["ok"]==true && status["pid"]==pid && status["selectedApi"]=="dx11","Cross-process status mismatch");
            return 0;
        }
        require(argc>=2,"Usage: dx11_hook_host <test-dll> [early]");
        SetEnvironmentVariableW(L"ZTMZ_RENDER_API",L"auto");
        std::unique_ptr<Hook> hook;
        bool early=argc>2 && std::wstring_view(argv[2])==L"early";
        if(early) {hook=std::make_unique<Hook>(argv[1]);hook->begin();}
        Host host;
        if(!hook) {host.frame(false);hook=std::make_unique<Hook>(argv[1]);hook->begin();}
        require(hook->api()==static_cast<int>(ztmz::RenderApi::Auto),"API selected before a presentation");
        host.awaitOverlay();
        host.frame(true);
        require(hook->api()==static_cast<int>(ztmz::RenderApi::Dx11),"Auto detection did not select DX11");
        auto status=control(R"({"version":1,"command":"status"})");
        require(status["ok"]==true && status["started"]==true && status["selectedApi"]=="dx11","Incorrect control status");
        const auto instance=status.at("instance").get<std::string>();
        externalProbe();
        if(argc>3) {externalProbe(argv[3]);host.frame(true);}
        require(status["submittedFrames"].get<uint64_t>()>0 && status["window"].get<uint64_t>()==reinterpret_cast<uintptr_t>(host.window),"Missing frame/window acknowledgement");
        require(control(command("start",instance).dump())["ok"]==true,"Idempotent start rejected");
        require(control(command("stop","stale-instance").dump())["ok"]==false,"Stale request accepted");
        require(control(R"({"version":2,"command":"stop"})")["ok"]==false,"Unknown version accepted");
        require(control("{broken")["ok"]==false,"Malformed request accepted");
        bool oversizedRejected=false;
        try {control(std::string(5000,'x'));} catch(const std::runtime_error&) {oversizedRejected=true;}
        require(oversizedRejected,"Oversized control request accepted");
        auto invalid=command("start",instance);invalid["port"]=65536;
        require(control(invalid.dump())["ok"]==false,"Invalid port accepted");
        invalid["port"]=8081;
        require(control(invalid.dump())["ok"]==false,"Running configuration silently changed");
        host.frame(true);
        host.resize();host.frame(true);
        host.replaceSwapChain();host.awaitOverlay();host.frame(true);
        host.replaceSwapChain();host.awaitOverlay();host.frame(true);
        {Host auxiliary;auxiliary.frame(false);host.frame(true);}
        for(int cycle=0;cycle<3;++cycle) {
            status=control(command("stop",instance).dump());
            require(status["ok"]==true && status["started"]==false,"Control stop failed");
            require(status["submittedFrames"]==0 && status["lastFrameAgeMs"]==-1,"Stop retained stale frame status");
            host.frame(false);
            require(hook->api()==static_cast<int>(ztmz::RenderApi::Auto),"Stop retained renderer ownership");
            require(control(command("start",instance).dump())["ok"]==true,"Control restart failed");
            host.awaitOverlay();
            host.frame(true);
        }
        hook->stop();host.frame(false);
        auto targeted=command("start",instance);
        auto splash=CreateWindowExW(0,L"STATIC",L"Startup window",WS_POPUP|WS_VISIBLE,-30000,-30000,64,64,nullptr,nullptr,GetModuleHandleW(nullptr),nullptr);
        require(splash!=nullptr,"Splash window");
        targeted["targetWindow"]=reinterpret_cast<uintptr_t>(splash);
        require(control(targeted.dump())["ok"]==true,"Splash target rejected");
        host.frame(false);
        DestroyWindow(splash);
        targeted["targetWindow"]=reinterpret_cast<uintptr_t>(host.window);
        require(control(targeted.dump())["ok"]==true,"Live window handoff rejected");
        host.awaitOverlay();
        host.frame(true);
        require(control(command("stop",instance).dump())["ok"]==true,"Targeted stop failed");
        targeted["targetWindow"]=reinterpret_cast<uintptr_t>(GetDesktopWindow());
        require(control(targeted.dump())["ok"]==false,"Foreign window accepted");
        std::cout<<"DX11 hook passed: auto detection, GPU pixel readback, host state restoration, resize, 3 stop/start cycles ("
                 <<(early?"early":"late")<<" loading).\n";
        return 0;
    } catch(const std::exception& e){std::cerr<<e.what()<<"\n";return 1;}
}
