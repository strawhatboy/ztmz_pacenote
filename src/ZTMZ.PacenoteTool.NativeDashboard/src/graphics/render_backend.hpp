#pragma once
#include "graphics/render_api.hpp"
#include "assets.hpp"
#include "renderer.hpp"
namespace ztmz {
// API-specific subclasses hold borrowed native handles needed for this submission.
// DX12/Vulkan frames can carry command buffers, barriers and submission context;
// the common renderer never interprets those handles or assumes immediate submission.
// submit may update this context (e.g. Vulkan presentation wait semaphores). The
// hook consumes those outputs before forwarding the original presentation call.
struct NativeFrame {
    virtual ~NativeFrame() = default;
    virtual RenderApi api() const noexcept = 0;
};
struct FrameInfo {
    unsigned width = 0, height = 0;
    float deltaSeconds = 1.f / 60;
};
class RenderBackend {
public:
    virtual ~RenderBackend() = default;
    virtual RenderApi api() const noexcept = 0;
    // All calls run with the owning RenderSession's ImGui context current.
    virtual void initialize() = 0;
    virtual bool readyForFrame() const {return true;}
    virtual void newFrame() = 0;
    virtual void syncImages(const ImageAssets& images) = 0;
    virtual Texture texture(const std::string& key) const = 0;
    virtual void submit(ImDrawData& data, NativeFrame& frame) = 0;
    // The adapter owns GPU synchronization and deferred resource retirement.
    // Do not destroy resources that an explicit API still has in flight.
    virtual void invalidateDeviceObjects() = 0;
    virtual void shutdown() noexcept = 0;
};
}
