#include "graphics/dx10/backend.hpp"
#include "graphics/dx10/dx10_images.hpp"
#include "graphics/dx10/glass.hpp"
#include <imgui_impl_dx10.h>
#include <wrl/client.h>
#include <stdexcept>
namespace ztmz {
namespace {
using Microsoft::WRL::ComPtr;
class Dx10Backend final : public RenderBackend {
    ComPtr<ID3D10Device> device_;
    Dx10Images images_;
    Dx10Glass glass_;
    bool initialized_ = false;
public:
    Dx10Backend(ID3D10Device* device) : device_(device),glass_(device) {
        if(!device) throw std::invalid_argument("DX10 requires a device");
    }
    RenderApi api() const noexcept override {return RenderApi::Dx10;}
    void initialize() override {
        if(!ImGui_ImplDX10_Init(device_.Get())) throw std::runtime_error("ImGui DX10 initialization failed");
        initialized_=true;
    }
    void newFrame() override {glass_.beginFrame();ImGui_ImplDX10_NewFrame();}
    void syncImages(const ImageAssets& images) override {images_.sync(device_.Get(),images);}
    Texture texture(const std::string& key) const override {return images_.lookup(key);}
    GlassDraw prepareGlass(NativeFrame& native) override {
        auto* frame=dynamic_cast<Dx10Frame*>(&native);return frame?glass_.prepare(frame->target):GlassDraw{};
    }
    void submit(ImDrawData& data,NativeFrame& native) override {
        auto* frame=dynamic_cast<Dx10Frame*>(&native);
        if(!frame || !frame->target) throw std::invalid_argument("DX10 submission requires a render target");
        frame->glassCapture=glass_.capture();
        struct Targets {
            ID3D10Device* context;
            ID3D10RenderTargetView* views[D3D10_SIMULTANEOUS_RENDER_TARGET_COUNT]{};
            ID3D10DepthStencilView* depth=nullptr;
            explicit Targets(ID3D10Device* c) : context(c) {context->OMGetRenderTargets(D3D10_SIMULTANEOUS_RENDER_TARGET_COUNT,views,&depth);}
            ~Targets(){
                context->OMSetRenderTargets(D3D10_SIMULTANEOUS_RENDER_TARGET_COUNT,views,depth);
                for(auto* view:views) if(view) view->Release();
                if(depth) depth->Release();
            }
        } previous(device_.Get());
        device_->OMSetRenderTargets(1,&frame->target,nullptr);
        ImGui_ImplDX10_RenderDrawData(&data);
    }
    void invalidateDeviceObjects() override {glass_.clear();images_.clear();if(initialized_) ImGui_ImplDX10_InvalidateDeviceObjects();}
    void shutdown() noexcept override {
        glass_.clear();images_.clear();if(initialized_) ImGui_ImplDX10_Shutdown();initialized_=false;
    }
};
}
std::unique_ptr<RenderBackend> createDx10Backend(ID3D10Device* device) {
    return std::make_unique<Dx10Backend>(device);
}
}
