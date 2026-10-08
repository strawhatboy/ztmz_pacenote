#include "hooks/dxgi_presenter.hpp"
#include "graphics/dx11/backend.hpp"
#include "graphics/render_session.hpp"
#include <wrl/client.h>
namespace ztmz {
namespace {
using Microsoft::WRL::ComPtr;
class Dx11Presenter final : public DxgiPresenter {
    ComPtr<ID3D11Device> device_;
    ComPtr<ID3D11DeviceContext> context_;
    std::unique_ptr<RenderSession> renderer_;
    uint64_t submitted_=0;
public:
    uint64_t submittedFrames() const noexcept override {return submitted_;}
    RenderApi api() const noexcept override {return RenderApi::Dx11;}
    void installAuxiliaryHooks() override {}
    bool readyFor(IDXGISwapChain* swap) override {
        ComPtr<ID3D11Device> device;
        return SUCCEEDED(swap->GetDevice(IID_PPV_ARGS(&device)));
    }
    void render(IDXGISwapChain* swap,const Scene& scene,const ImageAssets& images,float deltaSeconds) override {
        ComPtr<ID3D11Device> currentDevice;
        if(FAILED(swap->GetDevice(IID_PPV_ARGS(&currentDevice)))) return;
        // A released swap-chain address can be reused. Device identity is the
        // resource ownership boundary, even when the chain pointer looks unchanged.
        if(device_ && currentDevice.Get()!=device_.Get()) releaseDevice();
        if(!renderer_) {
            device_=currentDevice;
            device_->GetImmediateContext(&context_);
            renderer_=std::make_unique<RenderSession>(createDx11Backend(device_.Get(),context_.Get()));
        }
        ComPtr<ID3D11Texture2D> buffer;
        if(FAILED(swap->GetBuffer(0,IID_PPV_ARGS(&buffer)))) return;
        D3D11_TEXTURE2D_DESC desc{};buffer->GetDesc(&desc);
        ComPtr<ID3D11RenderTargetView> target;
        if(FAILED(device_->CreateRenderTargetView(buffer.Get(),nullptr,&target))) return;
        Dx11Frame frame(target.Get());
        renderer_->render({desc.Width,desc.Height,deltaSeconds},frame,scene,images);
        if(desc.Width && desc.Height) ++submitted_;
        // Release back-buffer references before returning to the game.
    }
    void beforeResize() override {if(renderer_) renderer_->invalidateDeviceObjects();}
    void releaseDevice() noexcept override {renderer_.reset();context_.Reset();device_.Reset();}
};
}
std::unique_ptr<DxgiPresenter> createDx11Presenter() {return std::make_unique<Dx11Presenter>();}
}
