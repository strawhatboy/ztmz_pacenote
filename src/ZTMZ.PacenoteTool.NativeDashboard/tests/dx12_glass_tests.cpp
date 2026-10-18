#include "graphics/dx12/backend.hpp"
#include "graphics/render_session.hpp"
#include <d3d12sdklayers.h>
#include <dxgi1_4.h>
#include <wrl/client.h>
#include <algorithm>
#include <cmath>
#include <cstring>
#include <functional>
#include <iostream>
#include <sstream>
#include <stdexcept>
#include <vector>

using namespace ztmz;
using Microsoft::WRL::ComPtr;

namespace {
constexpr unsigned Width=640,Height=360;
constexpr unsigned HudPixel=0xff4080ff;
void require(bool condition,const char* message){if(!condition)throw std::runtime_error(message);}
void check(HRESULT result){
    if(FAILED(result)){
        std::ostringstream message;message<<"DX12 test call failed: 0x"<<std::hex<<static_cast<unsigned>(result);
        throw std::runtime_error(message.str());
    }
}
D3D12_HEAP_PROPERTIES heap(D3D12_HEAP_TYPE type){
    D3D12_HEAP_PROPERTIES result{};result.Type=type;result.CreationNodeMask=result.VisibleNodeMask=1;return result;
}
D3D12_RESOURCE_DESC bufferDesc(UINT64 size){
    D3D12_RESOURCE_DESC result{};result.Dimension=D3D12_RESOURCE_DIMENSION_BUFFER;
    result.Width=size;result.Height=1;result.DepthOrArraySize=result.MipLevels=1;
    result.SampleDesc.Count=1;result.Layout=D3D12_TEXTURE_LAYOUT_ROW_MAJOR;return result;
}
void transition(ID3D12GraphicsCommandList* list,ID3D12Resource* resource,
    D3D12_RESOURCE_STATES from,D3D12_RESOURCE_STATES to){
    if(from==to)return;
    D3D12_RESOURCE_BARRIER barrier{};barrier.Type=D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
    barrier.Transition={resource,D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES,from,to};
    list->ResourceBarrier(1,&barrier);
}
struct Event {
    HANDLE handle=CreateEventW(nullptr,FALSE,FALSE,nullptr);
    Event(){require(handle!=nullptr,"Cannot create test fence event");}
    ~Event(){CloseHandle(handle);}
};
struct Gpu {
    ComPtr<ID3D12Device> device;
    ComPtr<ID3D12CommandQueue> queue;
    ComPtr<ID3D12CommandAllocator> allocator;
    ComPtr<ID3D12GraphicsCommandList> list;
    ComPtr<ID3D12Fence> fence;
    ComPtr<ID3D12InfoQueue> messages;
    Event completed;
    UINT64 sequence=0;

