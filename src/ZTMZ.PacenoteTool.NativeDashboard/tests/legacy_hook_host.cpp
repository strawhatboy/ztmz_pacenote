#include <windows.h>
#include <wrl/client.h>
#include <chrono>
#include <iostream>
#include <stdexcept>
#ifdef TEST_DX9
#include <d3d9.h>
#else
#include <d3d10.h>
#include <dxgi.h>
#endif
using Microsoft::WRL::ComPtr;
void require(bool ok,const char* message){if(!ok)throw std::runtime_error(message);}
void check(HRESULT hr){require(SUCCEEDED(hr),"Legacy graphics operation failed");}
int wmain(int argc,wchar_t** argv) {
    try {
        require(argc==2,"Expected fixture DLL");
        auto window=CreateWindowExW(WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE,L"STATIC",L"Legacy overlay test",WS_POPUP|WS_VISIBLE,-30000,-30000,640,360,nullptr,nullptr,GetModuleHandleW(nullptr),nullptr);
        require(window!=nullptr,"Window");
#ifdef TEST_DX9
        ComPtr<IDirect3D9> api;api.Attach(Direct3DCreate9(D3D_SDK_VERSION));require(bool(api),"DX9");
        D3DPRESENT_PARAMETERS params{};params.Windowed=TRUE;params.SwapEffect=D3DSWAPEFFECT_COPY;
        params.hDeviceWindow=window;params.BackBufferWidth=640;params.BackBufferHeight=360;params.BackBufferFormat=D3DFMT_A8R8G8B8;
        params.PresentationInterval=D3DPRESENT_INTERVAL_IMMEDIATE;
        ComPtr<IDirect3DDevice9> device;
        check(api->CreateDevice(0,D3DDEVTYPE_HAL,window,D3DCREATE_SOFTWARE_VERTEXPROCESSING,&params,&device));
#else
        DXGI_SWAP_CHAIN_DESC desc{};desc.BufferCount=1;desc.BufferDesc.Width=640;desc.BufferDesc.Height=360;
        desc.BufferDesc.Format=DXGI_FORMAT_R8G8B8A8_UNORM;desc.BufferUsage=DXGI_USAGE_RENDER_TARGET_OUTPUT;
        desc.OutputWindow=window;desc.SampleDesc.Count=1;desc.Windowed=TRUE;desc.SwapEffect=DXGI_SWAP_EFFECT_SEQUENTIAL;
        ComPtr<ID3D10Device> device;ComPtr<IDXGISwapChain> swap;
        check(D3D10CreateDeviceAndSwapChain(nullptr,D3D10_DRIVER_TYPE_HARDWARE,nullptr,D3D10_CREATE_DEVICE_SINGLETHREADED,D3D10_SDK_VERSION,&desc,&swap,&device));
#endif
        double maximum=0;
        auto frame=[&]{
#ifdef TEST_DX9
            check(device->Clear(0,nullptr,D3DCLEAR_TARGET,D3DCOLOR_XRGB(0,0,255),1,0));
            ComPtr<IDirect3DSurface9> back;check(device->GetBackBuffer(0,0,D3DBACKBUFFER_TYPE_MONO,&back));
            auto start=std::chrono::steady_clock::now();check(device->Present(nullptr,nullptr,nullptr,nullptr));
            maximum=std::max(maximum,std::chrono::duration<double,std::milli>(std::chrono::steady_clock::now()-start).count());
            ComPtr<IDirect3DSurface9> read;check(device->CreateOffscreenPlainSurface(640,360,D3DFMT_A8R8G8B8,D3DPOOL_SYSTEMMEM,&read,nullptr));
            check(device->GetRenderTargetData(back.Get(),read.Get()));D3DLOCKED_RECT mapped{};check(read->LockRect(&mapped,nullptr,D3DLOCK_READONLY));
            auto pixel=static_cast<unsigned char*>(mapped.pBits)+64*mapped.Pitch+64*4;
            bool red=pixel[2]>240&&pixel[1]<10&&pixel[0]<10;
            bool blue=pixel[0]>240&&pixel[1]<10&&pixel[2]<10;check(read->UnlockRect());
#else
            ComPtr<ID3D10Texture2D> back;check(swap->GetBuffer(0,IID_PPV_ARGS(&back)));
            ComPtr<ID3D10RenderTargetView> target;check(device->CreateRenderTargetView(back.Get(),nullptr,&target));
            float clear[]{0,0,1,1};device->ClearRenderTargetView(target.Get(),clear);
            auto start=std::chrono::steady_clock::now();check(swap->Present(0,0));
            maximum=std::max(maximum,std::chrono::duration<double,std::milli>(std::chrono::steady_clock::now()-start).count());
            D3D10_TEXTURE2D_DESC rd{};back->GetDesc(&rd);rd.Usage=D3D10_USAGE_STAGING;rd.BindFlags=0;rd.CPUAccessFlags=D3D10_CPU_ACCESS_READ;
            ComPtr<ID3D10Texture2D> read;check(device->CreateTexture2D(&rd,nullptr,&read));device->CopyResource(read.Get(),back.Get());
            D3D10_MAPPED_TEXTURE2D mapped{};check(read->Map(0,D3D10_MAP_READ,0,&mapped));
            auto pixel=static_cast<unsigned char*>(mapped.pData)+64*mapped.RowPitch+64*4;
            bool red=pixel[0]>240&&pixel[1]<10&&pixel[2]<10;
            bool blue=pixel[2]>240&&pixel[1]<10&&pixel[0]<10;read->Unmap(0);
#endif
            require(red||blue,"Invalid GPU pixels");return red;
        };
        require(!frame(),"Baseline");
        const auto baseline=maximum;maximum=0;
        auto dll=LoadLibraryExW(argv[1],nullptr,LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR|LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);require(dll!=nullptr,"DLL");
        auto start=reinterpret_cast<BOOL(WINAPI*)(USHORT)>(GetProcAddress(dll,"ZtmzDashboardStart"));
        auto stop=reinterpret_cast<void(WINAPI*)()>(GetProcAddress(dll,"ZtmzDashboardStop"));
        auto scene=reinterpret_cast<void(WINAPI*)()>(GetProcAddress(dll,"ZtmzDashboardTestScene"));
        require(start&&stop&&scene,"Exports");scene();
        for(int cycle=0;cycle<3;++cycle){
            require(start(8080),"Start");auto deadline=GetTickCount64()+10000;
            while(!frame()){require(GetTickCount64()<deadline,"Warmup timeout");Sleep(1);}
            require(frame(),"Overlay disappeared");stop();require(!frame(),"Stop still draws");
#ifdef TEST_DX9
            check(device->Reset(&params));
#else
            check(swap->ResizeBuffers(1,640,360,DXGI_FORMAT_UNKNOWN,0));
#endif
        }
        DestroyWindow(window);
        std::cout<<"Legacy single-threaded GPU readback, restart and reset passed; baseline Present "<<baseline<<" ms, max active Present "<<maximum<<" ms (includes driver presentation).\n";
        return 0;
    }catch(const std::exception& e){std::cerr<<e.what()<<'\n';return 1;}
}
