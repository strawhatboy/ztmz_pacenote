#include "graphics/render_api.hpp"
#include <algorithm>
#include <array>
#include <cctype>
#include <stdexcept>
#include <string>
namespace ztmz {
std::string_view apiName(RenderApi api) noexcept {
    switch(api) {
    case RenderApi::Auto: return "auto";
    case RenderApi::Dx9: return "dx9";
    case RenderApi::Dx10: return "dx10";
    case RenderApi::Dx11: return "dx11";
    case RenderApi::Dx12: return "dx12";
    case RenderApi::Vulkan: return "vulkan";
    }
    return "unknown";
}
RenderApi parseRenderApi(std::string_view name) {
    std::string normalized(name);
    std::transform(normalized.begin(),normalized.end(),normalized.begin(),[](unsigned char c){return static_cast<char>(std::tolower(c));});
    for(auto api:{RenderApi::Auto,RenderApi::Dx9,RenderApi::Dx10,RenderApi::Dx11,RenderApi::Dx12,RenderApi::Vulkan})
        if(normalized==apiName(api)) return api;
    throw std::invalid_argument("Unknown render API '"+std::string(name)+"'; choose auto, dx9, dx10, dx11, dx12, or vulkan");
}
std::span<const BackendInfo> backendCatalog() noexcept {
#ifdef ZTMZ_HAS_DX9
    constexpr bool dx9Compiled=true;
#else
    constexpr bool dx9Compiled=false;
#endif
#ifdef ZTMZ_HAS_DX10
    constexpr bool dx10Compiled=true;
#else
    constexpr bool dx10Compiled=false;
#endif
#ifdef ZTMZ_HAS_DX11
    constexpr bool dx11Compiled=true;
#else
    constexpr bool dx11Compiled=false;
#endif
#ifdef ZTMZ_HAS_DX12
    constexpr bool dx12Compiled=true;
#else
    constexpr bool dx12Compiled=false;
#endif
#ifdef ZTMZ_HAS_VULKAN
    constexpr bool vulkanCompiled=true;
#else
    constexpr bool vulkanCompiled=false;
#endif
    static constexpr std::array catalog{
        BackendInfo{RenderApi::Dx9,"imgui_impl_dx9",true,dx9Compiled,"Present/EndScene, Reset, device-lost resources"},
        BackendInfo{RenderApi::Dx10,"imgui_impl_dx10",true,dx10Compiled,"DXGI Present/ResizeBuffers, D3D10 textures and state"},
        BackendInfo{RenderApi::Dx11,"imgui_impl_dx11",true,dx11Compiled,"DXGI Present/ResizeBuffers, immediate context and SRVs"},
        BackendInfo{RenderApi::Dx12,"imgui_impl_dx12",true,dx12Compiled,"DXGI swap chain, owning command queue, descriptors, barriers and fences"},
        BackendInfo{RenderApi::Vulkan,"imgui_impl_vulkan",true,vulkanCompiled,"Device/queue/swapchain dispatch, command buffers, layouts and semaphore chaining"}
    };
    return catalog;
}
std::vector<RenderApi> compiledOverlayApis() {
    std::vector<RenderApi> result;
    for(const auto& entry:backendCatalog()) if(entry.implemented && entry.compiled) result.push_back(entry.api);
    return result;
}
void RenderApiRouter::configure(RenderApi requested,std::span<const RenderApi> available) {
    std::vector<RenderApi> next;
    for(auto api:available) {
        if(api==RenderApi::Auto) throw std::invalid_argument("Auto is a selection policy, not a graphics backend");
        if((requested==RenderApi::Auto || requested==api) && std::find(next.begin(),next.end(),api)==next.end()) next.push_back(api);
    }
    if(next.empty()) throw std::runtime_error("Render API '"+std::string(apiName(requested))+"' is unavailable in this build; an implemented adapter and hook are required");
    std::lock_guard lock(mutex_);candidates_=std::move(next);active_=RenderApi::Auto;
}
bool RenderApiRouter::tryActivate(RenderApi observed) {
    std::lock_guard lock(mutex_);
    if(std::find(candidates_.begin(),candidates_.end(),observed)==candidates_.end()) return false;
    if(active_==RenderApi::Auto) active_=observed;
    return active_==observed;
}
void RenderApiRouter::release(RenderApi api) {
    std::lock_guard lock(mutex_);if(active_==api) active_=RenderApi::Auto;
}
RenderApi RenderApiRouter::active() const {std::lock_guard lock(mutex_);return active_;}
std::vector<RenderApi> RenderApiRouter::candidates() const {std::lock_guard lock(mutex_);return candidates_;}
}
