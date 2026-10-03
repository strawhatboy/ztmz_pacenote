#pragma once
#include <cstdint>
#include <map>
#include <memory>
#include <string>
#include <vector>
namespace ztmz {
struct ImageAsset {
    unsigned width = 0, height = 0;
    std::vector<std::uint8_t> rgba;
};
using ImageAssets = std::map<std::string, std::shared_ptr<const ImageAsset>>;
}
