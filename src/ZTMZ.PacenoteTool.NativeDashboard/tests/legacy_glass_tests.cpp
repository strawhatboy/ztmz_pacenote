#include "graphics/render_session.hpp"
#ifdef TEST_DX9
#include "graphics/dx9/backend.hpp"
#else
#include "graphics/dx10/backend.hpp"
#include <d3d10sdklayers.h>
#endif
#include <wrl/client.h>
#include <algorithm>
#include <cstring>
#include <iostream>
#include <stdexcept>
#include <source_location>
using namespace ztmz;
using Microsoft::WRL::ComPtr;
void require(bool value,const char* reason){if(!value)throw std::runtime_error(reason);}
void check(HRESULT hr,const std::source_location location=std::source_location::current()){
    if(FAILED(hr))throw std::runtime_error("Legacy DX call failed at line "+std::to_string(location.line())+": "+std::to_string(hr));
}
struct Stats {unsigned copies=0,width=0,height=0;bool multisampleResolve=false;};
struct Fixture {
#ifdef TEST_DX9
    HWND window=nullptr;ComPtr<IDirect3D9> api;ComPtr<IDirect3DDevice9> device;D3DPRESENT_PARAMETERS params{};
    Fixture(){
        window=CreateWindowExW(WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE,L"STATIC",L"Offscreen glass test",WS_POPUP,0,0,640,360,nullptr,nullptr,GetModuleHandleW(nullptr),nullptr);
        require(window!=nullptr,"Cannot create hidden DX9 device window");api.Attach(Direct3DCreate9(D3D_SDK_VERSION));require(bool(api),"DX9 unavailable");
        D3DADAPTER_IDENTIFIER9 adapter{};api->GetAdapterIdentifier(0,0,&adapter);
        D3DCAPS9 caps{};auto capabilities=api->GetDeviceCaps(0,D3DDEVTYPE_HAL,&caps);
        std::cout<<"DX9 adapter: "<<adapter.Description<<"; caps HRESULT "<<capabilities<<"; vertex/pixel shader "<<caps.VertexShaderVersion<<"/"<<caps.PixelShaderVersion<<'\n';
        params.Windowed=TRUE;params.SwapEffect=D3DSWAPEFFECT_DISCARD;params.hDeviceWindow=window;
        params.BackBufferWidth=640;params.BackBufferHeight=360;params.BackBufferFormat=D3DFMT_UNKNOWN;params.PresentationInterval=D3DPRESENT_INTERVAL_IMMEDIATE;
        auto result=api->CreateDevice(0,D3DDEVTYPE_HAL,window,D3DCREATE_HARDWARE_VERTEXPROCESSING,&params,&device);
        if(FAILED(result))result=api->CreateDevice(0,D3DDEVTYPE_HAL,window,D3DCREATE_SOFTWARE_VERTEXPROCESSING,&params,&device);
        check(result);
    }
    ~Fixture(){device.Reset();api.Reset();if(window)DestroyWindow(window);}
    std::unique_ptr<RenderBackend> backend(){return createDx9Backend(device.Get());}
    void resetDevice(){
        ComPtr<IDirect3DSurface9> back;check(device->GetBackBuffer(0,0,D3DBACKBUFFER_TYPE_MONO,&back));
        check(device->SetRenderTarget(0,back.Get()));back.Reset();device->SetDepthStencilSurface(nullptr);
        device->SetStreamSource(0,nullptr,0,0);device->SetIndices(nullptr);device->SetTexture(0,nullptr);
        device->SetVertexShader(nullptr);device->SetPixelShader(nullptr);check(device->Reset(&params));
    }
    void debugCheck(){}
#else
    ComPtr<ID3D10Device> device;ComPtr<ID3D10InfoQueue> messages;
    Fixture(){
        auto hr=D3D10CreateDevice(nullptr,D3D10_DRIVER_TYPE_WARP,nullptr,D3D10_CREATE_DEVICE_DEBUG,D3D10_SDK_VERSION,&device);
        if(FAILED(hr))check(D3D10CreateDevice(nullptr,D3D10_DRIVER_TYPE_WARP,nullptr,0,D3D10_SDK_VERSION,&device));
        device.As(&messages);if(messages)messages->ClearStoredMessages();
    }
    std::unique_ptr<RenderBackend> backend(){return createDx10Backend(device.Get());}
    void resetDevice(){} // DX10 recreates device objects rather than D3D9 Reset.
    void debugCheck(){if(messages)for(UINT64 i=0;i<messages->GetNumStoredMessages();++i){
        SIZE_T size=0;messages->GetMessage(i,nullptr,&size);std::vector<char> bytes(size);auto* message=reinterpret_cast<D3D10_MESSAGE*>(bytes.data());
        messages->GetMessage(i,message,&size);if(message->Severity<=D3D10_MESSAGE_SEVERITY_WARNING)throw std::runtime_error(message->pDescription);
    }}
#endif
};
struct Surface {
    Fixture& fixture;unsigned width,height;std::vector<unsigned> backdrop;
#ifdef TEST_DX9
    ComPtr<IDirect3DSurface9> target;
#else
    ComPtr<ID3D10Texture2D> texture;ComPtr<ID3D10RenderTargetView> target;
#endif
    Surface(Fixture& f,unsigned w,unsigned h):fixture(f),width(w),height(h),backdrop(w*h){
        for(unsigned y=0;y<h;++y)for(unsigned x=0;x<w;++x){bool on=(x/16+y/16)%2;backdrop[y*w+x]=0xff000000u|((on?185:65)<<16)|((on?60+140*y/h:30+60*y/h)<<8)|(on?50+180*x/w:25+75*x/w);}
#ifdef TEST_DX9
        check(f.device->CreateRenderTarget(w,h,D3DFMT_A8R8G8B8,D3DMULTISAMPLE_NONE,0,FALSE,&target,nullptr));
#else
        D3D10_TEXTURE2D_DESC desc{};desc.Width=w;desc.Height=h;desc.MipLevels=desc.ArraySize=1;desc.Format=DXGI_FORMAT_R8G8B8A8_UNORM;
        desc.SampleDesc.Count=1;desc.BindFlags=D3D10_BIND_RENDER_TARGET;check(f.device->CreateTexture2D(&desc,nullptr,&texture));
        check(f.device->CreateRenderTargetView(texture.Get(),nullptr,&target));
#endif
    }
    void reset(){
#ifdef TEST_DX9
        ComPtr<IDirect3DSurface9> staging;check(fixture.device->CreateOffscreenPlainSurface(width,height,D3DFMT_A8R8G8B8,D3DPOOL_SYSTEMMEM,&staging,nullptr));
        D3DLOCKED_RECT map{};check(staging->LockRect(&map,nullptr,0));
        for(unsigned y=0;y<height;++y){auto* out=reinterpret_cast<unsigned*>(static_cast<char*>(map.pBits)+y*map.Pitch);
            for(unsigned x=0;x<width;++x){auto p=backdrop[y*width+x];out[x]=(p&0xff00ff00)|((p&255)<<16)|((p>>16)&255);}}
        check(staging->UnlockRect());check(fixture.device->UpdateSurface(staging.Get(),nullptr,target.Get(),nullptr));
#else
        fixture.device->UpdateSubresource(texture.Get(),0,nullptr,backdrop.data(),width*4,0);
#endif
    }
    std::vector<unsigned> read(){
        std::vector<unsigned> pixels(width*height);
#ifdef TEST_DX9
        ComPtr<IDirect3DSurface9> staging;check(fixture.device->CreateOffscreenPlainSurface(width,height,D3DFMT_A8R8G8B8,D3DPOOL_SYSTEMMEM,&staging,nullptr));
        check(fixture.device->GetRenderTargetData(target.Get(),staging.Get()));D3DLOCKED_RECT map{};check(staging->LockRect(&map,nullptr,D3DLOCK_READONLY));
        for(unsigned y=0;y<height;++y){auto* row=reinterpret_cast<unsigned*>(static_cast<char*>(map.pBits)+y*map.Pitch);
            for(unsigned x=0;x<width;++x){auto p=row[x];pixels[y*width+x]=(p&0xff00ff00)|((p&255)<<16)|((p>>16)&255);}}
        staging->UnlockRect();
#else
        D3D10_TEXTURE2D_DESC desc{};texture->GetDesc(&desc);desc.Usage=D3D10_USAGE_STAGING;desc.BindFlags=0;desc.CPUAccessFlags=D3D10_CPU_ACCESS_READ;
        ComPtr<ID3D10Texture2D> staging;check(fixture.device->CreateTexture2D(&desc,nullptr,&staging));fixture.device->CopyResource(staging.Get(),texture.Get());
        D3D10_MAPPED_TEXTURE2D map{};check(staging->Map(0,D3D10_MAP_READ,0,&map));
        for(unsigned y=0;y<height;++y)std::memcpy(pixels.data()+y*width,static_cast<char*>(map.pData)+y*map.RowPitch,width*4);
        staging->Unmap(0);
#endif
        return pixels;
    }
};
Scene scene(float refraction=18,float blur=0){
    Scene s;s.width=640;s.height=360;s.layers={{"glass","dynamic",Json::array({
        {{"type","drawGlassPanel"},{"x",80},{"y",60},{"width",480},{"height",240},{"radius",40},{"refraction",refraction},{"blur",blur},{"color",0}},
        {{"type","fillRectangle"},{"x",270},{"y",140},{"width",100},{"height",50},{"color",0xffff8040u}}})}};return s;
}
std::vector<unsigned> render(RenderSession& session,Surface& target,const Scene& s,Stats* stats=nullptr){
    target.reset();
#ifdef TEST_DX9
    Dx9Frame frame(target.target.Get());
#else
    Dx10Frame frame(target.target.Get());
#endif
    require(session.render({target.width,target.height,1.f/60},frame,s,{}).empty(),"Glass scene produced warnings");
    if(stats)*stats={frame.glassCapture.copies,frame.glassCapture.width,frame.glassCapture.height,frame.glassCapture.multisampleResolve};
    return target.read();
}
void msaaTest(Fixture& fixture,RenderSession& session,Surface& target){
#ifdef TEST_DX9
    DWORD quality=0;
    if(FAILED(fixture.api->CheckDeviceMultiSampleType(0,D3DDEVTYPE_HAL,D3DFMT_A8R8G8B8,TRUE,D3DMULTISAMPLE_4_SAMPLES,&quality))||!quality)return;
    ComPtr<IDirect3DSurface9> msaa,previous,depth;
    check(fixture.device->CreateRenderTarget(640,360,D3DFMT_A8R8G8B8,D3DMULTISAMPLE_4_SAMPLES,0,FALSE,&msaa,nullptr));
    fixture.device->GetRenderTarget(0,&previous);fixture.device->GetDepthStencilSurface(&depth);
    check(fixture.device->SetRenderTarget(0,msaa.Get()));fixture.device->SetDepthStencilSurface(nullptr);
    check(fixture.device->Clear(0,nullptr,D3DCLEAR_TARGET,D3DCOLOR_XRGB(0,0,255),1,0));
    check(fixture.device->SetRenderTarget(0,previous.Get()));fixture.device->SetDepthStencilSurface(depth.Get());
    Dx9Frame frame(msaa.Get());
#else
    UINT quality=0;
    check(fixture.device->CheckMultisampleQualityLevels(DXGI_FORMAT_R8G8B8A8_UNORM,4,&quality));if(!quality)return;
    D3D10_TEXTURE2D_DESC desc{};target.texture->GetDesc(&desc);desc.SampleDesc.Count=4;
    ComPtr<ID3D10Texture2D> msaa;ComPtr<ID3D10RenderTargetView> msaaTarget;
    check(fixture.device->CreateTexture2D(&desc,nullptr,&msaa));check(fixture.device->CreateRenderTargetView(msaa.Get(),nullptr,&msaaTarget));
    const float blue[4]={0,0,1,1};fixture.device->ClearRenderTargetView(msaaTarget.Get(),blue);Dx10Frame frame(msaaTarget.Get());
#endif
    require(session.render({640,360,1.f/60},frame,scene(),{}).empty(),"MSAA rendering failed");
    require(frame.glassCapture.copies==1&&frame.glassCapture.multisampleResolve&&frame.glassCapture.width==640&&frame.glassCapture.height==360,"MSAA did not resolve the full scene");
#ifdef TEST_DX9
    check(fixture.device->StretchRect(msaa.Get(),nullptr,target.target.Get(),nullptr,D3DTEXF_NONE));
#else
    fixture.device->ResolveSubresource(target.texture.Get(),0,msaa.Get(),0,DXGI_FORMAT_R8G8B8A8_UNORM);
#endif
    auto pixels=target.read();require(pixels[100*640+200]==0xffff0000u,"MSAA lost background");
    require(pixels[150*640+300]==0xff4080ffu,"MSAA distorted foreground");
}
int main(){try{
    Fixture fixture;auto backend=fixture.backend();auto* backendView=backend.get();RenderSession session(std::move(backend));
    auto target=std::make_unique<Surface>(fixture,640,360);
    const float sentinelValues[16]={.125f,.25f,.5f,1,2,3,4,5,6,7,8,9,10,11,12,13};
#ifdef TEST_DX9
    check(fixture.device->SetPixelShaderConstantF(0,sentinelValues,4));check(fixture.device->SetVertexShaderConstantF(0,sentinelValues,4));
    check(fixture.device->SetFVF(D3DFVF_XYZ));
#else
    D3D10_BUFFER_DESC bd{};bd.ByteWidth=64;bd.BindFlags=D3D10_BIND_CONSTANT_BUFFER;
    D3D10_SUBRESOURCE_DATA initial{sentinelValues,0,0};ComPtr<ID3D10Buffer> sentinel;check(fixture.device->CreateBuffer(&bd,&initial,&sentinel));
    auto* buffer=sentinel.Get();fixture.device->PSSetConstantBuffers(0,1,&buffer);
#endif
    auto only=scene();only.layers[0].commands.erase(1);Stats stats;
    auto onlyPixels=render(session,*target,only,&stats);
    require(stats.copies==1&&onlyPixels[62*640+200]!=target->backdrop[62*640+200],"Glass-only callback list did not render on a fresh backend");
    auto flat=render(session,*target,scene(0));auto refracted=render(session,*target,scene(),&stats);
    require(stats.copies==1&&stats.width==520&&stats.height==280,"Regional capture missing");unsigned changed=0;
    for(unsigned y=0;y<360;++y)for(unsigned x=0;x<640;++x){auto index=y*640+x;
        if(refracted[index]!=flat[index])++changed;
        if(x<78||x>562||y<58||y>302)require(refracted[index]==target->backdrop[index],"Glass changed outside pixels");
        if(x>=272&&x<368&&y>=142&&y<188)require(refracted[index]==0xff4080ff,"Foreground was distorted or state not restored");
    }
    require(changed>2000,"Refraction did not move background pixels");
    require(flat[100*640+200]==target->backdrop[100*640+200],"Clear glass altered interior source colors/texel alignment");
#ifdef TEST_DX9
    float restored[16]{};check(fixture.device->GetPixelShaderConstantF(0,restored,4));require(std::memcmp(restored,sentinelValues,sizeof(restored))==0,"DX9 PS constants leaked");
    check(fixture.device->GetVertexShaderConstantF(0,restored,4));require(std::memcmp(restored,sentinelValues,sizeof(restored))==0,"DX9 VS constants leaked");
    DWORD fvf=0;check(fixture.device->GetFVF(&fvf));require(fvf==D3DFVF_XYZ,"DX9 vertex declaration/FVF leaked");
#else
    ComPtr<ID3D10Buffer> restored;fixture.device->PSGetConstantBuffers(0,1,&restored);require(restored==sentinel,"DX10 PS constant buffer leaked");
#endif
    // The slanted mask must leave the two cut-away corners untouched.
    auto slanted=scene();
    slanted.layers[0].commands[0]["slant"]=100;
    slanted.layers[0].commands[0]["radius"]=12;
    slanted.layers[0].commands[0]["color"]=0xff102030u;
    auto slantedPixels=render(session,*target,slanted);
    require(slantedPixels[100*640+100]==target->backdrop[100*640+100],"Glass leaked into slanted top-left cutout");
    require(slantedPixels[270*640+540]==target->backdrop[270*640+540],"Glass leaked into slanted bottom-right cutout");
    require(slantedPixels[180*640+200]!=target->backdrop[180*640+200],"Slanted glass interior disappeared");
    // An asymmetric contour must mask the shoulder and both unequal right cuts.
    auto polygon=scene();
    polygon.layers[0].commands[0]["points"]=Json::array({160,60,540,60,560,105,550,225,500,300,100,300,80,275,105,150});
    polygon.layers[0].commands[0]["color"]=0xff102030u;
    auto polygonPixels=render(session,*target,polygon);
    require(polygonPixels[100*640+100]==target->backdrop[100*640+100],"Glass leaked into polygon shoulder");
    require(polygonPixels[280*640+540]==target->backdrop[280*640+540],"Glass leaked into lower-right cut");
    require(polygonPixels[70*640+550]==target->backdrop[70*640+550],"Glass leaked into upper-right cut");
    require(polygonPixels[180*640+200]!=target->backdrop[180*640+200],"Polygon glass interior disappeared");
    // Circular corner fillets cut inward without moving the straight edges.
    auto rounded=scene();
    rounded.layers[0].commands[0]["points"]=Json::array({80,60,560,60,560,300,80,300});
    rounded.layers[0].commands[0]["radius"]=30;
    rounded.layers[0].commands[0]["color"]=0xff102030u;
    auto roundedPixels=render(session,*target,rounded);
    require(roundedPixels[63*640+83]==target->backdrop[63*640+83],"Rounded polygon retained sharp corner");
    require(roundedPixels[90*640+110]!=target->backdrop[90*640+110],"Rounded polygon lost curved interior");
    require(roundedPixels[63*640+300]!=target->backdrop[63*640+300],"Rounded polygon moved straight edge");
    auto blurred=render(session,*target,scene(0,4));require(blurred[100*640+192]!=flat[100*640+192],"Blur was not applied");
    auto preHud=scene(0);preHud.layers[0].commands.insert(preHud.layers[0].commands.begin(),Json{{"type","fillRectangle"},{"x",180},{"y",90},{"width",50},{"height",40},{"color",0xffff0000u}});
    require(render(session,*target,preHud)[100*640+200]==flat[100*640+200],"Capture included earlier HUD commands");
    const auto original=target->backdrop;
    for(int variant=0;variant<3;++variant){
        for(auto& pixel:target->backdrop)pixel^=0x005a7f39u;
        auto compact=scene(64,32);if(variant==1){compact.layers[0].commands[0]["x"]=-25;compact.layers[0].commands[0]["y"]=-15;}
        if(variant==2){compact.layers[0].commands[0]["x"]=380;compact.layers[0].commands[0]["y"]=230;}
        auto optimized=render(session,*target,compact);auto full=compact;
        full.layers[0].commands.insert(full.layers[0].commands.begin(),Json{{"type","drawGlassPanel"},{"x",0},{"y",0},{"width",640},{"height",360},{"radius",0},{"refraction",0},{"blur",0},{"color",0}});
        auto reference=render(session,*target,full);for(unsigned y=5;y<355;++y)for(unsigned x=5;x<635;++x)require(optimized[y*640+x]==reference[y*640+x],"Regional capture differs from full reference");
    }
    target->backdrop=original;
    auto ordinary=scene();ordinary.layers[0].commands.erase(0);render(session,*target,ordinary,&stats);require(stats.copies==0,"Disabled glass copied scene");
    auto invisible=scene();invisible.layers[0].commands[0]["x"]=2000;render(session,*target,invisible,&stats);require(stats.copies==0,"Invisible glass copied scene");
    auto clipped=scene();clipped.layers[0].commands.insert(clipped.layers[0].commands.begin(),Json{{"type","clipRegionStart"},{"x",150},{"y",100},{"width",300},{"height",150}});
    require(render(session,*target,clipped)[90*640+100]==target->backdrop[90*640+100],"Glass ignored clip rectangle");
    {Surface wrong(fixture,128,128);
#ifdef TEST_DX9
        Dx9Frame frame(wrong.target.Get());
#else
        Dx10Frame frame(wrong.target.Get());
#endif
        session.withContext([&]{require(!backendView->prepareGlass(frame),"Mismatched target failed to request fallback");});
    }
    // Exercise the actual translucent fallback (rather than just checking an
    // empty prepare callback): mismatched viewport/target deliberately rejects
    // scene capture, but normal rounded fill and foreground must remain visible.
    {
        target->reset();auto fallback=scene();fallback.layers[0].commands[0]["color"]=0xcc102030u;
#ifdef TEST_DX9
        Dx9Frame frame(target->target.Get());
#else
        Dx10Frame frame(target->target.Get());
#endif
        require(session.render({320,180,1.f/60},frame,fallback,{}).empty(),"Fallback renderer rejected panel");
        auto pixels=target->read();require(frame.glassCapture.copies==0,"Unsupported target still captured scene");
        require(pixels[60*640+100]!=target->backdrop[60*640+100],"Translucent fallback was invisible");
        require(pixels[80*640+150]==0xff4080ff,"Fallback damaged foreground");
        require(pixels[300*640+500]==target->backdrop[300*640+500],"Fallback escaped viewport");
    }
    {Surface resized(fixture,960,540);require(render(session,resized,scene())[220*960+440]==0xff4080ff,"Resize lost sharp foreground");}
    session.invalidateDeviceObjects();require(render(session,*target,scene())==refracted,"Invalidation changed glass output");
    msaaTest(fixture,session,*target);
    session.invalidateDeviceObjects();target.reset();fixture.resetDevice();
    target=std::make_unique<Surface>(fixture,640,360);require(render(session,*target,scene())==refracted,"Device reset/recreation failed");
    fixture.debugCheck();std::cout<<"Legacy glass passed: "<<changed<<" refracted pixels; foreground, regional capture, zero capture when off, blur, clipping, pre-HUD capture, state restoration, resize and reset\n";return 0;
}catch(const std::exception& e){std::cerr<<e.what()<<'\n';return 1;}}
