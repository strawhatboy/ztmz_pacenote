#pragma once
#include "scene.hpp"
#include <imgui.h>
#include <functional>
#include <set>
#include <array>
namespace ztmz {
struct Texture { ImTextureID id = 0; unsigned width = 0, height = 0; };
using TextureLookup = std::function<Texture(const std::string&)>;
using FontLookup = std::function<ImFont*(const Json&,std::set<std::string>&)>;
// Physical display coordinates, produced from bounded dashboard-coordinate values.
struct GlassPanel {
    ImVec2 min, max;
    float radius=20, refraction=8, blur=4;
    ImVec4 tint{0.125f,0.188f,0.25f,0.188f};
    float slant=0; // Top-to-bottom horizontal offset, inside the existing bounds.
    std::array<ImVec2,8> points{};
    unsigned pointCount=0;
    std::array<float,32> shaderConstants(float width,float height,float captureReady=0) const {
        std::array<float,32> v{min.x,min.y,max.x,max.y,radius,refraction,blur,captureReady,
            tint.x,tint.y,tint.z,tint.w,width,height,slant,float(pointCount)};
        for(unsigned i=0;i<8;++i){const auto& p=points[i<pointCount?i:0];v[16+i*2]=p.x;v[17+i*2]=p.y;}
        return v;
    }
};
using GlassDraw = std::function<void(ImDrawList&,const GlassPanel&)>;
std::set<std::string> draw(const Scene& scene, const TextureLookup& textures = {},const FontLookup& fonts = {},const GlassDraw& glass = {});
}
