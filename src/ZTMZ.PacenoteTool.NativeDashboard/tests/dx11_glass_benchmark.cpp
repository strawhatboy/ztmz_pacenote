// Opt-in offscreen hardware timing. Never injects, presents, or reads scene pixels.
#include "graphics/dx11/backend.hpp"
#include "graphics/render_session.hpp"
#include "lua_dashboard.hpp"
#include <dxgi.h>
#include <wrl/client.h>
#include <algorithm>
#include <chrono>
#include <fstream>
#include <iostream>
#include <numeric>
#include <thread>
using namespace ztmz;
using Microsoft::WRL::ComPtr;
namespace {
void check(HRESULT hr){if(FAILED(hr))throw std::runtime_error("DX11 benchmark call failed: "+std::to_string(hr));}
template<class T> void query(ID3D11DeviceContext* context,ID3D11Query* q,T& value) {
    const auto deadline=std::chrono::steady_clock::now()+std::chrono::seconds(10);
    for(;;){
        auto hr=context->GetData(q,&value,sizeof(value),D3D11_ASYNC_GETDATA_DONOTFLUSH);
        if(hr==S_OK)return;check(hr);
        if(std::chrono::steady_clock::now()>deadline)throw std::runtime_error("GPU timestamp query timed out");
        std::this_thread::sleep_for(std::chrono::milliseconds(1));
    }
}
double percentile(std::vector<double> values,double p){std::sort(values.begin(),values.end());return values[static_cast<size_t>(p*(values.size()-1))];}
Json measure(ID3D11Device* device,ID3D11DeviceContext* context,RenderSession& session,const Scene& scene,unsigned width,unsigned height) {
    D3D11_TEXTURE2D_DESC desc{};desc.Width=width;desc.Height=height;desc.MipLevels=desc.ArraySize=1;
    desc.Format=DXGI_FORMAT_R8G8B8A8_UNORM;desc.SampleDesc.Count=1;desc.BindFlags=D3D11_BIND_RENDER_TARGET;
    ComPtr<ID3D11Texture2D> texture;ComPtr<ID3D11RenderTargetView> target;
    check(device->CreateTexture2D(&desc,nullptr,&texture));check(device->CreateRenderTargetView(texture.Get(),nullptr,&target));
    const float background[4]={0.17f,0.3f,0.5f,1};Dx11Frame frame(target.Get());
    auto draw=[&]{
        auto warnings=session.render({width,height,1.f/60},frame,scene,{});
        if(!warnings.empty())throw std::runtime_error(*warnings.begin());
    };
    // Allow asynchronous font preparation and shader/resource creation to settle.
    for(int i=0;i<40;++i){context->ClearRenderTargetView(target.Get(),background);draw();std::this_thread::sleep_for(std::chrono::milliseconds(5));}
    constexpr unsigned samples=160;
    ComPtr<ID3D11Query> disjoint;D3D11_QUERY_DESC qdesc{D3D11_QUERY_TIMESTAMP_DISJOINT,0};check(device->CreateQuery(&qdesc,&disjoint));
    std::vector<ComPtr<ID3D11Query>> starts(samples),ends(samples);
    qdesc.Query=D3D11_QUERY_TIMESTAMP;
    for(unsigned i=0;i<samples;++i){check(device->CreateQuery(&qdesc,&starts[i]));check(device->CreateQuery(&qdesc,&ends[i]));}
    std::vector<double> cpu,gpu;context->Begin(disjoint.Get());
    for(unsigned i=0;i<samples;++i){
        // Restore background outside the measured HUD interval. No CPU pixel upload/readback.
        context->ClearRenderTargetView(target.Get(),background);
        context->End(starts[i].Get());auto start=std::chrono::steady_clock::now();draw();
        cpu.push_back(std::chrono::duration<double,std::milli>(std::chrono::steady_clock::now()-start).count());
        context->End(ends[i].Get());
    }
    context->End(disjoint.Get());context->Flush();
    D3D11_QUERY_DATA_TIMESTAMP_DISJOINT clock{};query(context,disjoint.Get(),clock);
    if(clock.Disjoint||!clock.Frequency)throw std::runtime_error("GPU clock changed during timing; rerun benchmark");
    for(unsigned i=0;i<samples;++i){UINT64 start=0,end=0;query(context,starts[i].Get(),start);query(context,ends[i].Get(),end);gpu.push_back(1000.0*double(end-start)/double(clock.Frequency));}
    return {{"width",width},{"height",height},{"samples",samples},{"gpuMedianMs",percentile(gpu,0.5)},
        {"gpuP95Ms",percentile(gpu,0.95)},{"cpuSubmitMedianMs",percentile(cpu,0.5)},{"cpuSubmitP95Ms",percentile(cpu,0.95)},
        {"captureCopies",frame.glassCapture.copies},{"captureWidth",frame.glassCapture.width},{"captureHeight",frame.glassCapture.height},
        {"capturePixels",static_cast<uint64_t>(frame.glassCapture.width)*frame.glassCapture.height}};
}
}
int main(int argc,char** argv){
    try {
        if(argc<2)throw std::runtime_error("Usage: dx11_glass_benchmark package.json [report.json]");
        ComPtr<ID3D11Device> device;ComPtr<ID3D11DeviceContext> context;
        auto hr=D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_HARDWARE,nullptr,0,nullptr,0,D3D11_SDK_VERSION,&device,nullptr,&context);
        if(FAILED(hr)){std::cerr<<"Hardware DX11 unavailable; no WARP performance substitution\n";return 77;}
        ComPtr<IDXGIDevice> dxgi;ComPtr<IDXGIAdapter> adapter;check(device.As(&dxgi));check(dxgi->GetAdapter(&adapter));
        DXGI_ADAPTER_DESC info{};check(adapter->GetDesc(&info));
        char name[512]{};WideCharToMultiByte(CP_UTF8,0,info.Description,-1,name,sizeof(name),nullptr,nullptr);
        std::ifstream input(argv[1]);Json package;input>>package;
        LuaDashboard lua(package,package.value("config",Json::object()));Scene source;source.width=1920;source.height=1080;
        source.layers=lua.update(package.value("telemetry",Json::object()),package.value("context",Json::object()),0,"");
        RenderSession session(createDx11Backend(device.Get(),context.Get()));
        Json report={{"adapter",name},{"vendorId",info.VendorId},{"deviceId",info.DeviceId},{"debugLayer",false},
            {"method","Offscreen hardware GPU timestamps around complete RenderSession HUD submission; 40 warmup frames, 160 measured; no Present, vsync, game workload or CPU scene readback"},
            {"results",Json::array()}};
        for(auto size:{std::pair{1920u,1080u},std::pair{2560u,1440u},std::pair{3840u,2160u}}){
            for(auto variant:{"glass-off","glass-zero-blur","glass-blur-4"}){
                auto scene=source;
                for(auto& layer:scene.layers)for(auto& command:layer.commands)if(command.value("type",std::string{})=="drawGlassPanel"){
                    if(std::string(variant)=="glass-off")command["type"]="fillRoundedRectangle";
                    else command["blur"]=std::string(variant)=="glass-zero-blur"?0:4;
                }
                auto result=measure(device.Get(),context.Get(),session,scene,size.first,size.second);result["variant"]=variant;
                report["results"].push_back(result);
                std::cout<<variant<<" "<<size.first<<"x"<<size.second<<": "<<result["gpuMedianMs"]<<" ms GPU median, "<<result["gpuP95Ms"]<<" ms p95\n";
            }
        }
        if(argc>2){std::ofstream output(argv[2]);output<<report.dump(2)<<'\n';if(!output)throw std::runtime_error("Cannot write report");}
        else std::cout<<report.dump(2)<<'\n';
        return 0;
    }catch(const std::exception& e){std::cerr<<e.what()<<'\n';return 1;}
}
