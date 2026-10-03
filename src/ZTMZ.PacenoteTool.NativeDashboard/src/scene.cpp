#include "scene.hpp"
#include <algorithm>
#include <stdexcept>
namespace ztmz {
bool Scene::apply(const Json& m) {
    const auto type = m.value("type", std::string{});
    if (type != "drawCommands" && type != "layeredDrawCommands") return true;
    Scene next = *this;
    next.width = m.value("canvasWidth", width);
    next.height = m.value("canvasHeight", height);
    if (next.width <= 0 || next.height <= 0 || next.width > 16384 || next.height > 16384)
        throw std::runtime_error("Invalid canvas dimensions");
    if (type == "drawCommands") {
        const auto commands = m.at("commands");
        if (!commands.is_array()) throw std::runtime_error("Commands must be an array");
        next.layers = {{"legacy", "dynamic", commands}};
        next.layered = false; next.revision = 0;
    } else {
        auto revision = m.at("sceneRevision").get<std::int64_t>();
        if (revision <= 0) return false;
        if (m.contains("layers")) {
            if (layered && revision < this->revision) return true;
            if (!m.at("layers").is_array()) throw std::runtime_error("Layers must be an array");
            next.layers.clear();
            for (const auto& l : m.at("layers")) {
                Layer layer{l.at("id").get<std::string>(), l.at("kind").get<std::string>(), l.at("commands")};
                if (!layer.commands.is_array() || (layer.kind != "static" && layer.kind != "dynamic"))
                    throw std::runtime_error("Invalid layer");
                if (std::any_of(next.layers.begin(), next.layers.end(), [&](const Layer& x){return x.id == layer.id;}))
                    throw std::runtime_error("Duplicate layer ID");
                next.layers.push_back(std::move(layer));
            }
            next.layered = true; next.revision = revision;
        } else {
            if (!layered || revision != this->revision) return false;
            if (!m.at("dynamicLayers").is_array()) throw std::runtime_error("Dynamic layers must be an array");
            for (const auto& l : m.at("dynamicLayers")) {
                auto found = std::find_if(next.layers.begin(), next.layers.end(), [&](const Layer& x){return x.id == l.at("id").get<std::string>();});
                if (found == next.layers.end() || found->kind != "dynamic") return false;
                if (!l.at("commands").is_array()) throw std::runtime_error("Invalid commands");
                found->commands = l.at("commands");
            }
        }
    }
    *this = std::move(next); return true;
}
}
