#pragma once
#include <nlohmann/json.hpp>
#include <cstdint>
#include <string>
#include <vector>
namespace ztmz {
using Json = nlohmann::json;
struct Layer { std::string id, kind; Json commands; };
// Owned by one thread. Apply a complete message before publishing a copy.
struct Scene {
    int width = 1280, height = 720;
    std::int64_t revision = 0;
    bool layered = false;
    std::vector<Layer> layers;
    // False means the client must requestSceneSnapshot; state stays unchanged.
    bool apply(const Json& message);
};
}
