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
    };
    std::map<std::string,Entry> entries_;
    bool dirty_=true;
    static std::string key(const Json& command);
public:
    DashboardFonts();
    bool collect(const Scene& scene);
    void rebuild();
    ImFont* lookup(const Json& command,std::set<std::string>& warnings) const;
};
}