    Gpu(){
        ComPtr<ID3D12Debug> debug;
        if(SUCCEEDED(D3D12GetDebugInterface(IID_PPV_ARGS(&debug))))debug->EnableDebugLayer();
        ComPtr<IDXGIFactory4> factory;check(CreateDXGIFactory2(0,IID_PPV_ARGS(&factory)));
        ComPtr<IDXGIAdapter> warp;check(factory->EnumWarpAdapter(IID_PPV_ARGS(&warp)));
        check(D3D12CreateDevice(warp.Get(),D3D_FEATURE_LEVEL_11_0,IID_PPV_ARGS(&device)));
        D3D12_COMMAND_QUEUE_DESC description{};description.Type=D3D12_COMMAND_LIST_TYPE_DIRECT;
        check(device->CreateCommandQueue(&description,IID_PPV_ARGS(&queue)));
        check(device->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT,IID_PPV_ARGS(&allocator)));
        check(device->CreateCommandList(0,D3D12_COMMAND_LIST_TYPE_DIRECT,allocator.Get(),nullptr,IID_PPV_ARGS(&list)));
        check(list->Close());check(device->CreateFence(0,D3D12_FENCE_FLAG_NONE,IID_PPV_ARGS(&fence)));
        device.As(&messages);if(messages)messages->ClearStoredMessages();
    }
    void flush(){
        const auto value=++sequence;check(queue->Signal(fence.Get(),value));
        if(fence->GetCompletedValue()<value){
            check(fence->SetEventOnCompletion(value,completed.handle));
            const auto result=WaitForSingleObject(completed.handle,15000);
            check(device->GetDeviceRemovedReason());
            require(result==WAIT_OBJECT_0,"Timed out waiting for DX12 test queue");
        }
        check(device->GetDeviceRemovedReason());
    }
    void execute(const std::function<void(ID3D12GraphicsCommandList*)>& record){
        // Never reset this test allocator while its previous commands are in flight.
        flush();check(allocator->Reset());check(list->Reset(allocator.Get(),nullptr));
        record(list.Get());check(list->Close());
        ID3D12CommandList* commands[]{list.Get()};queue->ExecuteCommandLists(1,commands);flush();
    }
    void assertCleanDebugLayer(){
        flush();
        if(!messages)return;
        for(UINT64 i=0;i<messages->GetNumStoredMessages();++i){
            SIZE_T size=0;check(messages->GetMessage(i,nullptr,&size));
            std::vector<unsigned char> storage(size);
            auto* message=reinterpret_cast<D3D12_MESSAGE*>(storage.data());
            check(messages->GetMessage(i,message,&size));
            if(message->Severity<=D3D12_MESSAGE_SEVERITY_WARNING)
                throw std::runtime_error(message->pDescription);
        }
    }
};

struct Surface {
    unsigned width,height,bytesPerPixel;
    DXGI_FORMAT format;
    ComPtr<ID3D12Resource> texture,upload,readback;
    D3D12_PLACED_SUBRESOURCE_FOOTPRINT footprint{};
    UINT64 byteCount=0;
    std::vector<unsigned> backdrop;

