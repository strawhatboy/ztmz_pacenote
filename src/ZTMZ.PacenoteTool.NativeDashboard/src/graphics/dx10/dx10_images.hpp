#pragma once
#include "assets.hpp"
#include "renderer.hpp"
#include <d3d10.h>
#include <wrl/client.h>
namespace ztmz {
class Dx10Images {
    struct Entry { std::shared_ptr<const ImageAsset> image; Microsoft::WRL::ComPtr<ID3D10ShaderResourceView> view; };
    std::map<std::string,Entry> entries_;
public:
    void sync(ID3D10Device* device,const ImageAssets& images);
    Texture lookup(const std::string& key) const;
    void clear() {entries_.clear();}
};
}
