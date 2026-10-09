#pragma once
#include "graphics/render_backend.hpp"
#include <memory>
#include "font_atlas.hpp"
#include "font_worker.hpp"
#include "graphics/prepared_context.hpp"
namespace ztmz {
// One session per native graphics device. Lua/telemetry remain outside its lifetime.
// Hosts serialize access; no backend/ImGui calls belong on the network thread.
class RenderSession {
public:
    using UserInterface = std::function<void(const std::set<std::string>& warnings)>;
    explicit RenderSession(std::unique_ptr<RenderBackend> backend,std::unique_ptr<PreparedContext> prepared={});
    ~RenderSession();
    RenderSession(const RenderSession&) = delete;
    RenderSession& operator=(const RenderSession&) = delete;
    RenderApi api() const noexcept { return backend_->api(); }
    bool lastFrameSubmitted() const noexcept {return submitted_;}
    std::set<std::string> render(const FrameInfo& info, NativeFrame& frame,
        const Scene& scene, const ImageAssets& images, const UserInterface& ui = {});
    void invalidateDeviceObjects();
    // Call only with exclusive session ownership; DX12 may prepare on a worker.
    void warmup(const ImageAssets& images);
    // Platform input/timing adapters may run here without exposing the context.
    void withContext(const std::function<void()>& action);
private:
    std::unique_ptr<RenderBackend> backend_;
    ImGuiContext* context_ = nullptr;
    FontWorker fontWorker_;
    std::shared_ptr<PreparedFonts> fonts_;
    bool submitted_=false;
};
}
