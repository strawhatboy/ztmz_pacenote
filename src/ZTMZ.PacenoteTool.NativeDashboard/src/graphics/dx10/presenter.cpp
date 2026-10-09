#include "hooks/dxgi_presenter.hpp"
#include "graphics/dx10/backend.hpp"
#include "graphics/render_session.hpp"
#include "graphics/async_preparation.hpp"
#include <wrl/client.h>
namespace ztmz {
namespace {
using Microsoft::WRL::ComPtr;
class Dx10Presenter final : public DxgiPresenter {
    ComPtr<ID3D10Device> device_;
    std::unique_ptr<RenderSession> renderer_;
    AsyncPreparation<PreparedContext> preparation_;
public:
    uint64_t submitted_=0;
    uint64_t submittedFrames() const noexcept override {return submitted_;}
    RenderApi api() const noexcept override {return RenderApi::Dx10;}
    void installAuxiliaryHooks() override {}
    bool readyFor(IDXGISwapChain* swap) override {
        ComPtr<ID3D10Device> device;
        return SUCCEEDED(swap->GetDevice(IID_PPV_ARGS(&device)));
    }
    void render(IDXGISwapChain* swap,const Scene& scene,const ImageAssets& images,float deltaSeconds) override {
        if(!renderer_) {
            if(!preparation_.started())preparation_.start([]{return std::make_unique<PreparedContext>();});
            auto context=preparation_.take();if(!context)return;
            if(FAILED(swap->GetDevice(IID_PPV_ARGS(&device_)))) return;
            renderer_=std::make_unique<RenderSession>(createDx10Backend(device_.Get()),std::move(context));
        }
        ComPtr<ID3D10Texture2D> buffer;
        if(FAILED(swap->GetBuffer(0,IID_PPV_ARGS(&buffer)))) return;
        D3D10_TEXTURE2D_DESC desc{};buffer->GetDesc(&desc);
        ComPtr<ID3D10RenderTargetView> target;
        if(FAILED(device_->CreateRenderTargetView(buffer.Get(),nullptr,&target))) return;
        Dx10Frame frame(target.Get());
        renderer_->render({desc.Width,desc.Height,deltaSeconds},frame,scene,images);
        if(desc.Width && desc.Height) ++submitted_;
        // Release back-buffer references before returning to the game.
    }
    void beforeResize() override {} // no retained swap-chain targets; keep device-owned resources warm
    void releaseDevice() noexcept override {preparation_.cancel();renderer_.reset();device_.Reset();}
};
}
std::unique_ptr<DxgiPresenter> createDx10Presenter() {return std::make_unique<Dx10Presenter>();}
}
