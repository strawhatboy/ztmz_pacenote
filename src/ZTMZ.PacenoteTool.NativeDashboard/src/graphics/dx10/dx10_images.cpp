#include "dx10_images.hpp"
namespace ztmz {
void Dx10Images::sync(ID3D10Device* device,const ImageAssets& images) {
    for(auto it=entries_.begin();it!=entries_.end();) {
        if(!images.contains(it->first)) it=entries_.erase(it);else ++it;
    }
    for(const auto& [key,image]:images) {
        auto& entry=entries_[key];if(entry.image==image && entry.view) continue;
        D3D10_TEXTURE2D_DESC desc{};desc.Width=image->width;desc.Height=image->height;
        desc.MipLevels=desc.ArraySize=1;desc.Format=DXGI_FORMAT_R8G8B8A8_UNORM;desc.SampleDesc.Count=1;
        desc.Usage=D3D10_USAGE_IMMUTABLE;desc.BindFlags=D3D10_BIND_SHADER_RESOURCE;
        D3D10_SUBRESOURCE_DATA data{image->rgba.data(),image->width*4,0};
        Microsoft::WRL::ComPtr<ID3D10Texture2D> texture;
        entry.view.Reset();entry.image=image;
        if(FAILED(device->CreateTexture2D(&desc,&data,&texture))) continue;
        device->CreateShaderResourceView(texture.Get(),nullptr,&entry.view);
    }
}
Texture Dx10Images::lookup(const std::string& key) const {
    auto it=entries_.find(key);if(it==entries_.end() || !it->second.view) return {};
    return {static_cast<ImTextureID>(reinterpret_cast<std::uintptr_t>(it->second.view.Get())),it->second.image->width,it->second.image->height};
}
}