    Surface(Gpu& gpu,unsigned w,unsigned h,DXGI_FORMAT f=DXGI_FORMAT_R8G8B8A8_UNORM)
        :width(w),height(h),bytesPerPixel(f==DXGI_FORMAT_R32G32B32A32_FLOAT?16:4),format(f),backdrop(w*h){
        for(unsigned y=0;y<h;++y)for(unsigned x=0;x<w;++x){
            const bool checker=(x/16+y/16)%2;
            const unsigned r=checker?50+180*x/w:25+75*x/w;
            const unsigned g=checker?60+140*y/h:30+60*y/h,b=checker?185:65;
            backdrop[y*w+x]=r|(g<<8)|(b<<16)|0xff000000;
        }
        D3D12_RESOURCE_DESC desc{};desc.Dimension=D3D12_RESOURCE_DIMENSION_TEXTURE2D;
        desc.Width=w;desc.Height=h;desc.DepthOrArraySize=desc.MipLevels=1;desc.Format=f;
        desc.SampleDesc.Count=1;desc.Flags=D3D12_RESOURCE_FLAG_ALLOW_RENDER_TARGET;
        auto properties=heap(D3D12_HEAP_TYPE_DEFAULT);
        check(gpu.device->CreateCommittedResource(&properties,D3D12_HEAP_FLAG_NONE,&desc,
            D3D12_RESOURCE_STATE_PRESENT,nullptr,IID_PPV_ARGS(&texture)));
        gpu.device->GetCopyableFootprints(&desc,0,1,0,&footprint,nullptr,nullptr,&byteCount);
        auto buffer=bufferDesc(byteCount);properties=heap(D3D12_HEAP_TYPE_UPLOAD);
        check(gpu.device->CreateCommittedResource(&properties,D3D12_HEAP_FLAG_NONE,&buffer,
            D3D12_RESOURCE_STATE_GENERIC_READ,nullptr,IID_PPV_ARGS(&upload)));
        properties=heap(D3D12_HEAP_TYPE_READBACK);
        check(gpu.device->CreateCommittedResource(&properties,D3D12_HEAP_FLAG_NONE,&buffer,
            D3D12_RESOURCE_STATE_COPY_DEST,nullptr,IID_PPV_ARGS(&readback)));
    }
    void reset(Gpu& gpu){
        gpu.flush();void* memory=nullptr;D3D12_RANGE empty{0,0};check(upload->Map(0,&empty,&memory));
        for(unsigned y=0;y<height;++y){
            auto* row=static_cast<unsigned char*>(memory)+footprint.Offset+size_t(y)*footprint.Footprint.RowPitch;
            if(bytesPerPixel==4)std::memcpy(row,backdrop.data()+size_t(y)*width,width*4);
            else for(unsigned x=0;x<width;++x){
                const auto pixel=backdrop[size_t(y)*width+x];
                float channels[4];for(unsigned c=0;c<4;++c)channels[c]=float((pixel>>(c*8))&255)/255;
                std::memcpy(row+x*16,channels,sizeof(channels));
            }
        }
        upload->Unmap(0,nullptr);
        gpu.execute([&](auto* list){
            transition(list,texture.Get(),D3D12_RESOURCE_STATE_PRESENT,D3D12_RESOURCE_STATE_COPY_DEST);
            D3D12_TEXTURE_COPY_LOCATION from{};from.pResource=upload.Get();
            from.Type=D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT;from.PlacedFootprint=footprint;
            D3D12_TEXTURE_COPY_LOCATION to{};to.pResource=texture.Get();to.Type=D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX;
            list->CopyTextureRegion(&to,0,0,0,&from,nullptr);
            transition(list,texture.Get(),D3D12_RESOURCE_STATE_COPY_DEST,D3D12_RESOURCE_STATE_PRESENT);
        });
    }
    std::vector<unsigned> read(Gpu& gpu,D3D12_RESOURCE_STATES state=D3D12_RESOURCE_STATE_PRESENT){
        gpu.execute([&](auto* list){
            transition(list,texture.Get(),state,D3D12_RESOURCE_STATE_COPY_SOURCE);
            D3D12_TEXTURE_COPY_LOCATION from{};from.pResource=texture.Get();from.Type=D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX;
            D3D12_TEXTURE_COPY_LOCATION to{};to.pResource=readback.Get();
            to.Type=D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT;to.PlacedFootprint=footprint;
            list->CopyTextureRegion(&to,0,0,0,&from,nullptr);
            transition(list,texture.Get(),D3D12_RESOURCE_STATE_COPY_SOURCE,state);
        });
        void* memory=nullptr;D3D12_RANGE range{0,static_cast<SIZE_T>(byteCount)};
        check(readback->Map(0,&range,&memory));std::vector<unsigned> pixels(width*height);
        for(unsigned y=0;y<height;++y){
            const auto* row=static_cast<const unsigned char*>(memory)+footprint.Offset+size_t(y)*footprint.Footprint.RowPitch;
            if(bytesPerPixel==4)std::memcpy(pixels.data()+size_t(y)*width,row,width*4);
            else for(unsigned x=0;x<width;++x){
                float channels[4];std::memcpy(channels,row+x*16,sizeof(channels));unsigned pixel=0;
                for(unsigned c=0;c<4;++c){
                    require(std::isfinite(channels[c]),"Float render target contains a non-finite component");
                    pixel|=static_cast<unsigned>(std::lround(std::clamp(channels[c],0.f,1.f)*255))<<(c*8);
                }
                pixels[size_t(y)*width+x]=pixel;
            }
        }
        D3D12_RANGE written{0,0};readback->Unmap(0,&written);return pixels;
    }
};

