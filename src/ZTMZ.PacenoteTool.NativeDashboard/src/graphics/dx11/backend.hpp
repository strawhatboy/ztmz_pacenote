#pragma once
#include "graphics/render_backend.hpp"
#include "graphics/dx11/glass_capture.hpp"
#include <d3d11.h>
#include <memory>
namespace ztmz {
struct Dx11Frame final : NativeFrame {
    explicit Dx11Frame(ID3D11RenderTargetView* value) : target(value) {}
    RenderApi api() const noexcept override {return RenderApi::Dx11;}
    ID3D11RenderTargetView* target; // borrowed for the duration of submit
    Dx11GlassCaptureStats glassCapture; // populated by submit, including zero for ordinary frames
};
std::unique_ptr<RenderBackend> createDx11Backend(ID3D11Device* device,ID3D11DeviceContext* context);
}
