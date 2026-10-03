#pragma once
#include "scene.hpp"
#include <memory>
namespace ztmz {
struct LuaLimits {
    std::size_t memoryBytes = 32 * 1024 * 1024;
    int instructionsPerCall = 2'000'000;
};
// One Lua VM per dashboard, confined to the client worker thread.
class LuaDashboard {
public:
    LuaDashboard(const Json& package, const Json& config, LuaLimits limits = {});
    ~LuaDashboard();
    LuaDashboard(const LuaDashboard&) = delete;
    LuaDashboard& operator=(const LuaDashboard&) = delete;
    std::vector<Layer> update(const Json& data, const Json& context,
                              double animationSeconds, const std::string& clockText);
    void shutdown();
private:
    struct Impl;
    std::unique_ptr<Impl> impl_;
};
}
