#include "hooks/dxgi_presenter.hpp"
#include "hooks/dx12_queue.hpp"
#include "graphics/dx12/backend.hpp"
#include "graphics/render_session.hpp"
#include <dxgi1_4.h>
namespace ztmz {
namespace {
using Microsoft::WRL::ComPtr;
class Dx12Presenter final:public DxgiPresenter {
    std::unique_ptr<RenderSession> renderer_;
public:
    RenderApi api() const noexcept override{return RenderApi::Dx12;}
    void installAuxiliaryHooks() override{installDx12QueueCapture();}
    bool readyFor(IDXGISwapChain* swap) override {
        ComPtr<ID3D12Device> device;ComPtr<IDXGISwapChain3> modern;
        return SUCCEEDED(swap->GetDevice(IID_PPV_ARGS(&device))) && SUCCEEDED(swap->QueryInterface(IID_PPV_ARGS(&modern))) && dx12QueueFor(swap)!=nullptr;
    }
    void render(IDXGISwapChain* swap,const Scene& scene,const ImageAssets& images,float dt) override {
        ComPtr<IDXGISwapChain3> modern;if(FAILED(swap->QueryInterface(IID_PPV_ARGS(&modern)))) return;
        if(!renderer_) {
            ComPtr<ID3D12Device> device;if(FAILED(swap->GetDevice(IID_PPV_ARGS(&device)))) return;
            auto queue=dx12QueueFor(swap);if(!queue) return;
            DXGI_SWAP_CHAIN_DESC desc{};if(FAILED(swap->GetDesc(&desc))) return;
            renderer_=std::make_unique<RenderSession>(createDx12Backend(device.Get(),queue.Get(),desc.BufferDesc.Format,desc.BufferCount));
        }
        ComPtr<ID3D12Resource> buffer;if(FAILED(swap->GetBuffer(modern->GetCurrentBackBufferIndex(),IID_PPV_ARGS(&buffer)))) return;
        auto desc=buffer->GetDesc();Dx12Frame frame(buffer.Get());renderer_->render({static_cast<unsigned>(desc.Width),desc.Height,dt},frame,scene,images);
    }
    void beforeResize() override{renderer_.reset();} // wait for fences, release every back-buffer reference before ResizeBuffers
    void releaseDevice() noexcept override{renderer_.reset();}
};
}
std::unique_ptr<DxgiPresenter> createDx12Presenter(){return std::make_unique<Dx12Presenter>();}
}