Scene testScene(float refraction=18,float blur=0){
    Scene scene;scene.width=Width;scene.height=Height;
    scene.layers={{"glass","dynamic",Json::array({
        {{"type","drawGlassPanel"},{"x",80},{"y",60},{"width",480},{"height",240},
            {"radius",40},{"refraction",refraction},{"blur",blur},{"color",0}},
        {{"type","fillRectangle"},{"x",270},{"y",140},{"width",100},{"height",50},{"color",0xffff8040u}}
    })}};return scene;
}
auto submit(RenderSession& session,Surface& surface,const Scene& scene){
    Dx12Frame frame(surface.texture.Get());
    // reset() leaves PRESENT; read() transitions from PRESENT after submission.
    // The debug layer verifies the backend's complete round trip between them.
    const auto warnings=session.render({surface.width,surface.height,1.f/60},frame,scene,{});
    require(warnings.empty(),warnings.empty()?"":warnings.begin()->c_str());
    require(session.lastFrameSubmitted(),"Ready DX12 frame was silently skipped");
    return frame.glassCapture;
}
std::vector<unsigned> render(Gpu& gpu,RenderSession& session,Surface& surface,const Scene& scene){
    surface.reset(gpu);submit(session,surface,scene);return surface.read(gpu);
}
void verifyOutsideAndHud(const Surface& surface,const std::vector<unsigned>& pixels){
    require(surface.width==Width&&surface.height==Height,"Pixel assertions require the base viewport");
    for(unsigned y=0;y<Height;++y)for(unsigned x=0;x<Width;++x){
        const auto i=size_t(y)*Width+x;
        if(x<78||x>562||y<58||y>302)
            require(pixels[i]==surface.backdrop[i],"Glass changed pixels outside its bounds");
        if(x>=272&&x<368&&y>=142&&y<188)
            require(pixels[i]==HudPixel,"Foreground HUD was blurred or refracted");
    }
}
// Release even on assertion failure, before the session destructor waits for GPU work.
struct QueueGate {
    ComPtr<ID3D12Fence> fence;
    explicit QueueGate(Gpu& gpu){
        check(gpu.device->CreateFence(0,D3D12_FENCE_FLAG_NONE,IID_PPV_ARGS(&fence)));
        check(gpu.queue->Wait(fence.Get(),1));
    }
    void release(){check(fence->Signal(1));}
    ~QueueGate(){if(fence)fence->Signal(1);}
};

