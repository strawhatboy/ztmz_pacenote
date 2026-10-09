#pragma once
#include <imgui.h>
#include <memory>
#include <utility>
namespace ztmz {
// CPU-only, including the bootstrap atlas. Legacy single-threaded devices are
// never touched on this worker. Each session owns its own mutable atlas/TexID.
class PreparedContext {
    ImGuiContext* context_=nullptr;
public:
    PreparedContext() {
        auto* previous=ImGui::GetCurrentContext();
        context_=ImGui::CreateContext();
        ImGui::SetCurrentContext(context_);
        try {
            auto& io=ImGui::GetIO();io.IniFilename=nullptr;io.LogFilename=nullptr;
            unsigned char* pixels=nullptr;int width=0,height=0;
            io.Fonts->GetTexDataAsRGBA32(&pixels,&width,&height);
        }catch(...) {ImGui::DestroyContext(context_);context_=nullptr;ImGui::SetCurrentContext(previous);throw;}
        ImGui::SetCurrentContext(previous);
    }
    ~PreparedContext() {
        auto* previous=ImGui::GetCurrentContext();
        if(context_)ImGui::DestroyContext(context_);
        ImGui::SetCurrentContext(previous);
    }
    ImGuiContext* release(){return std::exchange(context_,nullptr);}
};
}
