#include "graphics/dx11/backend.hpp"
#include "graphics/render_session.hpp"
#include "lua_dashboard.hpp"
#include <d3d11sdklayers.h>
#include <d3dcompiler.h>
#include <wrl/client.h>
#include <algorithm>
#include <chrono>
#include <cstring>
#include <fstream>
#include <iostream>
#include <stdexcept>
#include <thread>
using namespace ztmz;
using Microsoft::WRL::ComPtr;
namespace {
void require(bool value,const char* message){if(!value)throw std::runtime_error(message);}
void check(HRESULT hr){require(SUCCEEDED(hr),"DX11 call failed");}
struct Surface {
    unsigned width,height;
    ComPtr<ID3D11Texture2D> texture;
    ComPtr<ID3D11RenderTargetView> target;
    std::vector<unsigned> backdrop;
    Surface(ID3D11Device* device,unsigned w,unsigned h):width(w),height(h),backdrop(w*h) {
        for(unsigned y=0;y<h;++y)for(unsigned x=0;x<w;++x){
            const bool checker=(x/16+y/16)%2;
            unsigned r=checker?50+180*x/w:25+75*x/w,g=checker?60+140*y/h:30+60*y/h,b=checker?185:65;
            backdrop[y*w+x]=r|(g<<8)|(b<<16)|0xff000000;
        }
        D3D11_TEXTURE2D_DESC desc{};desc.Width=w;desc.Height=h;desc.MipLevels=desc.ArraySize=1;
        desc.Format=DXGI_FORMAT_R8G8B8A8_UNORM;desc.SampleDesc.Count=1;desc.BindFlags=D3D11_BIND_RENDER_TARGET;
        D3D11_SUBRESOURCE_DATA data{backdrop.data(),w*4,0};
        check(device->CreateTexture2D(&desc,&data,&texture));check(device->CreateRenderTargetView(texture.Get(),nullptr,&target));
    }
    void reset(ID3D11DeviceContext* context){context->UpdateSubresource(texture.Get(),0,nullptr,backdrop.data(),width*4,0);}
    std::vector<unsigned> read(ID3D11Device* device,ID3D11DeviceContext* context){
        D3D11_TEXTURE2D_DESC desc{};texture->GetDesc(&desc);desc.Usage=D3D11_USAGE_STAGING;
        desc.BindFlags=0;desc.CPUAccessFlags=D3D11_CPU_ACCESS_READ;
        ComPtr<ID3D11Texture2D> staging;check(device->CreateTexture2D(&desc,nullptr,&staging));
        context->CopyResource(staging.Get(),texture.Get());D3D11_MAPPED_SUBRESOURCE map{};
        check(context->Map(staging.Get(),0,D3D11_MAP_READ,0,&map));
        std::vector<unsigned> result(width*height);
        for(unsigned y=0;y<height;++y)std::memcpy(result.data()+y*width,static_cast<const char*>(map.pData)+y*map.RowPitch,width*4);
        context->Unmap(staging.Get(),0);return result;
    }
};
void bitmap(const char* path,const std::vector<unsigned>& pixels,unsigned w,unsigned h){
    BITMAPFILEHEADER file{};file.bfType=0x4d42;file.bfOffBits=sizeof(file)+sizeof(BITMAPINFOHEADER);file.bfSize=file.bfOffBits+w*h*4;
    BITMAPINFOHEADER info{};info.biSize=sizeof(info);info.biWidth=w;info.biHeight=-static_cast<LONG>(h);
    info.biPlanes=1;info.biBitCount=32;info.biCompression=BI_RGB;
    std::ofstream out(path,std::ios::binary);out.write(reinterpret_cast<const char*>(&file),sizeof(file));out.write(reinterpret_cast<const char*>(&info),sizeof(info));
    for(auto pixel:pixels){unsigned bgra=(pixel&0xff00ff00)|((pixel&0xff)<<16)|((pixel>>16)&0xff);out.write(reinterpret_cast<const char*>(&bgra),4);}
    require(bool(out),"Cannot write BMP");
}
Scene testScene(float refraction=18,float blur=0) {
    Scene scene;scene.width=640;scene.height=360;
    scene.layers={{"glass","dynamic",Json::array({
        {{"type","drawGlassPanel"},{"x",80},{"y",60},{"width",480},{"height",240},{"radius",40},{"refraction",refraction},{"blur",blur},{"color",0}},
        {{"type","fillRectangle"},{"x",270},{"y",140},{"width",100},{"height",50},{"color",0xffff8040u}}
    })}};return scene;
}
std::vector<unsigned> render(RenderSession& session,Surface& surface,ID3D11Device* device,ID3D11DeviceContext* context,const Scene& scene,Dx11GlassCaptureStats* stats=nullptr) {
    surface.reset(context);Dx11Frame frame(surface.target.Get());
    const auto warnings=session.render({surface.width,surface.height,1.f/60},frame,scene,{});
    if(stats)*stats=frame.glassCapture;
    require(warnings.empty(),warnings.empty()?"":warnings.begin()->c_str());
    return surface.read(device,context);
}
void fallbackAndBoundsTests() {
    auto* context=ImGui::CreateContext();ImGui::GetIO().IniFilename=nullptr;ImGui::GetIO().LogFilename=nullptr;
    ImGui::GetIO().DisplaySize={640,360};
    unsigned char* pixels;int w,h;ImGui::GetIO().Fonts->GetTexDataAsRGBA32(&pixels,&w,&h);
    ImGui::NewFrame();auto scene=testScene();
    require(draw(scene).empty(),"Portable fallback rejected glass");
    require(ImGui::GetBackgroundDrawList()->VtxBuffer.Size>0,"Portable fallback invisible");
    auto& command=scene.layers[0].commands[0];command["radius"]=9999;command["blur"]=9999;command["refraction"]=9999;
    bool called=false;draw(scene,{}, {},[&](ImDrawList&,const GlassPanel& p){called=true;require(p.radius==120&&p.blur==32&&p.refraction==64,"Glass bounds not enforced");});
    require(called,"Glass callback missing");
    command["slant"]=9999;
    draw(scene,{},{},[&](ImDrawList&,const GlassPanel& p){require(p.slant==192&&p.radius==120,"Slant not bounded");});
    command["width"]=20;command["height"]=100;
    draw(scene,{},{},[&](ImDrawList&,const GlassPanel& p){require(p.slant==8&&p.radius==6,"Slanted radius exceeds inner width");});
    command["width"]=480;command["height"]=240;
    command["points"]=Json::array({80,60,560,60,560,300,80,300});
    draw(scene,{},{},[&](ImDrawList&,const GlassPanel& p){require(p.pointCount==4,"Polygon not forwarded");});
    command["points"]=Json::array({80,60,560,60,300,120,560,300,80,300});
    called=false;draw(scene,{},{},[&](auto&,auto&){called=true;});require(!called,"Concave glass accepted");
    command["points"]=Json::array({0,0,560,60,560,300,80,300});
    called=false;draw(scene,{},{},[&](auto&,auto&){called=true;});require(!called,"Glass points escaped capture bounds");
    command.erase("points");
    command["x"]=604.6451612903226;command["y"]=410.4;
    command["width"]=710.7096774193549;command["height"]=259.2;
    command["points"]=Json::array({723.7935483870968,410.4,1277.7290322580645,410.4,
        1315.3548387096776,458.4774193548387,1300.7225806451613,588.0774193548387,
        1235.9225806451614,669.6,633.9096774193549,669.6,604.6451612903226,640.3354838709678,
        642.2709677419355,508.64516129032256});
    called=false;draw(scene,{},{},[&](auto&,auto&){called=true;});require(called,"Fractional polygon rejected by float bounds");
    command.erase("points");
    command["width"]=-1;called=false;draw(scene,{},{},[&](auto&,auto&){called=true;});require(!called,"Inverted panel accepted");
    ImGui::EndFrame();ImGui::DestroyContext(context);
}
}
int main(int argc,char** argv) {
    try {
        fallbackAndBoundsTests();
        ComPtr<ID3D11Device> device;ComPtr<ID3D11DeviceContext> context;
        UINT flags=D3D11_CREATE_DEVICE_DEBUG;
        HRESULT hr=D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_WARP,nullptr,flags,nullptr,0,D3D11_SDK_VERSION,&device,nullptr,&context);
        if(FAILED(hr)){flags=0;check(D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_WARP,nullptr,flags,nullptr,0,D3D11_SDK_VERSION,&device,nullptr,&context));}
        ComPtr<ID3D11InfoQueue> messages;device.As(&messages);
        if(messages) messages->ClearStoredMessages();
        auto backend=createDx11Backend(device.Get(),context.Get());auto* backendView=backend.get();
        RenderSession session(std::move(backend));
        Surface surface(device.Get(),640,360);
        // Sentinel states deliberately differ from ImGui's state; ensure glass
        // leaves both the game target and pixel constant-buffer slot intact.
        D3D11_BUFFER_DESC bd{};bd.ByteWidth=16;bd.BindFlags=D3D11_BIND_CONSTANT_BUFFER;
        ComPtr<ID3D11Buffer> sentinel;check(device->CreateBuffer(&bd,nullptr,&sentinel));
        auto* buffer=sentinel.Get();context->PSSetConstantBuffers(0,1,&buffer);
        auto* target=surface.target.Get();context->OMSetRenderTargets(1,&target,nullptr);
        const char* compute="[numthreads(1,1,1)] void main() {}";
        ComPtr<ID3DBlob> bytecode;check(D3DCompile(compute,strlen(compute),nullptr,nullptr,nullptr,"main","cs_5_0",0,0,&bytecode,nullptr));
        ComPtr<ID3D11ComputeShader> computeSentinel;check(device->CreateComputeShader(bytecode->GetBufferPointer(),bytecode->GetBufferSize(),nullptr,&computeSentinel));
        context->CSSetShader(computeSentinel.Get(),nullptr,0);
        auto flat=render(session,surface,device.Get(),context.Get(),testScene(0));
        Dx11GlassCaptureStats regionalStats;
        auto refracted=render(session,surface,device.Get(),context.Get(),testScene(),&regionalStats);
        require(regionalStats.copies==1&&regionalStats.width==520&&regionalStats.height==280&&!regionalStats.multisampleResolve,
            "Compact panel did not use a padded regional copy");
        unsigned displaced=0;
        for(unsigned y=0;y<360;++y)for(unsigned x=0;x<640;++x){
            const auto index=y*640+x;
            if(x<78||x>562||y<58||y>302) require(refracted[index]==surface.backdrop[index],"Glass modified pixels outside its bounds");
            if(x>=272&&x<368&&y>=142&&y<188) require(refracted[index]==0xff4080ff,"Foreground instrument was blurred or refracted");
            if(flat[index]!=refracted[index])++displaced;
        }
        require(displaced>2000,"Refraction did not actually move the sampled background");
        require(flat[100*640+200]==surface.backdrop[100*640+200],"Zero-refraction clear interior changed source colours");
        ComPtr<ID3D11Buffer> restored;context->PSGetConstantBuffers(0,1,&restored);require(restored==sentinel,"PS buffer not restored");
        ComPtr<ID3D11RenderTargetView> restoredTarget;context->OMGetRenderTargets(1,&restoredTarget,nullptr);require(restoredTarget==surface.target,"Render target not restored");
        ComPtr<ID3D11ComputeShader> restoredShader;context->CSGetShader(&restoredShader,nullptr,nullptr);require(restoredShader==computeSentinel,"Game compute shader not restored");
        // The slanted mask must leave the two cut-away corners untouched.
        auto slanted=testScene();
        slanted.layers[0].commands[0]["slant"]=100;
        slanted.layers[0].commands[0]["radius"]=12;
        slanted.layers[0].commands[0]["color"]=0xff102030u;
        auto slantedPixels=render(session,surface,device.Get(),context.Get(),slanted);
        require(slantedPixels[100*640+100]==surface.backdrop[100*640+100],"Glass leaked into slanted top-left cutout");
        require(slantedPixels[270*640+540]==surface.backdrop[270*640+540],"Glass leaked into slanted bottom-right cutout");
        require(slantedPixels[180*640+200]!=surface.backdrop[180*640+200],"Slanted glass interior disappeared");
        // An asymmetric contour must mask the shoulder and both unequal right cuts.
        auto polygon=testScene();
        polygon.layers[0].commands[0]["points"]=Json::array({160,60,540,60,560,105,550,225,500,300,100,300,80,275,105,150});
        polygon.layers[0].commands[0]["color"]=0xff102030u;
        auto polygonPixels=render(session,surface,device.Get(),context.Get(),polygon);
        require(polygonPixels[100*640+100]==surface.backdrop[100*640+100],"Glass leaked into polygon shoulder");
        require(polygonPixels[280*640+540]==surface.backdrop[280*640+540],"Glass leaked into lower-right cut");
        require(polygonPixels[70*640+550]==surface.backdrop[70*640+550],"Glass leaked into upper-right cut");
        require(polygonPixels[180*640+200]!=surface.backdrop[180*640+200],"Polygon glass interior disappeared");
        // Circular corner fillets cut inward without moving the straight edges.
        auto rounded=testScene();
        rounded.layers[0].commands[0]["points"]=Json::array({80,60,560,60,560,300,80,300});
        rounded.layers[0].commands[0]["radius"]=30;
        rounded.layers[0].commands[0]["color"]=0xff102030u;
        auto roundedPixels=render(session,surface,device.Get(),context.Get(),rounded);
        require(roundedPixels[63*640+83]==surface.backdrop[63*640+83],"Rounded polygon retained sharp corner");
        require(roundedPixels[90*640+110]!=surface.backdrop[90*640+110],"Rounded polygon lost curved interior");
        require(roundedPixels[63*640+300]!=surface.backdrop[63*640+300],"Rounded polygon moved straight edge");
        auto blurred=render(session,surface,device.Get(),context.Get(),testScene(0,4));
        require(blurred[100*640+192]!=flat[100*640+192],"Blur did not filter backdrop edge");
        // A transparent full-viewport panel forces a full copy. Subsequent
        // panels must match the optimized region pixel-for-pixel, including
        // refraction/blur padding, screen-edge clamping and multiple panels.
        const auto originalBackdrop=surface.backdrop;
        for(int variant=0;variant<3;++variant) {
            // Poison previously copied pixels so missing padding cannot pass
            // by accidentally sampling last frame's identical checkerboard.
            for(auto& pixel:surface.backdrop)pixel^=0x005a7f39u;
            auto regional=testScene(64,32);
            if(variant==1){regional.layers[0].commands[0]["x"]=-25;regional.layers[0].commands[0]["y"]=-15;}
            if(variant==2){
                regional.layers[0].commands[0]["x"]=380;regional.layers[0].commands[0]["y"]=230;
                regional.layers[0].commands.push_back(Json{{"type","drawGlassPanel"},{"x",15},{"y",25},
                    {"width",120},{"height",60},{"radius",25},{"refraction",25},{"blur",3},{"color",0x30405060u}});
            }
            auto optimized=render(session,surface,device.Get(),context.Get(),regional);
            auto full=regional;full.layers[0].commands.insert(full.layers[0].commands.begin(),Json{
                {"type","drawGlassPanel"},{"x",0},{"y",0},{"width",640},{"height",360},{"radius",0},{"refraction",0},{"blur",0},{"color",0}});
            auto reference=render(session,surface,device.Get(),context.Get(),full);
            for(unsigned y=5;y<355;++y)for(unsigned x=5;x<635;++x)
                require(optimized[y*640+x]==reference[y*640+x],"Regional capture differs from full-scene reference");
        }
        surface.backdrop=originalBackdrop;
        // Toggle glass off on the same live backend: no stale capture may run.
        auto ordinary=testScene();ordinary.layers[0].commands.erase(0);
        for(int i=0;i<2;++i) {
            Dx11GlassCaptureStats stats;auto pixels=render(session,surface,device.Get(),context.Get(),ordinary,&stats);
            require(stats.copies==0&&stats.width==0&&stats.height==0,"Frame without glass copied the scene");
            require(pixels[100*640+200]==surface.backdrop[100*640+200],"Disabled glass left stale rendering");
        }
        auto invisible=testScene();invisible.layers[0].commands[0]["x"]=2000;
        Dx11GlassCaptureStats invisibleStats;render(session,surface,device.Get(),context.Get(),invisible,&invisibleStats);
        require(invisibleStats.copies==0,"Entirely offscreen glass copied the scene");
        // Overlapping HUD commands must not contaminate the captured scene.
        auto beforeHud=testScene(0);beforeHud.layers[0].commands.insert(beforeHud.layers[0].commands.begin(),
            Json{{"type","fillRectangle"},{"x",180},{"y",90},{"width",50},{"height",40},{"color",0xffff0000u}});
        auto snapshot=render(session,surface,device.Get(),context.Get(),beforeHud);
        require(snapshot[100*640+200]==flat[100*640+200],"Glass sampled earlier HUD commands instead of the scene");
        // Respect clipping even though DX11 callbacks bypass ImGui's normal scissor setup.
        auto clipped=testScene();clipped.layers[0].commands.insert(clipped.layers[0].commands.begin(),
            Json{{"type","clipRegionStart"},{"x",150},{"y",100},{"width",300},{"height",150}});
        auto clippedPixels=render(session,surface,device.Get(),context.Get(),clipped);
        require(clippedPixels[90*640+100]==surface.backdrop[90*640+100],"Glass ignored clip rectangle");
        session.invalidateDeviceObjects();
        auto recovered=render(session,surface,device.Get(),context.Get(),testScene());
        require(recovered==refracted,"Device-object invalidation changed output");
        Surface resized(device.Get(),960,540);auto resizedResult=render(session,resized,device.Get(),context.Get(),testScene());
        require(resizedResult[220*960+440]==0xff4080ff,"Resize/scene scaling broke sharp foreground");
        auto returned=render(session,surface,device.Get(),context.Get(),testScene());require(returned==refracted,"Resize-back left stale snapshot resources");
        auto glassOnly=testScene();glassOnly.layers[0].commands.erase(1);
        auto only=render(session,surface,device.Get(),context.Get(),glassOnly);
        require(only[62*640+200]!=surface.backdrop[62*640+200],"Glass-only callback list was dropped");
        // Resolve an MSAA scene entirely on the GPU before sampling it.
        UINT quality=0;check(device->CheckMultisampleQualityLevels(DXGI_FORMAT_R8G8B8A8_UNORM,4,&quality));
        if(quality) {
            D3D11_TEXTURE2D_DESC desc{};surface.texture->GetDesc(&desc);desc.SampleDesc.Count=4;
            ComPtr<ID3D11Texture2D> msaa;ComPtr<ID3D11RenderTargetView> msaaTarget;
            check(device->CreateTexture2D(&desc,nullptr,&msaa));check(device->CreateRenderTargetView(msaa.Get(),nullptr,&msaaTarget));
            const float blue[4]={0,0,1,1};context->ClearRenderTargetView(msaaTarget.Get(),blue);
            Dx11Frame frame(msaaTarget.Get());
            session.withContext([&]{require(bool(backendView->prepareGlass(frame)),"MSAA unexpectedly used fallback");});
            require(session.render({640,360,1.f/60},frame,testScene(),{}).empty(),"MSAA scene failed");
            require(frame.glassCapture.copies==1&&frame.glassCapture.multisampleResolve&&frame.glassCapture.width==640&&frame.glassCapture.height==360,
                "MSAA no longer uses the full-surface resolve");
            context->OMSetRenderTargets(0,nullptr,nullptr);
            context->ResolveSubresource(surface.texture.Get(),0,msaa.Get(),0,desc.Format);
            auto resolved=surface.read(device.Get(),context.Get());
            require(resolved[100*640+200]==0xffff0000,"MSAA glass lost scene background");
            require(resolved[150*640+300]==0xff4080ff,"MSAA glass blurred foreground");
        }
        {
            D3D11_TEXTURE2D_DESC desc{};surface.texture->GetDesc(&desc);desc.Format=DXGI_FORMAT_R32G32B32A32_FLOAT;
            ComPtr<ID3D11Texture2D> unusual;ComPtr<ID3D11RenderTargetView> unusualTarget;
            check(device->CreateTexture2D(&desc,nullptr,&unusual));check(device->CreateRenderTargetView(unusual.Get(),nullptr,&unusualTarget));
            Dx11Frame frame(unusualTarget.Get());
            session.withContext([&]{require(!backendView->prepareGlass(frame),"Unsupported format did not request translucent fallback");});
            require(session.render({640,360,1.f/60},frame,testScene(),{}).empty(),"Unsupported target fallback failed");
        }
        if(argc>=3) {
            const bool package=std::string(argv[1])=="--package";
            require(argc>=(package?4:3),"Expected JSON input and BMP output");
            std::ifstream input(argv[package?2:1]);Json source;input>>source;Scene scene;
            std::unique_ptr<LuaDashboard> lua;
            if(package){
                // Match DashboardClient: native Lua always emits logical
                // 1920x1080 drawing units, irrespective of raster resolution.
                scene.width=1920;scene.height=1080;
                Json config=source.value("config",Json::object());
                lua=std::make_unique<LuaDashboard>(source,config);
                scene.layers=lua->update(source.value("telemetry",Json::object()),source.value("context",Json::object()),0,"");
            }else require(scene.apply(source),"Scene snapshot rejected");
            Surface preview(device.Get(),scene.width,scene.height);
            std::vector<unsigned> result;
            // Font production is asynchronous; allow it to settle for previews.
            for(int i=0;i<30;++i){
                Dx11GlassCaptureStats stats;
                result=render(session,preview,device.Get(),context.Get(),scene,&stats);
                if(package&&source.value("requireGlass",false))require(stats.copies==1,"Dashboard package lost its glass fill");
                std::this_thread::sleep_for(std::chrono::milliseconds(20));
            }
            bitmap(argv[package?3:2],result,preview.width,preview.height);
        }else bitmap("dx11-glass-preview.bmp",refracted,640,360);
        if(messages)for(UINT64 i=0;i<messages->GetNumStoredMessages();++i){
            SIZE_T size=0;messages->GetMessage(i,nullptr,&size);std::vector<char> storage(size);
            auto* message=reinterpret_cast<D3D11_MESSAGE*>(storage.data());messages->GetMessage(i,message,&size);
            if(message->Severity<=D3D11_MESSAGE_SEVERITY_WARNING)throw std::runtime_error(message->pDescription);
        }
        std::cout<<"WARP glass: "<<displaced<<" refracted pixels; sharp foreground, regional/full-copy equivalence, no-glass zero captures, GPU snapshot ordering, blur, clipping, state restoration, resize, invalidation and portable fallback passed. Debug layer "<<(messages?"clean":"unavailable")<<".\n";
        return 0;
    }catch(const std::exception& e){std::cerr<<e.what()<<'\n';return 1;}
}
