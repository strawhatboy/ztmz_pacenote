#pragma once
#include "scene.hpp"
#include "assets.hpp"
#include <mutex>
#include <stop_token>
#include <atomic>
namespace ztmz {
struct FrameProgress {
    std::mutex mutex;
    uint64_t submittedFrames=0,window=0,lastFrameTick=0;
    std::string error;
    std::string queueSource;
    void reset(){std::lock_guard lock(mutex);submittedFrames=window=lastFrameTick=0;error.clear();queueSource.clear();}
};
struct SharedScene { std::mutex mutex; Scene scene; std::shared_ptr<const ImageAssets> images = std::make_shared<ImageAssets>(); std::string status = "Connecting"; FrameProgress progress; std::atomic<uintptr_t> targetWindow{0}; };
void receive(std::stop_token stop, SharedScene& shared, unsigned short port);
}