void exercise(Gpu& gpu){
    RenderSession session(createDx12Backend(gpu.device.Get(),gpu.queue.Get(),DXGI_FORMAT_R8G8B8A8_UNORM,2));
    Surface surface(gpu,Width,Height);
    auto ordinary=testScene();ordinary.layers[0].commands.erase(0);
    surface.reset(gpu);const auto noGlass=submit(session,surface,ordinary);
    require(noGlass.copies==0&&noGlass.width==0&&noGlass.height==0,"Ordinary HUD captured the game scene");
    auto plain=surface.read(gpu);verifyOutsideAndHud(surface,plain);
    require(plain[100*Width+200]==surface.backdrop[100*Width+200],"No-glass frame modified the backdrop");

    auto flat=render(gpu,session,surface,testScene(0));
    surface.reset(gpu);auto stats=submit(session,surface,testScene());
    auto refracted=surface.read(gpu);
    require(stats.copies==1&&stats.width>0&&stats.width<=Width&&stats.height>0&&stats.height<=Height
        &&!stats.multisampleResolve,"Glass must capture exactly one valid single-sample scene");
    verifyOutsideAndHud(surface,refracted);
    unsigned displaced=0;for(size_t i=0;i<flat.size();++i)if(flat[i]!=refracted[i])++displaced;
    require(displaced>2000,"Refraction did not move a meaningful number of background pixels");
    require(flat[100*Width+200]==surface.backdrop[100*Width+200],"Clear zero-refraction interior changed source colours");
    // The slanted mask must leave the two cut-away corners untouched.
    auto slanted=testScene();
    slanted.layers[0].commands[0]["slant"]=100;
    slanted.layers[0].commands[0]["radius"]=12;
    slanted.layers[0].commands[0]["color"]=0xff102030u;
    auto slantedPixels=render(gpu,session,surface,slanted);
    require(slantedPixels[100*Width+100]==surface.backdrop[100*Width+100],"Glass leaked into slanted top-left cutout");
    require(slantedPixels[270*Width+540]==surface.backdrop[270*Width+540],"Glass leaked into slanted bottom-right cutout");
    require(slantedPixels[180*Width+200]!=surface.backdrop[180*Width+200],"Slanted glass interior disappeared");
    // An asymmetric contour must mask the shoulder and both unequal right cuts.
    auto polygon=testScene();
    polygon.layers[0].commands[0]["points"]=Json::array({160,60,540,60,560,105,550,225,500,300,100,300,80,275,105,150});
    polygon.layers[0].commands[0]["color"]=0xff102030u;
    auto polygonPixels=render(gpu,session,surface,polygon);
    require(polygonPixels[100*Width+100]==surface.backdrop[100*Width+100],"Glass leaked into polygon shoulder");
    require(polygonPixels[280*Width+540]==surface.backdrop[280*Width+540],"Glass leaked into lower-right cut");
    require(polygonPixels[70*Width+550]==surface.backdrop[70*Width+550],"Glass leaked into upper-right cut");
    require(polygonPixels[180*Width+200]!=surface.backdrop[180*Width+200],"Polygon glass interior disappeared");
    // Circular corner fillets cut inward without moving the straight edges.
    auto rounded=testScene();
    rounded.layers[0].commands[0]["points"]=Json::array({80,60,560,60,560,300,80,300});
    rounded.layers[0].commands[0]["radius"]=30;
    rounded.layers[0].commands[0]["color"]=0xff102030u;
    auto roundedPixels=render(gpu,session,surface,rounded);
    require(roundedPixels[63*Width+83]==surface.backdrop[63*Width+83],"Rounded polygon retained sharp corner");
    require(roundedPixels[90*Width+110]!=surface.backdrop[90*Width+110],"Rounded polygon lost curved interior");
    require(roundedPixels[63*Width+300]!=surface.backdrop[63*Width+300],"Rounded polygon moved straight edge");
    auto blurred=render(gpu,session,surface,testScene(0,4));verifyOutsideAndHud(surface,blurred);
    unsigned filtered=0;
    for(unsigned y=110;y<250;++y)for(unsigned x=130;x<510;++x)
        if(flat[y*Width+x]!=blurred[y*Width+x])++filtered;
    require(filtered>1000,"Blur did not filter the interior checkerboard");

    // Capture must precede all HUD commands, including commands before the first glass callback.
    auto beforeHud=testScene(0);
    beforeHud.layers[0].commands.insert(beforeHud.layers[0].commands.begin(),Json{
        {"type","fillRectangle"},{"x",180},{"y",90},{"width",50},{"height",40},{"color",0xffff0000u}});
    auto ordered=render(gpu,session,surface,beforeHud);
    require(ordered[100*Width+200]==flat[100*Width+200],"Glass sampled an earlier HUD draw instead of the game scene");
    auto clipped=testScene();clipped.layers[0].commands.insert(clipped.layers[0].commands.begin(),Json{
        {"type","clipRegionStart"},{"x",150},{"y",100},{"width",300},{"height",150}});
    clipped.layers[0].commands.push_back(Json{{"type","clipRegionEnd"}});
    auto clippedPixels=render(gpu,session,surface,clipped);
    require(clippedPixels[90*Width+100]==surface.backdrop[90*Width+100],"Glass ignored the active clip rectangle");
    require(clippedPixels[150*Width+300]==HudPixel,"Clipping damaged the foreground draw");

    // A callback-only draw list must still be submitted.
    auto glassOnly=testScene();glassOnly.layers[0].commands.erase(1);
    const auto only=render(gpu,session,surface,glassOnly);
    require(only[62*Width+200]!=surface.backdrop[62*Width+200],"Glass-only callback list was lost");

    // Change the game image, not just telemetry, before reusing snapshot resources.
    Surface second(gpu,Width,Height);for(auto& pixel:second.backdrop)pixel^=0x005a7f39u;
    const auto secondExpected=render(gpu,session,second,testScene());
    require(secondExpected!=refracted,"Second frame fixture did not change the scene");
    surface.reset(gpu);second.reset(gpu);
    {
        QueueGate gate(gpu);
        const auto firstStats=submit(session,surface,testScene());
        const auto secondStats=submit(session,second,testScene());
        require(firstStats.copies==1&&secondStats.copies==1,"Queued frames did not each capture their own scene");
        Dx12Frame busy(surface.texture.Get());
        require(session.render({Width,Height,1.f/60},busy,testScene(),{}).empty(),"Busy frame emitted warnings");
        require(!session.lastFrameSubmitted(),"Backend reused an allocator before its fence completed");
        gate.release();gpu.flush();
    }
    require(surface.read(gpu)==refracted,"First in-flight frame used another frame's snapshot/descriptors");
    require(second.read(gpu)==secondExpected,"Second in-flight frame sampled stale scene pixels");

    // Reuse the exact NativeFrame object: stats must describe this frame, not a previous capture.
    surface.reset(gpu);Dx12Frame reused(surface.texture.Get());
    require(session.render({Width,Height,1.f/60},reused,testScene(),{}).empty(),"Glass frame failed");
    require(reused.glassCapture.copies==1,"Initial reused-frame capture missing");gpu.flush();surface.reset(gpu);
    require(session.render({Width,Height,1.f/60},reused,ordinary,{}).empty(),"Disabled glass frame failed");
    require(session.lastFrameSubmitted(),"Ordinary frame after glass was skipped");
    require(reused.glassCapture.copies==0&&reused.glassCapture.width==0&&reused.glassCapture.height==0,
        "Disabling glass retained stale capture stats");
    require(surface.read(gpu)==plain,"Disabling glass left a stale shader callback");

    session.invalidateDeviceObjects();
    require(render(gpu,session,surface,testScene())==refracted,"Invalidation changed glass output");
    {
        Surface resized(gpu,960,540);auto pixels=render(gpu,session,resized,testScene());
        require(pixels[220*960+440]==HudPixel,"Resize lost sharp foreground or scene scaling");
        require(pixels[10*960+10]==resized.backdrop[10*960+10],"Resize modified pixels outside the panel");
    }
    require(render(gpu,session,surface,testScene())==refracted,"Resize-back retained incompatible snapshot resources");
    gpu.flush();
    std::cout<<"DX12 WARP: "<<displaced<<" refracted pixels, "<<filtered<<" blur-filtered pixels; "
        "HUD, bounds, ordering, clipping, fences, states, zero-capture, invalidation and resize passed.\n";
}

