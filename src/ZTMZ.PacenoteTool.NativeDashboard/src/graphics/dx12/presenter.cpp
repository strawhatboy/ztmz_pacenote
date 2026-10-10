#include "hooks/dxgi_presenter.hpp"
#include "hooks/dx12_queue.hpp"
#include "hooks/native_dxgi_interface.hpp"
#include "graphics/dx12/backend.hpp"
#include "graphics/render_session.hpp"
#include "graphics/async_preparation.hpp"
#include <dxgi1_4.h>
#include <chrono>
namespace ztmz {
namespace {
using Microsoft::WRL::ComPtr;
class Dx12Presenter final:public DxgiPresenter {
    std::unique_ptr<RenderSession> renderer_;
    AsyncPreparation<RenderSession> preparation_;
    const char* source_="";
public:
    const char* queueSource() const noexcept override {return source_;}
    uint64_t submitted_=0;
    uint64_t submittedFrames() const noexcept override {return submitted_;}
    RenderApi api() const noexcept override{return RenderApi::Dx12;}
    void installAuxiliaryHooks() override{installDx12QueueCapture();}
    bool readyFor(IDXGISwapChain* swap) override {
        ComPtr<ID3D12Device> device;ComPtr<IDXGISwapChain3> modern;
        return SUCCEEDED(swap->GetDevice(IID_PPV_ARGS(&device))) && SUCCEEDED(swap->QueryInterface(IID_PPV_ARGS(&modern))) && dx12QueueFor(swap)!=nullptr;
    }
    void render(IDXGISwapChain* swap,const Scene& scene,const ImageAssets& images,float dt) override {
        auto nativeSwap=nativeDxgiInterface(swap);if(!nativeSwap)return;
        swap=nativeSwap.Get();
        ComPtr<IDXGISwapChain3> modern;if(FAILED(swap->QueryInterface(IID_PPV_ARGS(&modern)))) return;
        if(!renderer_) {
            if(!preparation_.started()) {
                ComPtr<ID3D12Device> device;if(FAILED(swap->GetDevice(IID_PPV_ARGS(&device)))) return;
                device=nativeDxgiInterface(device.Get());if(!device)return;
                auto queue=dx12QueueFor(swap);if(!queue) return;
                source_=dx12QueueSource(swap);
                DXGI_SWAP_CHAIN_DESC desc{};if(FAILED(swap->GetDesc(&desc))) return;
                // No swap-chain/back-buffer ownership on the worker: resize can
                // cancel adoption without retaining buffers needed by DXGI.
                preparation_.start([device,queue,format=desc.BufferDesc.Format,count=desc.BufferCount,images] {
                    const auto started=std::chrono::steady_clock::now();
                    auto renderer=std::make_unique<RenderSession>(createDx12Backend(device.Get(),queue.Get(),format,count),std::make_unique<PreparedContext>());
                    renderer->warmup(images);
                    auto ms=std::chrono::duration_cast<std::chrono::milliseconds>(std::chrono::steady_clock::now()-started).count();
                    auto message="ZTMZ DX12 background warmup: "+std::to_string(ms)+" ms\n";
                    OutputDebugStringA(message.c_str());return renderer;
                });
            }
            renderer_=preparation_.take();if(!renderer_)return;
        }
        ComPtr<ID3D12Resource> buffer;if(FAILED(swap->GetBuffer(modern->GetCurrentBackBufferIndex(),IID_PPV_ARGS(&buffer)))) return;
        auto desc=buffer->GetDesc();Dx12Frame frame(buffer.Get());renderer_->render({static_cast<unsigned>(desc.Width),desc.Height,dt},frame,scene,images);
        if(renderer_->lastFrameSubmitted()) ++submitted_;
    }
    void beforeResize() override{preparation_.cancel();renderer_.reset();} // release every back-buffer reference before ResizeBuffers
    void releaseDevice() noexcept override{preparation_.cancel();renderer_.reset();}
};
}
std::unique_ptr<DxgiPresenter> createDx12Presenter(){return std::make_unique<Dx12Presenter>();}
}
