#include "hooks/overlay_hook.hpp"
#include "hooks/dxgi_presenter.hpp"
namespace ztmz {
std::unique_ptr<OverlayHook> createDx9Hook(SharedScene&,RenderApiRouter&);
std::unique_ptr<OverlayHook> createVulkanHook(SharedScene&,RenderApiRouter&);
std::unique_ptr<OverlayHook> createDxgiHook(SharedScene&,RenderApiRouter&,std::vector<std::unique_ptr<DxgiPresenter>>);
std::vector<std::unique_ptr<OverlayHook>> createOverlayHooks(SharedScene& shared,RenderApiRouter& router) {
    std::vector<std::unique_ptr<OverlayHook>> hooks;
    std::vector<std::unique_ptr<DxgiPresenter>> dxgi;
#ifdef ZTMZ_HAS_DX10
    dxgi.push_back(createDx10Presenter());
#endif
#ifdef ZTMZ_HAS_DX11
    dxgi.push_back(createDx11Presenter());
#endif
#ifdef ZTMZ_HAS_DX12
    dxgi.push_back(createDx12Presenter());
#endif
#ifdef ZTMZ_HAS_DX9
    hooks.push_back(createDx9Hook(shared,router));
#endif
#ifdef ZTMZ_HAS_VULKAN
    hooks.push_back(createVulkanHook(shared,router));
#endif
    if(!dxgi.empty()) hooks.push_back(createDxgiHook(shared,router,std::move(dxgi)));
    return hooks;
}
}
