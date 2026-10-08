#include "graphics/render_session.hpp"
#include <algorithm>
#include <cmath>
#include <stdexcept>
namespace ztmz {
namespace {
class ContextScope {
    ImGuiContext* previous_ = ImGui::GetCurrentContext();
public:
    explicit ContextScope(ImGuiContext* current) {ImGui::SetCurrentContext(current);}
    ~ContextScope(){ImGui::SetCurrentContext(previous_);}
};
}
RenderSession::RenderSession(std::unique_ptr<RenderBackend> backend) : backend_(std::move(backend)) {
    if(!backend_ || backend_->api()==RenderApi::Auto) throw std::invalid_argument("A concrete render backend is required");
    auto* previous=ImGui::GetCurrentContext();
    context_=ImGui::CreateContext();ImGui::SetCurrentContext(previous);
    try {
        ContextScope scope(context_);
        ImGui::GetIO().IniFilename=nullptr;ImGui::GetIO().LogFilename=nullptr;
        fonts_.rebuild();
        backend_->initialize();
    } catch(...) {
        {ContextScope scope(context_);backend_->shutdown();}
        ImGui::DestroyContext(context_);context_=nullptr;
        ImGui::SetCurrentContext(previous);throw;
    }
}
RenderSession::~RenderSession() {
    {ContextScope scope(context_);backend_->shutdown();}
    ImGui::DestroyContext(context_);
}
std::set<std::string> RenderSession::render(const FrameInfo& info,NativeFrame& frame,
    const Scene& scene,const ImageAssets& images,const UserInterface& ui) {
    if(frame.api()!=api()) throw std::invalid_argument("Native frame API does not match the active backend");
    if(!info.width || !info.height) return {}; // minimized/zero-size surface
    if(!std::isfinite(info.deltaSeconds)) throw std::invalid_argument("Invalid frame duration");
    ContextScope scope(context_);
    if(fonts_.collect(scene)) {
        backend_->invalidateDeviceObjects();
        fonts_.rebuild();
    }
    auto& io=ImGui::GetIO();io.DisplaySize={static_cast<float>(info.width),static_cast<float>(info.height)};
    io.DeltaTime=std::max(0.001f,info.deltaSeconds);
    backend_->syncImages(images);backend_->newFrame();
    ImGui::NewFrame();
    std::set<std::string> warnings;
    try {
        warnings=draw(scene,[&](const std::string& key){return backend_->texture(key);},
            [&](const Json& command,std::set<std::string>& warnings){return fonts_.lookup(command,warnings);});
        if(ui) ui(warnings);
        ImGui::Render();
    } catch(...) {ImGui::EndFrame();throw;}
    // For explicit APIs, submit may record/schedule work rather than complete it.
    // Such backends copy/stage draw data before this frame's CPU buffers are reused.
    backend_->submit(*ImGui::GetDrawData(),frame);
    return warnings;
}
void RenderSession::withContext(const std::function<void()>& action) {ContextScope scope(context_);action();}
void RenderSession::invalidateDeviceObjects() {ContextScope scope(context_);backend_->invalidateDeviceObjects();}
}
