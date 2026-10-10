#pragma once
#include <unknwn.h>
#include <wrl/client.h>
#include <utility>

namespace ztmz {
// Streamline's COM escape hatch (also supported by legacy 1.x, which has no
// slGetNativeInterface export). QI returns an AddRef'd IUnknown. Do not inspect
// proxy memory or initialize/load Streamline on behalf of the game.
// https://github.com/NVIDIA-RTX/Streamline/blob/v1.1.1/source/core/sl.api/internal.h
inline constexpr GUID streamlineNativeInterface={0xadec44e2,0x61f0,0x45c3,{0xad,0x9f,0x1b,0x37,0x37,0x92,0x84,0xff}};

template<class T>
Microsoft::WRL::ComPtr<T> nativeDxgiInterface(T* object) {
    Microsoft::WRL::ComPtr<T> current=object;
    for(unsigned depth=0;current&&depth<4;++depth) {
        Microsoft::WRL::ComPtr<IUnknown> native;
        if(FAILED(current->QueryInterface(streamlineNativeInterface,reinterpret_cast<void**>(native.GetAddressOf())))) return current;
        Microsoft::WRL::ComPtr<T> next;
        if(!native||FAILED(native.As(&next))) return {};
        if(next.Get()==current.Get()) return current;
        current=std::move(next);
    }
    return {}; // Reject a broken/cyclic proxy rather than scan an unknown layout.
}
}
