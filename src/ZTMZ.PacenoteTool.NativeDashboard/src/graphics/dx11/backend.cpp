#include "graphics/dx11/backend.hpp"
#include "graphics/dx11/dx11_images.hpp"
#include <imgui_impl_dx11.h>
#include <wrl/client.h>
#include <stdexcept>
namespace ztmz {
namespace {
using Microsoft::WRL::ComPtr;
class Dx11Backend final : public RenderBackend {
    ComPtr<ID3D11Device> device_;
    ComPtr<ID3D11DeviceContext> context_;
    Dx11Images images_;
    bool initialized_ = false;
public:
    Dx11Backend(ID3D11Device* device,ID3D11DeviceContext* context) : device_(device),context_(context) {
        if(!device || !context) throw std::invalid_argument("DX11 requires a device and immediate context");
    }
    RenderApi api() const noexcept override {return RenderApi::Dx11;}
    void initialize() override {
        if(!ImGui_ImplDX11_Init(device_.Get(),context_.Get())) throw std::runtime_error("ImGui DX11 initialization failed");
        initialized_=true;
    }
    void newFrame() override {ImGui_ImplDX11_NewFrame();}
    void syncImages(const ImageAssets& images) override {images_.sync(device_.Get(),images);}
    Texture texture(const std::string& key) const override {return images_.lookup(key);}
    void submit(ImDrawData& data,NativeFrame& native) override {
        const auto* frame=dynamic_cast<Dx11Frame*>(&native);
        if(!frame || !frame->target) throw std::invalid_argument("DX11 submission requires a render target");
        struct Targets {
            ID3D11DeviceContext* context;
            ID3D11RenderTargetView* views[D3D11_SIMULTANEOUS_RENDER_TARGET_COUNT]{};
            ID3D11DepthStencilView* depth=nullptr;
            explicit Targets(ID3D11DeviceContext* c) : context(c) {context->OMGetRenderTargets(D3D11_SIMULTANEOUS_RENDER_TARGET_COUNT,views,&depth);}
            ~Targets(){
                context->OMSetRenderTargets(D3D11_SIMULTANEOUS_RENDER_TARGET_COUNT,views,depth);
                for(auto* view:views) if(view) view->Release();
                if(depth) depth->Release();
            }
        } previous(context_.Get());
        context_->OMSetRenderTargets(1,&frame->target,nullptr);
        ImGui_ImplDX11_RenderDrawData(&data);
    }
    void invalidateDeviceObjects() override {images_.clear();if(initialized_) ImGui_ImplDX11_InvalidateDeviceObjects();}
    void shutdown() noexcept override {
        images_.clear();if(initialized_) ImGui_ImplDX11_Shutdown();initialized_=false;
    }
};
}
std::unique_ptr<RenderBackend> createDx11Backend(ID3D11Device* device,ID3D11DeviceContext* context) {
    return std::make_unique<Dx11Backend>(device,context);
}
}
