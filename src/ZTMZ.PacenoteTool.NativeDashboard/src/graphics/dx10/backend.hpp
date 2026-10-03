#pragma once
#include "graphics/render_backend.hpp"
#include <d3d10.h>
#include <memory>
namespace ztmz {
struct Dx10Frame final : NativeFrame {
    explicit Dx10Frame(ID3D10RenderTargetView* value) : target(value) {}
    RenderApi api() const noexcept override {return RenderApi::Dx10;}
    ID3D10RenderTargetView* target; // borrowed for the duration of submit
};
std::unique_ptr<RenderBackend> createDx10Backend(ID3D10Device* device);
}
