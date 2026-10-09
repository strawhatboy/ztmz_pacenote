#pragma once
#include "graphics/render_backend.hpp"
#include <d3d9.h>
namespace ztmz {
struct Dx9Frame final:NativeFrame {
    explicit Dx9Frame(IDirect3DSurface9* surface):target(surface){}
    RenderApi api() const noexcept override {return RenderApi::Dx9;}
    IDirect3DSurface9* target;
    bool submitted=false;
};
std::unique_ptr<RenderBackend> createDx9Backend(IDirect3DDevice9* device);
}
