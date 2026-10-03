#include "lua_dashboard.hpp"
#include "lua_host.hpp"
extern "C" {
#include <lua.h>
#include <lauxlib.h>
#include <lualib.h>
}
#include <cmath>
#include <cstdlib>
#include <stdexcept>
#include <utility>
namespace ztmz {
namespace {
// JSON arrays in the existing CLR/browser contract use zero-based indexing.
void pushJson(lua_State* state, const Json& value, int depth = 0) {
    if (depth > 48) luaL_error(state, "JSON nesting exceeds 48 levels");
    if (value.is_null()) lua_pushnil(state);
    else if (value.is_boolean()) lua_pushboolean(state, value.get<bool>());
    else if (value.is_number_integer()) lua_pushinteger(state, value.get<lua_Integer>());
    else if (value.is_number()) lua_pushnumber(state, value.get<double>());
    else if (value.is_string()) {
        const auto& string = value.get_ref<const std::string&>();
        lua_pushlstring(state, string.data(), string.size());
    } else {
        lua_newtable(state);
        if (value.is_array()) {
            for (std::size_t i = 0; i < value.size(); ++i) {
                pushJson(state, value[i], depth + 1);
                lua_rawseti(state, -2, static_cast<lua_Integer>(i));
            }
            lua_pushinteger(state, static_cast<lua_Integer>(value.size())); lua_setfield(state, -2, "Count");
            lua_pushinteger(state, static_cast<lua_Integer>(value.size())); lua_setfield(state, -2, "length");
        } else {
            for (auto i = value.begin(); i != value.end(); ++i) {
                lua_pushlstring(state, i.key().data(), i.key().size());
                pushJson(state, i.value(), depth + 1); lua_rawset(state, -3);
            }
        }
    }
}
Json readJson(lua_State* state, int index, std::size_t& budget, std::size_t& bytes, int depth = 0) {
    if (depth > 32 || budget == 0 || bytes < 16) throw std::runtime_error("Lua drawing output exceeds structural limit");
    --budget; bytes -= 16;
    index = lua_absindex(state, index);
    switch (lua_type(state, index)) {
    case LUA_TNIL: return nullptr;
    case LUA_TBOOLEAN: return lua_toboolean(state, index) != 0;
    case LUA_TNUMBER: {
        auto number = lua_tonumber(state, index);
        if (!std::isfinite(number)) throw std::runtime_error("Non-finite drawing coordinate");
        if (lua_isinteger(state, index)) return lua_tointeger(state, index);
        return number;
    }
    case LUA_TSTRING: {
        std::size_t size{}; auto* text = lua_tolstring(state, index, &size);
        if (size > 1024 * 1024 || size > bytes) throw std::runtime_error("Drawing string/output exceeds byte limit");
        bytes -= size;
        return std::string(text, size);
    }
    case LUA_TTABLE: {
        const auto length = lua_rawlen(state, index);
        if (length) {
            Json result = Json::array();
            for (std::size_t i = 1; i <= length; ++i) {
                lua_rawgeti(state, index, static_cast<lua_Integer>(i));
                result.push_back(readJson(state, -1, budget, bytes, depth + 1)); lua_pop(state, 1);
            }
            return result;
        }
        Json result = Json::object();
        lua_pushnil(state);
        while (lua_next(state, index)) {
            if (lua_type(state, -2) != LUA_TSTRING) throw std::runtime_error("Invalid command field");
            result[lua_tostring(state, -2)] = readJson(state, -1, budget, bytes, depth + 1); lua_pop(state, 1);
        }
        return result;
    }
    default: throw std::runtime_error("Unsupported value in Lua drawing output");
    }
}
}
struct LuaDashboard::Impl {
    lua_State* state = nullptr;
    LuaLimits limits;
    std::size_t used = 0;
    int remaining = 0, bridge = LUA_NOREF, id = 0;
    bool exited = false, failed = false;
    explicit Impl(LuaLimits value) : limits(value) {
        if (limits.memoryBytes < 256 * 1024 || limits.instructionsPerCall < 1000)
            throw std::runtime_error("Invalid Lua limits");
        state = lua_newstate(allocate, this);
        if (!state) throw std::runtime_error("Unable to allocate Lua state");
        *static_cast<Impl**>(lua_getextraspace(state)) = this;
    }
    ~Impl() { if (state) lua_close(state); }
    static void* allocate(void* user, void* pointer, std::size_t oldSize, std::size_t newSize) {
        auto& self = *static_cast<Impl*>(user);
        if (!pointer) oldSize = 0; // Lua passes a type tag for new allocations.
        if (!newSize) { std::free(pointer); self.used -= oldSize; return nullptr; }
        if (newSize > oldSize && newSize - oldSize > self.limits.memoryBytes - self.used) return nullptr;
        auto* result = std::realloc(pointer, newSize);
        if (result) self.used = self.used - oldSize + newSize;
        return result;
    }
    static void hook(lua_State* state, lua_Debug*) {
        auto& self = **static_cast<Impl**>(lua_getextraspace(state));
        self.remaining -= 1000;
        if (self.remaining <= 0) luaL_error(state, "dashboard instruction budget exceeded");
    }
    void call(int arguments, int results, const std::string& scope) {
        remaining = limits.instructionsPerCall;
        lua_sethook(state, hook, LUA_MASKCOUNT, 1000);
        auto status = lua_pcall(state, arguments, results, 0);
        lua_sethook(state, nullptr, 0, 0);
        if (status != LUA_OK) {
            auto* message = lua_tostring(state, -1);
            std::string error = scope + ": " + (message ? message : "Lua error");
            lua_settop(state, 0); failed = true;
            throw std::runtime_error(error);
        }
    }
    void push(const Json& json) {
        lua_pushcfunction(state, [](lua_State* s) -> int {
            auto* json = static_cast<const Json*>(lua_touserdata(s, 1)); pushJson(s, *json); return 1;
        });
        lua_pushlightuserdata(state, const_cast<Json*>(&json));
        call(1, 1, "JSON to Lua");
    }
    void chunk(const std::string& source, const std::string& name) {
        if (luaL_loadbufferx(state, source.data(), source.size(), name.c_str(), "t") != LUA_OK) {
            auto* message = lua_tostring(state, -1);
            std::string error = name + ": " + (message ? message : "Lua compilation failed");
            lua_settop(state, 0); failed = true; throw std::runtime_error(error);
        }
        call(0, 0, name);
    }
    void method(const char* name) {
        lua_rawgeti(state, LUA_REGISTRYINDEX, bridge);
        lua_getfield(state, -1, name); lua_remove(state, -2);
    }
};
LuaDashboard::LuaDashboard(const Json& package, const Json& config, LuaLimits limits)
    : impl_(std::make_unique<Impl>(limits)) {
    auto& vm = *impl_; vm.id = package.at("id").get<int>();
    lua_pushcfunction(vm.state, [](lua_State* state) -> int {
        // Only the libraries used by dashboards. No OS, file, native-module, or debug access.
        luaL_requiref(state, LUA_GNAME, luaopen_base, 1); lua_pop(state, 1);
        luaL_requiref(state, LUA_TABLIBNAME, luaopen_table, 1); lua_pop(state, 1);
        luaL_requiref(state, LUA_STRLIBNAME, luaopen_string, 1); lua_pop(state, 1);
        luaL_requiref(state, LUA_MATHLIBNAME, luaopen_math, 1); lua_pop(state, 1);
        luaL_requiref(state, LUA_UTF8LIBNAME, luaopen_utf8, 1); lua_pop(state, 1);
        for (const char* name : {"dofile", "loadfile", "load", "collectgarbage", "pcall", "xpcall", "print", "warn"}) {
            lua_pushnil(state); lua_setglobal(state, name);
        }
        // Existing common helpers contain diagnostic prints.
        lua_pushcfunction(state, [](lua_State*) -> int { return 0; }); lua_setglobal(state, "print");
        return 0;
    });
    vm.call(0, 0, "open dashboard libraries");
    if (luaL_loadbufferx(vm.state, luaHostSource, sizeof(luaHostSource) - 1, "native-host.lua", "t") != LUA_OK)
        throw std::runtime_error("Cannot compile native Lua host");
    vm.push(package); vm.push(config); vm.call(2, 1, "create native host");
    vm.bridge = luaL_ref(vm.state, LUA_REGISTRYINDEX);
    for (const auto& source : package.value("commonScripts", Json::array()))
        vm.chunk(source.get<std::string>(), "common.lua");
    vm.chunk(package.at("luaScript").get<std::string>(), "dashboard-" + std::to_string(vm.id) + ".lua");
    vm.method("init"); vm.call(0, 0, "onInit");
}
LuaDashboard::~LuaDashboard() { try { shutdown(); } catch (...) {} }
void LuaDashboard::shutdown() {
    auto& vm = *impl_;
    if (!vm.exited && !vm.failed) { vm.exited = true; vm.method("shutdown"); vm.call(0, 0, "onExit"); }
}
std::vector<Layer> LuaDashboard::update(const Json& data, const Json& context, double seconds, const std::string& clock) {
    auto& vm = *impl_;
    if (vm.exited || vm.failed) throw std::runtime_error("Lua dashboard is stopped");
    try {
        vm.method("update"); vm.push(data); vm.push(context);
        lua_pushnumber(vm.state, seconds); lua_pushlstring(vm.state, clock.data(), clock.size());
        vm.call(4, 2, "onUpdate");
        std::vector<Layer> result;
        std::size_t budget = 500'000, bytes = 8 * 1024 * 1024;
        for (int layer = 0; layer < 2; ++layer) {
            const int index = lua_absindex(vm.state, -2 + layer);
            Json commands = Json::array();
            const auto count = lua_rawlen(vm.state, index);
            if (count > 20000) throw std::runtime_error("Too many drawing commands");
            for (std::size_t i = 1; i <= count; ++i) {
                lua_rawgeti(vm.state, index, static_cast<lua_Integer>(i));
                commands.push_back(readJson(vm.state, -1, budget, bytes)); lua_pop(vm.state, 1);
            }
            const std::string kind = layer == 0 ? "static" : "dynamic";
            result.push_back({"native-lua-" + std::to_string(vm.id) + "-" + kind, kind, std::move(commands)});
        }
        lua_settop(vm.state, 0); return result;
    } catch (...) { lua_settop(vm.state, 0); vm.failed = true; throw; }
}
}
