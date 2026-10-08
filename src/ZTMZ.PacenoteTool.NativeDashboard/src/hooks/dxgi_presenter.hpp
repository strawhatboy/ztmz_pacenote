#pragma once
#include "graphics/render_api.hpp"
#include "scene.hpp"
#include "assets.hpp"
#include <dxgi.h>
#include <memory>
namespace ztmz {
// DX10/11/12 share one DXGI interception point. Per-API presenters provide the
// device/context/queue-specific bridge to RenderSession rather than re-hooking DXGI.
class DxgiPresenter {
public:
    virtual ~DxgiPresenter() = default;
    virtual RenderApi api() const noexcept = 0;
    // DX12 captures swapchain-to-queue associations here.
    virtual void installAuxiliaryHooks() = 0;
    virtual bool readyFor(IDXGISwapChain* swap) = 0;
    virtual void render(IDXGISwapChain* swap,const Scene& scene,const ImageAssets& images,float deltaSeconds) = 0;
    // Opt-in acknowledgement: uninstrumented adapters must not claim submitted frames.
    virtual uint64_t submittedFrames() const noexcept {return 0;}
    virtual void beforeResize() = 0;
    virtual void releaseDevice() noexcept = 0;
};
std::unique_ptr<DxgiPresenter> createDx10Presenter();
std::unique_ptr<DxgiPresenter> createDx11Presenter();
std::unique_ptr<DxgiPresenter> createDx12Presenter();
}
