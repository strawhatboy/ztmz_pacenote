#pragma once
#include "font_catalog.hpp"
#include "renderer.hpp"
#include <map>
#include <set>
namespace ztmz {
class DashboardFonts {
    struct Entry {
        FontSelection selection;
        std::set<unsigned int> glyphs;
        ImVector<ImWchar> ranges;
        ImFont* font=nullptr;
        int pixels=20;
    };
    std::map<std::string,Entry> entries_;
    bool dirty_=true;
    float scale_=1;
    int pixels(const Json& command) const;
    std::string key(const Json& command) const;
public:
    DashboardFonts();
    bool collect(const Scene& scene,float scale=1);
    void rebuild();
    void rebuild(ImFontAtlas& atlas);
    std::unique_ptr<DashboardFonts> snapshot() const;
    void markClean(){dirty_=false;}
    void setScale(float scale){scale_=scale;}
    ImFont* lookup(const Json& command,std::set<std::string>& warnings) const;
};
}
