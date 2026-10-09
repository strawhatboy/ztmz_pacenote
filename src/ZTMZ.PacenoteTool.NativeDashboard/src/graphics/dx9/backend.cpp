#include "graphics/dx9/backend.hpp"
#include <imgui_impl_dx9.h>
#include <wrl/client.h>
#include <stdexcept>
#include <array>
#include <algorithm>
namespace ztmz {
namespace {
using Microsoft::WRL::ComPtr;
void check(HRESULT result){if(FAILED(result)) throw std::runtime_error("DX9 operation failed: "+std::to_string(result));}
class Dx9Backend final:public RenderBackend {
    ComPtr<IDirect3DDevice9> device_;
    struct Image {std::shared_ptr<const ImageAsset> source;ComPtr<IDirect3DTexture9> texture;};
    std::map<std::string,Image> images_;
    bool initialized_=false;
public:
    explicit Dx9Backend(IDirect3DDevice9* device):device_(device){if(!device) throw std::invalid_argument("DX9 device missing");}
    RenderApi api() const noexcept override{return RenderApi::Dx9;}
    void initialize() override {if(!ImGui_ImplDX9_Init(device_.Get())) throw std::runtime_error("ImGui DX9 initialization failed");initialized_=true;}
    void newFrame() override {ImGui_ImplDX9_NewFrame();}
    void syncImages(const ImageAssets& images) override {
        for(auto it=images_.begin();it!=images_.end();) if(!images.contains(it->first)) it=images_.erase(it);else ++it;
        for(const auto& [key,source]:images) {
            auto& image=images_[key];if(image.source==source && image.texture) continue;
            ComPtr<IDirect3DTexture9> texture;
            check(device_->CreateTexture(source->width,source->height,1,D3DUSAGE_DYNAMIC,D3DFMT_A8R8G8B8,D3DPOOL_DEFAULT,&texture,nullptr));
            D3DLOCKED_RECT locked{};check(texture->LockRect(0,&locked,nullptr,D3DLOCK_DISCARD));
            for(unsigned y=0;y<source->height;++y) {
                auto* dst=static_cast<unsigned char*>(locked.pBits)+static_cast<std::size_t>(y)*locked.Pitch;
                const auto* src=source->rgba.data()+static_cast<std::size_t>(y)*source->width*4;
                for(unsigned x=0;x<source->width;++x){dst[x*4]=src[x*4+2];dst[x*4+1]=src[x*4+1];dst[x*4+2]=src[x*4];dst[x*4+3]=src[x*4+3];}
            }
            check(texture->UnlockRect(0));image={source,std::move(texture)};
            break; // Legacy device calls stay on Present; never upload the entire package in one frame.
        }
    }
    Texture texture(const std::string& key) const override {
        auto i=images_.find(key);if(i==images_.end()) return {};
        return {static_cast<ImTextureID>(reinterpret_cast<std::uintptr_t>(i->second.texture.Get())),i->second.source->width,i->second.source->height};
    }
    void submit(ImDrawData& data,NativeFrame& native) override {
        auto* frame=dynamic_cast<Dx9Frame*>(&native);if(!frame || !frame->target) throw std::invalid_argument("DX9 surface missing");
        // Present is outside the game's scene. Never EndScene if our BeginScene failed.
        if(FAILED(device_->BeginScene())) return;
        struct Restore {
            IDirect3DDevice9* device;std::array<ComPtr<IDirect3DSurface9>,4> targets;ComPtr<IDirect3DSurface9> depth;D3DVIEWPORT9 viewport{};DWORD count=1;
            explicit Restore(IDirect3DDevice9* d):device(d){
                D3DCAPS9 caps{};if(SUCCEEDED(device->GetDeviceCaps(&caps))) count=std::clamp<DWORD>(caps.NumSimultaneousRTs,1,4);
                for(DWORD i=0;i<count;++i) device->GetRenderTarget(i,&targets[i]);
                device->GetDepthStencilSurface(&depth);device->GetViewport(&viewport);
            }
            ~Restore(){for(DWORD i=0;i<count;++i) device->SetRenderTarget(i,targets[i].Get());device->SetDepthStencilSurface(depth.Get());device->SetViewport(&viewport);device->EndScene();}
        } restore(device_.Get());
        for(DWORD i=1;i<restore.count;++i) check(device_->SetRenderTarget(i,nullptr));
        check(device_->SetRenderTarget(0,frame->target));check(device_->SetDepthStencilSurface(nullptr));
          ImGui_ImplDX9_RenderDrawData(&data);
          frame->submitted=true;
    }
    void invalidateDeviceObjects() override {images_.clear();if(initialized_) ImGui_ImplDX9_InvalidateDeviceObjects();}
    void shutdown() noexcept override {images_.clear();if(initialized_) ImGui_ImplDX9_Shutdown();initialized_=false;}
};
}
std::unique_ptr<RenderBackend> createDx9Backend(IDirect3DDevice9* device){return std::make_unique<Dx9Backend>(device);}
}
