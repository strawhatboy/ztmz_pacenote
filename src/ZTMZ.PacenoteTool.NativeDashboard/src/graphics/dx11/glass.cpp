#include "graphics/dx11/glass.hpp"
#include "glass_shaders.hpp"
#include <algorithm>
#include <cmath>
#include <cstring>
namespace ztmz {
bool Dx11Glass::initialize() {
    if(attempted_) return initialized_;
    attempted_=true;
    if(FAILED(device_->CreateVertexShader(glass_vs,sizeof(glass_vs),nullptr,&vertex_))||
       FAILED(device_->CreatePixelShader(glass_ps,sizeof(glass_ps),nullptr,&pixel_))) return false;
    D3D11_BUFFER_DESC buffer{};buffer.ByteWidth=64;buffer.Usage=D3D11_USAGE_DEFAULT;buffer.BindFlags=D3D11_BIND_CONSTANT_BUFFER;
    if(FAILED(device_->CreateBuffer(&buffer,nullptr,&constants_))) return false;
    D3D11_SAMPLER_DESC sampler{};sampler.Filter=D3D11_FILTER_MIN_MAG_MIP_LINEAR;
    sampler.AddressU=sampler.AddressV=sampler.AddressW=D3D11_TEXTURE_ADDRESS_CLAMP;sampler.MaxLOD=D3D11_FLOAT32_MAX;
    if(FAILED(device_->CreateSamplerState(&sampler,&sampler_))) return false;
    initialized_=true;return true;
}
GlassDraw Dx11Glass::prepare(ID3D11RenderTargetView* target) {
    if(!target||!initialize()) return {};
    ComPtr<ID3D11Resource> resource;target->GetResource(&resource);
    ComPtr<ID3D11Texture2D> source;if(FAILED(resource.As(&source))) return {};
    D3D11_TEXTURE2D_DESC desc{};source->GetDesc(&desc);
    D3D11_RENDER_TARGET_VIEW_DESC targetDesc{};target->GetDesc(&targetDesc);
    // Unusual subresources/arrays, stereo and mismatched surfaces fall back. The
    // common presenter uses mip zero of a single ordinary swap-chain surface.
    if(desc.ArraySize!=1||desc.MipLevels!=1||
       (targetDesc.ViewDimension!=D3D11_RTV_DIMENSION_TEXTURE2D&&targetDesc.ViewDimension!=D3D11_RTV_DIMENSION_TEXTURE2DMS)||
       (targetDesc.ViewDimension==D3D11_RTV_DIMENSION_TEXTURE2D&&targetDesc.Texture2D.MipSlice!=0)) return {};
    auto display=ImGui::GetIO().DisplaySize;
    if(desc.Width!=static_cast<unsigned>(display.x)||desc.Height!=static_cast<unsigned>(display.y)) return {};
    const auto format=targetDesc.Format;
    // Typed snapshots also support typeless source resources through their RTV.
    if(format!=DXGI_FORMAT_R8G8B8A8_UNORM&&format!=DXGI_FORMAT_R8G8B8A8_UNORM_SRGB&&
       format!=DXGI_FORMAT_B8G8R8A8_UNORM&&format!=DXGI_FORMAT_B8G8R8A8_UNORM_SRGB&&
       format!=DXGI_FORMAT_R10G10B10A2_UNORM&&format!=DXGI_FORMAT_R16G16B16A16_FLOAT) return {};
    UINT support=0;if(FAILED(device_->CheckFormatSupport(format,&support))||!(support&D3D11_FORMAT_SUPPORT_SHADER_SAMPLE)||
        (desc.SampleDesc.Count>1&&!(support&D3D11_FORMAT_SUPPORT_MULTISAMPLE_RESOLVE))) return {};
    if(!snapshot_||width_!=desc.Width||height_!=desc.Height||format_!=format) {
        view_.Reset();snapshot_.Reset();width_=height_=0;format_=DXGI_FORMAT_UNKNOWN;
        D3D11_TEXTURE2D_DESC copy{};copy.Width=desc.Width;copy.Height=desc.Height;copy.MipLevels=copy.ArraySize=1;
        copy.Format=format;copy.SampleDesc.Count=1;copy.Usage=D3D11_USAGE_DEFAULT;copy.BindFlags=D3D11_BIND_SHADER_RESOURCE;
        if(FAILED(device_->CreateTexture2D(&copy,nullptr,&snapshot_))||FAILED(device_->CreateShaderResourceView(snapshot_.Get(),nullptr,&view_))) return {};
        width_=desc.Width;height_=desc.Height;format_=format;
    }
    pendingSource_=source;pendingMsaa_=desc.SampleDesc.Count>1;hasRegion_=false;
    return [this](ImDrawList& list,const GlassPanel& panel){
        const auto clipMin=list.GetClipRectMin(),clipMax=list.GetClipRectMax();
        const float left=std::max({0.f,panel.min.x,clipMin.x}),top=std::max({0.f,panel.min.y,clipMin.y});
        const float right=std::min({static_cast<float>(width_),panel.max.x,clipMax.x});
        const float bottom=std::min({static_cast<float>(height_),panel.max.y,clipMax.y});
        if(right<=left||bottom<=top) return;
        // Maximum displacement + blur tap + bilinear/AA footprint. Retaining
        // full-size texture coordinates makes clamping at viewport edges exact.
        const float padding=panel.refraction+panel.blur+2;
        D3D11_BOX box{static_cast<UINT>(std::max(0.f,std::floor(left-padding))),
            static_cast<UINT>(std::max(0.f,std::floor(top-padding))),0,
            static_cast<UINT>(std::min(static_cast<float>(width_),std::ceil(right+padding))),
            static_cast<UINT>(std::min(static_cast<float>(height_),std::ceil(bottom+padding))),1};
        if(!hasRegion_){captureBox_=box;hasRegion_=true;}
        else {
            captureBox_.left=std::min(captureBox_.left,box.left);captureBox_.top=std::min(captureBox_.top,box.top);
            captureBox_.right=std::max(captureBox_.right,box.right);captureBox_.bottom=std::max(captureBox_.bottom,box.bottom);
        }
        struct Payload {Dx11Glass* owner;GlassPanel panel;};
        Payload payload{this,panel};
        list.AddCallback([](const ImDrawList*,const ImDrawCmd* cmd){
            const auto& payload=*static_cast<const Payload*>(cmd->UserCallbackData);
            payload.owner->drawPanel(payload.panel,*cmd);
        },&payload,sizeof(payload));
        list.AddCallback(ImDrawCallback_ResetRenderState,nullptr);
    };
}
void Dx11Glass::beginFrame() {pendingSource_.Reset();hasRegion_=false;pendingMsaa_=false;}
Dx11GlassCaptureStats Dx11Glass::capture() {
    Dx11GlassCaptureStats stats;
    if(!pendingSource_||!hasRegion_)return stats;
    // Called after command construction but BEFORE ImGui submits any HUD draws.
    if(pendingMsaa_) {
        context_->ResolveSubresource(snapshot_.Get(),0,pendingSource_.Get(),0,format_);
        stats={1,width_,height_,true};
    } else {
        context_->CopySubresourceRegion(snapshot_.Get(),0,captureBox_.left,captureBox_.top,0,pendingSource_.Get(),0,&captureBox_);
        stats={1,captureBox_.right-captureBox_.left,captureBox_.bottom-captureBox_.top,false};
    }
    pendingSource_.Reset();hasRegion_=false;return stats;
}
void Dx11Glass::drawPanel(const GlassPanel& panel,const ImDrawCmd& cmd) {
    // ImGui does not save PS constant buffers. Preserve this slot ourselves;
    // its reset callback restores every other binding changed below.
    ComPtr<ID3D11Buffer> previous;context_->PSGetConstantBuffers(0,1,&previous);
    const float values[16]={panel.min.x,panel.min.y,panel.max.x,panel.max.y,
        panel.radius,panel.refraction,panel.blur,0,panel.tint.x,panel.tint.y,panel.tint.z,panel.tint.w,
        static_cast<float>(width_),static_cast<float>(height_),panel.slant,0};
    context_->UpdateSubresource(constants_.Get(),0,nullptr,values,0,0);
    context_->VSSetShader(vertex_.Get(),nullptr,0);context_->PSSetShader(pixel_.Get(),nullptr,0);
    auto* buffer=constants_.Get();context_->PSSetConstantBuffers(0,1,&buffer);
    auto* texture=view_.Get();context_->PSSetShaderResources(0,1,&texture);
    auto* sampler=sampler_.Get();context_->PSSetSamplers(0,1,&sampler);
    context_->IASetInputLayout(nullptr);context_->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
    // ImGui doesn't apply command scissor rectangles before callbacks. Intersect
    // the caller's clip with the panel to avoid a full-screen pixel shader pass.
    D3D11_RECT clip{
        static_cast<LONG>(std::max(cmd.ClipRect.x,panel.min.x)-1),static_cast<LONG>(std::max(cmd.ClipRect.y,panel.min.y)-1),
        static_cast<LONG>(std::min(cmd.ClipRect.z,panel.max.x)+1),static_cast<LONG>(std::min(cmd.ClipRect.w,panel.max.y)+1)};
    clip.left=std::max(clip.left,static_cast<LONG>(cmd.ClipRect.x));clip.top=std::max(clip.top,static_cast<LONG>(cmd.ClipRect.y));
    clip.right=std::min(clip.right,static_cast<LONG>(cmd.ClipRect.z));clip.bottom=std::min(clip.bottom,static_cast<LONG>(cmd.ClipRect.w));
    if(clip.right>clip.left&&clip.bottom>clip.top){context_->RSSetScissorRects(1,&clip);context_->Draw(3,0);}
    buffer=previous.Get();context_->PSSetConstantBuffers(0,1,&buffer);
    // Remove the snapshot SRV before next frame's CopyResource (even if this is
    // the final draw command); ImGui's outer backup restores the game's SRV.
    texture=nullptr;context_->PSSetShaderResources(0,1,&texture);
}
void Dx11Glass::clear() {
    beginFrame();
    view_.Reset();snapshot_.Reset();sampler_.Reset();constants_.Reset();vertex_.Reset();pixel_.Reset();
    width_=height_=0;format_=DXGI_FORMAT_UNKNOWN;attempted_=initialized_=false;
}
}
