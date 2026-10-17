#include "dashboard_client.hpp"
#include <algorithm>
#include <stdexcept>
namespace ztmz {
DashboardClient::DashboardClient(PackageLoader loader) : loader_(std::move(loader)) {}
void DashboardClient::reset() {
    runtimes_.clear(); commands_ = {}; composed_ = {}; context_ = Json::object();
    revision_ = 0; hybrid_ = false; disabled_ = false; status_ = "Waiting for Lua scene";
}
void DashboardClient::compose() {
    composed_ = commands_;
    composed_.width = 1920; composed_.height = 1080;
    // Both server capture and browser Lua use logical 1920x1080 coordinates.
    // canvasWidth/Height describe raster resolution, not these drawing units.
    if (runtimes_.empty()) return;
    composed_.revision = revision_;
    struct OrderedLayer { long long order; Layer layer; };
    std::vector<OrderedLayer> layers;
    if (hybrid_) {
        for (const auto& layer : commands_.layers) {
            long long order = 0;
            if (layer.id.starts_with("scene:")) order = -1000;
            else if (layer.id.starts_with("dashboard-")) {
                const auto colon = layer.id.find(':');
                const auto segment = layer.id.rfind(':');
                if (colon != std::string::npos && segment != colon) {
                    try { order = std::stoll(layer.id.substr(10, colon - 10)) * 1000
                        + std::stoll(layer.id.substr(segment + 1)); }
                    catch (const std::exception&) { order = 0; }
                }
            }
            layers.push_back({order, layer});
        }
    }
    for (const auto& [id, entry] : runtimes_) {
        (void)id;
        for (std::size_t i = 0; i < entry.layers.size(); ++i)
            layers.push_back({static_cast<long long>(entry.order) * 1000 + static_cast<long long>(i), entry.layers[i]});
    }
    std::stable_sort(layers.begin(), layers.end(), [](const auto& a, const auto& b){return a.order < b.order;});
    composed_.layers.clear();
    for (auto& item : layers) composed_.layers.push_back(std::move(item.layer));
}
ClientResult DashboardClient::accept(const Json& message, double seconds, const std::string& clock) {
    const auto type = message.value("type", std::string{});
    if (type == "drawCommands" || type == "layeredDrawCommands") {
        if (!commands_.apply(message)) return {false, R"({"type":"requestSceneSnapshot"})"};
        hybrid_ = message.contains("browserLuaDashboardIds") && !message.at("browserLuaDashboardIds").empty();
        if (!hybrid_) { runtimes_.clear(); revision_ = 0; context_ = Json::object(); }
        compose();
        if (!hybrid_ && !disabled_) status_ = "Server command mode (server selected compatibility output)";
        return {true, {}};
    }
    if (type != "browserLuaTelemetry" || disabled_) return {};
    try {
        const auto revision = message.at("sceneRevision").get<std::int64_t>();
        if (revision <= 0) throw std::runtime_error("Invalid Lua scene revision");
        if (message.contains("dashboards")) {
            if (revision < revision_) return {};
            const auto& descriptors = message.at("dashboards");
            if (!descriptors.is_array() || descriptors.size() > 64) throw std::runtime_error("Invalid Lua scene");
            const auto config = message.value("config", Json::object());
            if (!config.is_object()) throw std::runtime_error("Invalid Lua configuration");
            std::map<int, Entry> next;
            for (const auto& descriptor : descriptors) {
                const int id = descriptor.at("id").get<int>();
                if (id < 0 || next.contains(id)) throw std::runtime_error("Invalid/duplicate dashboard ID");
                auto identity = descriptor; identity.erase("order");
                const auto signature = Json{{"descriptor", identity}, {"config", config}}.dump();
                auto previous = runtimes_.find(id);
                if (previous != runtimes_.end() && previous->second.signature == signature) {
                    previous->second.order = descriptor.value("order", id);
                    next.emplace(id, std::move(previous->second));
                } else {
                    Entry entry;entry.order=descriptor.value("order",id);entry.signature=signature;
                    entry.config=config;entry.settings=descriptor.value("settings",Json{});
                    next.emplace(id,std::move(entry));
                }
            }
            runtimes_ = std::move(next); revision_ = revision;
            context_ = Json::object();
        } else if (revision != revision_) {
            return {false, R"({"type":"requestBrowserLuaScene"})"};
        }
        hybrid_ = message.value("hybrid", false);
        const auto context = message.value("context", Json::object());
        if (!context.is_object()) throw std::runtime_error("Invalid telemetry context");
        context_.update(context);
        const auto data = message.value("data", Json::object());
        if (!data.is_object()) throw std::runtime_error("Invalid telemetry data");
        for (auto& [id, entry] : runtimes_) {
            if (!entry.failure.empty() && seconds < entry.retryAt) continue;
            try {
                if (!entry.lua) {
                    if (entry.package.is_null()) {
                        auto package=loader_(id);
                        if (package.at("id").get<int>()!=id) throw std::runtime_error("Package ID mismatch");
                        if (!entry.settings.is_null()) package["settings"]=entry.settings;
                        entry.package=std::move(package);
                    }
                    entry.lua=std::make_unique<LuaDashboard>(entry.package,entry.config);
                }
                entry.layers=entry.lua->update(data,context_,seconds,clock);
                entry.failure.clear();entry.retryDelay=1;
            } catch (const std::exception& e) {
                // A VM is permanently stopped after failed output conversion or
                // Lua execution. Recreate ONLY that VM, using cached source and
                // fresh telemetry, rather than disabling the whole client.
                entry.layers.clear();entry.lua.reset();entry.failure=e.what();
                entry.retryAt=seconds+entry.retryDelay;
                entry.retryDelay=std::min(30u,entry.retryDelay*2);
            }
        }
        if (!hybrid_) commands_ = {};
        compose();
        const auto healthy=std::count_if(runtimes_.begin(),runtimes_.end(),[](const auto& item){return item.second.failure.empty();});
        status_="Local Lua: "+std::to_string(healthy)+" dashboard(s)";
        for (const auto& [id,entry]:runtimes_) if(!entry.failure.empty())
            status_+="; Dashboard "+std::to_string(id)+" suspended (retrying): "+entry.failure;
        return {true, {}};
    } catch (const std::exception& e) {
        // Invalid scene/protocol state still requires connection-wide fallback.
        // Individual package/VM failures are contained above.
        runtimes_.clear(); revision_ = 0; context_ = Json::object();
        commands_ = {}; composed_ = {}; disabled_ = true;
        status_ = std::string("Lua failure: ") + e.what();
        return {true, Json{{"type", "browserLuaFallback"}, {"reason", status_}}.dump()};
    }
}
}
