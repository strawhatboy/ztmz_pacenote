#include <windows.h>
#include <d3d9.h>
#include <wrl/client.h>
#include <iostream>
#include <string>
#include <stdexcept>
using Microsoft::WRL::ComPtr;
void check(bool ok,const char* message){if(!ok) throw std::runtime_error(message);}
int wmain(int argc,wchar_t** argv) {
    try {
        check(argc==3,"Expected production DLL and x64 managed probe executable");
        HWND window=CreateWindowExW(WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE,L"STATIC",L"ZTMZ x86 DX9 injection test",
            WS_POPUP|WS_VISIBLE,-30000,-30000,640,360,nullptr,nullptr,GetModuleHandleW(nullptr),nullptr);
        check(window!=nullptr,"Create test window failed");
        ComPtr<IDirect3D9> api;api.Attach(Direct3DCreate9(D3D_SDK_VERSION));check(api!=nullptr,"D3D9 unavailable");
        D3DPRESENT_PARAMETERS parameters{};parameters.Windowed=TRUE;parameters.SwapEffect=D3DSWAPEFFECT_DISCARD;
        parameters.hDeviceWindow=window;parameters.BackBufferWidth=640;parameters.BackBufferHeight=360;
        ComPtr<IDirect3DDevice9> device;
        check(SUCCEEDED(api->CreateDevice(0,D3DDEVTYPE_HAL,window,D3DCREATE_SOFTWARE_VERTEXPROCESSING|D3DCREATE_MULTITHREADED,&parameters,&device)),"D3D9 device creation failed");
        std::wstring command=L"\""+std::wstring(argv[2])+L"\" --inject-probe "+std::to_wstring(GetCurrentProcessId())+
            L" \""+argv[1]+L"\" "+std::to_wstring(reinterpret_cast<uintptr_t>(window))+L" dx9";
        STARTUPINFOW startup{};startup.cb=sizeof(startup);PROCESS_INFORMATION child{};
        check(CreateProcessW(argv[2],command.data(),nullptr,nullptr,FALSE,CREATE_NO_WINDOW,nullptr,nullptr,&startup,&child)!=FALSE,"Launch x64 probe failed");
        CloseHandle(child.hThread);
        auto deadline=GetTickCount64()+25000;
        while(WaitForSingleObject(child.hProcess,0)==WAIT_TIMEOUT && GetTickCount64()<deadline) {
            device->Clear(0,nullptr,D3DCLEAR_TARGET,D3DCOLOR_XRGB(0,0,255),1,0);
            if(SUCCEEDED(device->BeginScene())) device->EndScene();
            device->Present(nullptr,nullptr,nullptr,nullptr);
            MSG message{};while(PeekMessageW(&message,nullptr,0,0,PM_REMOVE)){TranslateMessage(&message);DispatchMessageW(&message);}
            Sleep(10);
        }
        DWORD result=1;GetExitCodeProcess(child.hProcess,&result);CloseHandle(child.hProcess);
        check(result==0,"x64 host -> x86 helper -> DX9 overlay/control test failed");
        device.Reset();api.Reset();DestroyWindow(window);
        std::cout<<"x86 DX9 cross-bitness loading, auto API selection, frame acknowledgement and stop passed.\n";
        return 0;
    } catch(const std::exception& error){std::cerr<<error.what()<<'\n';return 1;}
}
