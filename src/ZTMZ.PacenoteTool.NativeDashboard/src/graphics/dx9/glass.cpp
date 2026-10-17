#include "graphics/dx9/glass.hpp"
#include "dx9_glass_shaders.hpp"
#include <algorithm>
#include <cmath>
namespace ztmz {
bool Dx9Glass::initialize() {
    if(attempted_)return initialized_;attempted_=true;
    D3DCAPS9 caps{};
    if(FAILED(device_->GetDeviceCaps(&caps))||caps.VertexShaderVersion<D3DVS_VERSION(3,0)||caps.PixelShaderVersion<D3DPS_VERSION(3,0))return false;
    const D3DVERTEXELEMENT9 elements[]={{0,0,D3DDECLTYPE_FLOAT2,D3DDECLMETHOD_DEFAULT,D3DDECLUSAGE_POSITION,0},D3DDECL_END()};
    if(FAILED(device_->CreateVertexShader(reinterpret_cast<const DWORD*>(glass_vs),&vertex_))||
       FAILED(device_->CreatePixelShader(reinterpret_cast<const DWORD*>(glass_ps),&pixel_))||
       FAILED(device_->CreateVertexDeclaration(elements,&declaration_))||
       FAILED(device_->CreateStateBlock(D3DSBT_ALL,&state_)))return false;
    initialized_=true;return true;
}
GlassDraw Dx9Glass::prepare(IDirect3DSurface9* target) {
    if(!target||!initialize())return {};
    D3DSURFACE_DESC desc{};if(FAILED(target->GetDesc(&desc)))return {};
    auto display=ImGui::GetIO().DisplaySize;
    if(desc.Width!=static_cast<unsigned>(display.x)||desc.Height!=static_cast<unsigned>(display.y)||
       !(desc.Usage&D3DUSAGE_RENDERTARGET)||desc.Pool!=D3DPOOL_DEFAULT||
       (desc.Format!=D3DFMT_A8R8G8B8&&desc.Format!=D3DFMT_X8R8G8B8))return {};
    if(!snapshot_||width_!=desc.Width||height_!=desc.Height||format_!=desc.Format){
        surface_.Reset();snapshot_.Reset();width_=height_=0;format_=D3DFMT_UNKNOWN;
        if(FAILED(device_->CreateTexture(desc.Width,desc.Height,1,D3DUSAGE_RENDERTARGET,desc.Format,D3DPOOL_DEFAULT,&snapshot_,nullptr))||
           FAILED(snapshot_->GetSurfaceLevel(0,&surface_)))return {};
        width_=desc.Width;height_=desc.Height;format_=desc.Format;
    }
    pendingSource_=target;pendingMsaa_=desc.MultiSampleType!=D3DMULTISAMPLE_NONE;hasRegion_=captureReady_=false;
    return [this](ImDrawList& list,const GlassPanel& panel){
        auto clipMin=list.GetClipRectMin(),clipMax=list.GetClipRectMax();
        float left=std::max({0.f,panel.min.x,clipMin.x}),top=std::max({0.f,panel.min.y,clipMin.y});
        float right=std::min({static_cast<float>(width_),panel.max.x,clipMax.x}),bottom=std::min({static_cast<float>(height_),panel.max.y,clipMax.y});
        if(right<=left||bottom<=top)return;
        float padding=panel.refraction+panel.blur+2;
        RECT box{static_cast<LONG>(std::max(0.f,std::floor(left-padding))),static_cast<LONG>(std::max(0.f,std::floor(top-padding))),
            static_cast<LONG>(std::min(static_cast<float>(width_),std::ceil(right+padding))),
            static_cast<LONG>(std::min(static_cast<float>(height_),std::ceil(bottom+padding)))};
        if(!hasRegion_){region_=box;hasRegion_=true;}
        else {region_.left=std::min(region_.left,box.left);region_.top=std::min(region_.top,box.top);region_.right=std::max(region_.right,box.right);region_.bottom=std::max(region_.bottom,box.bottom);}
        struct Payload {Dx9Glass* owner;GlassPanel panel;};Payload payload{this,panel};
        list.AddCallback([](const ImDrawList*,const ImDrawCmd* cmd){auto& p=*static_cast<const Payload*>(cmd->UserCallbackData);p.owner->drawPanel(p.panel,*cmd);},&payload,sizeof(payload));
        list.AddCallback(ImDrawCallback_ResetRenderState,nullptr);
    };
}
void Dx9Glass::beginFrame(){pendingSource_.Reset();hasRegion_=pendingMsaa_=captureReady_=false;}
Dx9GlassCaptureStats Dx9Glass::capture(){
    Dx9GlassCaptureStats stats;if(!pendingSource_||!hasRegion_)return stats;
    // StretchRect must run outside BeginScene/EndScene, before any HUD drawing.
    const RECT* region=pendingMsaa_?nullptr:&region_;
    captureReady_=SUCCEEDED(device_->StretchRect(pendingSource_.Get(),region,surface_.Get(),region,D3DTEXF_NONE));
    if(captureReady_)stats={1,pendingMsaa_?width_:static_cast<unsigned>(region_.right-region_.left),
        pendingMsaa_?height_:static_cast<unsigned>(region_.bottom-region_.top),pendingMsaa_};
    pendingSource_.Reset();hasRegion_=false;return stats;
}
void Dx9Glass::drawPanel(const GlassPanel& panel,const ImDrawCmd& cmd){
    if(FAILED(state_->Capture()))return;
    // DrawPrimitiveUP clears stream 0; ImGui's reset callback doesn't restore
    // streams/FVF or shader constants. Restore a captured state block ourselves.
    struct Restore {IDirect3DStateBlock9* state;~Restore(){state->Apply();}} restore{state_.Get()};
    const float constants[16]={panel.min.x,panel.min.y,panel.max.x,panel.max.y,panel.radius,panel.refraction,panel.blur,captureReady_?1.f:0.f,
        panel.tint.x,panel.tint.y,panel.tint.z,panel.tint.w,static_cast<float>(width_),static_cast<float>(height_),0,0};
    device_->SetVertexShader(vertex_.Get());device_->SetPixelShader(pixel_.Get());device_->SetVertexDeclaration(declaration_.Get());
    device_->SetVertexShaderConstantF(0,constants,4);device_->SetPixelShaderConstantF(0,constants,4);
    device_->SetTexture(0,snapshot_.Get());device_->SetSamplerState(0,D3DSAMP_MINFILTER,D3DTEXF_LINEAR);device_->SetSamplerState(0,D3DSAMP_MAGFILTER,D3DTEXF_LINEAR);
    device_->SetSamplerState(0,D3DSAMP_MIPFILTER,D3DTEXF_NONE);device_->SetSamplerState(0,D3DSAMP_ADDRESSU,D3DTADDRESS_CLAMP);device_->SetSamplerState(0,D3DSAMP_ADDRESSV,D3DTADDRESS_CLAMP);
    device_->SetSamplerState(0,D3DSAMP_SRGBTEXTURE,FALSE);device_->SetRenderState(D3DRS_SRGBWRITEENABLE,FALSE);
    RECT clip{static_cast<LONG>(std::max(cmd.ClipRect.x,panel.min.x)),static_cast<LONG>(std::max(cmd.ClipRect.y,panel.min.y)),
        static_cast<LONG>(std::min(cmd.ClipRect.z,std::ceil(panel.max.x))),static_cast<LONG>(std::min(cmd.ClipRect.w,std::ceil(panel.max.y)))};
    if(clip.right<=clip.left||clip.bottom<=clip.top)return;
    device_->SetScissorRect(&clip);
    const float vertices[]={-0.5f,-0.5f,2*width_-0.5f,-0.5f,-0.5f,2*height_-0.5f};
    device_->DrawPrimitiveUP(D3DPT_TRIANGLELIST,1,vertices,2*sizeof(float));
}
void Dx9Glass::clear(){
    beginFrame();state_.Reset();surface_.Reset();snapshot_.Reset();declaration_.Reset();vertex_.Reset();pixel_.Reset();
    width_=height_=0;format_=D3DFMT_UNKNOWN;attempted_=initialized_=false;
}
}
