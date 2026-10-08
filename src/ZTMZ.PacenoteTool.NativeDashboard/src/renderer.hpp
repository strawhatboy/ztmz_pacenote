#pragma once
#include "scene.hpp"
#include <imgui.h>
#include <functional>
#include <set>
namespace ztmz {
struct Texture { ImTextureID id = 0; unsigned width = 0, height = 0; };
using TextureLookup = std::function<Texture(const std::string&)>;
using FontLookup = std::function<ImFont*(const Json&,std::set<std::string>&)>;
std::set<std::string> draw(const Scene& scene, const TextureLookup& textures = {},const FontLookup& fonts = {});
}
