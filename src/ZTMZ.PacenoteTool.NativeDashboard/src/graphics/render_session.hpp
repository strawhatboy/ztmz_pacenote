#pragma once
#include "graphics/render_backend.hpp"
#include <memory>
#include "font_atlas.hpp"
namespace ztmz {
// One session per native graphics device. Lua/telemetry remain outside its lifetime.
// Hosts serialize access; no backend/ImGui calls belong on the network thread.
class RenderSession {
public:
    using UserInterface = std::function<void(const std::set<std::string>& warnings)>;
    explicit RenderSession(std::unique_ptr<RenderBackend> backend);
    ~RenderSession();
    RenderSession(const RenderSession&) = delete;
    RenderSession& operator=(const RenderSession&) = delete;
    RenderApi api() const noexcept { return backend_->api(); }
    std::set<std::string> render(const FrameInfo& info, NativeFrame& frame,
        const Scene& scene, const ImageAssets& images, const UserInterface& ui = {});
    void invalidateDeviceObjects();
    // Platform input/timing adapters may run here without exposing the context.
    void withContext(const std::function<void()>& action);
private:
    std::unique_ptr<RenderBackend> backend_;
    ImGuiContext* context_ = nullptr;
    DashboardFonts fonts_;
};
}
