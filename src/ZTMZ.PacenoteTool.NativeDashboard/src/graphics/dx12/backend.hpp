#pragma once
#include "graphics/render_backend.hpp"
#include <d3d12.h>
#include "graphics/glass_capture.hpp"
namespace ztmz {
struct Dx12Frame final:NativeFrame {
    explicit Dx12Frame(ID3D12Resource* resource):backBuffer(resource){}
    RenderApi api() const noexcept override{return RenderApi::Dx12;}
    ID3D12Resource* backBuffer;
    GlassCaptureStats glassCapture;
};
std::unique_ptr<RenderBackend> createDx12Backend(ID3D12Device*,ID3D12CommandQueue*,DXGI_FORMAT,unsigned bufferCount);
}
