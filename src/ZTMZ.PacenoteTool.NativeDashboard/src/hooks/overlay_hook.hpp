#pragma once
#include "graphics/render_api.hpp"
#include "transport.hpp"
#include <memory>
namespace ztmz {
// A hook family observes/validates native devices and owns their presenters,
// and adapts presentation/reset events. DLL lifecycle and networking are shared.
// Instances must remain alive while their process-lifetime hooks are installed.
class OverlayHook {
public:
    virtual ~OverlayHook() = default;
    virtual std::span<const RenderApi> apis() const noexcept = 0;
    virtual void install() = 0;
    virtual std::vector<std::string> warnings() const {return {};}
    virtual void enable() noexcept = 0;
    virtual void stop() noexcept = 0;
};
std::vector<std::unique_ptr<OverlayHook>> createOverlayHooks(SharedScene& shared,RenderApiRouter& router);
}