void unsupportedFormat(Gpu& gpu){
    // Match backend PSO and target format: the fallback concerns glass sampling,
    // not an invalid RTV/PSO combination that the debug layer would reject.
    constexpr auto format=DXGI_FORMAT_R32G32B32A32_FLOAT;
    RenderSession session(createDx12Backend(gpu.device.Get(),gpu.queue.Get(),format,2));
    Surface surface(gpu,Width,Height,format);surface.reset(gpu);
    auto scene=testScene();scene.layers[0].commands[0]["color"]=0xff284878u;
    const auto stats=submit(session,surface,scene);
    require(stats.copies==0,"Unsupported glass format tried to capture the target");
    const auto pixels=surface.read(gpu);
    require(pixels[100*Width+200]==0xff784828,"Unsupported format did not draw the ordinary coloured fallback");
    require(pixels[150*Width+300]==HudPixel,"Unsupported-format fallback lost foreground HUD");
    require(pixels[10*Width+10]==surface.backdrop[10*Width+10],"Unsupported-format fallback changed outside pixels");
    gpu.flush();std::cout<<"DX12 unsupported-format rounded fallback passed.\n";
}
}

int main(){
    try{
        Gpu gpu;exercise(gpu);unsupportedFormat(gpu);gpu.assertCleanDebugLayer();
        std::cout<<"DX12 debug layer "<<(gpu.messages?"clean":"unavailable")<<".\n";
        return 0;
    }catch(const std::exception& error){std::cerr<<error.what()<<'\n';return 1;}
}
