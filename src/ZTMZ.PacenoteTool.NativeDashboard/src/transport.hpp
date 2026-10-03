#pragma once
#include "scene.hpp"
#include "assets.hpp"
#include <mutex>
#include <stop_token>
namespace ztmz {
struct SharedScene { std::mutex mutex; Scene scene; std::shared_ptr<const ImageAssets> images = std::make_shared<ImageAssets>(); std::string status = "Connecting"; };
void receive(std::stop_token stop, SharedScene& shared, unsigned short port);
}
