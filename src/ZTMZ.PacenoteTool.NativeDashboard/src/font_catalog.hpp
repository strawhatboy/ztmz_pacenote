#pragma once
#include <memory>
#include <string>
#include <vector>
#include <cstdint>
namespace ztmz {
struct FontFace {
    std::vector<unsigned char> bytes;
    int index=0;
    std::string identity;
    bool contains(unsigned int codepoint) const;
    float advance(unsigned int codepoint,float size) const;
};
struct FontSelection {
    std::vector<std::shared_ptr<const FontFace>> faces;
    bool substituted=false;
};
FontSelection selectFont(const std::string& family,int weight,bool italic);
std::vector<unsigned int> textCodepoints(const std::string& text);
struct TextSize {float width=0,height=0;};
TextSize measureText(const FontSelection& fonts,float size,const std::string& text);
}
