#pragma once
#include "font_atlas.hpp"
#include "assets.hpp"
#include <mutex>
namespace ztmz {
struct PreparedFonts {
    // Destroy the atlas before its input buffers/configurations.
    std::unique_ptr<DashboardFonts> fonts;
    ImFontAtlas atlas;
    std::shared_ptr<ImageAsset> image;
};
class FontWorker {
    struct State;
    std::shared_ptr<State> state_;
    std::string signature_;
public:
    FontWorker();
    void request(const Scene& scene,float scale);
    std::shared_ptr<PreparedFonts> takeReady();
    void retire(std::shared_ptr<PreparedFonts> fonts);
};
}
