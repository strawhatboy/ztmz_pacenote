#pragma once
#include <mutex>
#include <span>
#include <string_view>
#include <vector>
namespace ztmz {
enum class RenderApi { Auto, Dx9, Dx10, Dx11, Dx12, Vulkan };
std::string_view apiName(RenderApi api) noexcept;
RenderApi parseRenderApi(std::string_view name);
struct BackendInfo {
    RenderApi api;
    std::string_view imguiBackend;
    bool implemented;
    bool compiled;
    std::string_view integration;
};
std::span<const BackendInfo> backendCatalog() noexcept;
std::vector<RenderApi> compiledOverlayApis();
// Selection happens on a verified device/presentation event. A loaded DLL name
// is not evidence of the API actually presenting the game's frames.
class RenderApiRouter {
public:
    void configure(RenderApi requested, std::span<const RenderApi> available);
    bool tryActivate(RenderApi observed);
    void release(RenderApi api);
    RenderApi active() const;
    std::vector<RenderApi> candidates() const;
private:
    mutable std::mutex mutex_;
    std::vector<RenderApi> candidates_;
    RenderApi active_ = RenderApi::Auto;
};
}
