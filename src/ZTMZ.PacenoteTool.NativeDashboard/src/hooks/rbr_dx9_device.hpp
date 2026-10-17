#pragma once
#include <array>
#include <cstdint>
#include <functional>
#include <optional>

namespace ztmz {
struct Dx9DeviceTargets {uintptr_t reset=0,present=0;};
using ReadRbrMemory=std::function<bool(uintptr_t,void*,size_t)>;

// RBR's SDK device chain. Read pointer values only: no COM calls or ownership
// changes on a device which the game can reset while we install hooks.
inline std::optional<Dx9DeviceTargets> readRbrDx9Targets(const ReadRbrMemory& read) {
    auto pointer=[&](uint32_t address,uint32_t& value) {
        return address>=0x10000 && address<=UINT32_MAX-4 && read(address,&value,4) && value>=0x10000;
    };
    uint32_t engine=0,graphics=0,device=0,vtable=0;
    if(!pointer(0x7ea990,engine) || engine>UINT32_MAX-0x28 || !pointer(engine+0x28,graphics)
        || graphics>UINT32_MAX-0xf4 || !pointer(graphics+0xf4,device) || !pointer(device,vtable)
        || vtable>UINT32_MAX-18*4) return {};
    std::array<uint32_t,18> methods{};
    if(!read(vtable,methods.data(),sizeof(methods)) || methods[16]<0x10000 || methods[17]<0x10000) return {};
    uint32_t check=0;
    if(!pointer(0x7ea990,check) || check!=engine || !pointer(engine+0x28,check) || check!=graphics
        || !pointer(graphics+0xf4,check) || check!=device || !pointer(device,check) || check!=vtable) return {};
    return Dx9DeviceTargets{methods[16],methods[17]};
}

// nullopt means an ordinary host may use the generic probe. RBR failures throw:
// falling back to CreateDevice can crash its already-installed GaugerPlugin.
std::optional<Dx9DeviceTargets> existingRbrDx9Targets();
}
