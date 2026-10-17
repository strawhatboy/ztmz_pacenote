#pragma once
#include "lua_dashboard.hpp"
#include <functional>
#include <map>
namespace ztmz {
// The loader returns /api/dashboard/{id}, augmented with imageMetadata when available.
using PackageLoader = std::function<Json(int)>;
struct ClientResult {
    bool changed = false;
    std::string controlMessage;
};
class DashboardClient {
public:
    explicit DashboardClient(PackageLoader loader);
    ClientResult accept(const Json& message, double seconds, const std::string& clockText);
    const Scene& scene() const { return composed_; }
    const std::string& status() const { return status_; }
    void reset();
private:
    struct Entry {
        int order = 0;
        std::string signature;
        std::unique_ptr<LuaDashboard> lua;
        std::vector<Layer> layers;
        Json package, settings, config;
        std::string failure;
        double retryAt = 0;
        unsigned retryDelay = 1;
    };
    PackageLoader loader_;
    std::map<int, Entry> runtimes_;
    Scene commands_, composed_;
    Json context_ = Json::object();
    std::int64_t revision_ = 0;
    bool hybrid_ = false, disabled_ = false;
    std::string status_ = "Waiting for Lua scene";
    void compose();
};
}
