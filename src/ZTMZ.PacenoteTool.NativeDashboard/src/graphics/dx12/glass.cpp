#include "graphics/dx12/glass.hpp"
#include "glass_shaders.hpp"
#include <algorithm>
#include <cmath>
namespace ztmz {
namespace {
void barrier(ID3D12GraphicsCommandList* cmd,ID3D12Resource* image,D3D12_RESOURCE_STATES from,D3D12_RESOURCE_STATES to) {
    D3D12_RESOURCE_BARRIER b{};b.Type=D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
    b.Transition={image,D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES,from,to};cmd->ResourceBarrier(1,&b);
}
}
Dx12Glass::Dx12Glass(ID3D12Device* device,DXGI_FORMAT format,
    const std::vector<D3D12_CPU_DESCRIPTOR_HANDLE>& cpu,const std::vector<D3D12_GPU_DESCRIPTOR_HANDLE>& gpu)
    :device_(device),format_(format) {
    for(size_t i=0;i<cpu.size();++i){Snapshot s;s.cpu=cpu[i];s.gpu=gpu.at(i);snapshots_.push_back(std::move(s));}
}
bool Dx12Glass::initialize() {
    if(attempted_)return pipeline_!=nullptr;
    attempted_=true;
    D3D12_DESCRIPTOR_RANGE range{};range.RangeType=D3D12_DESCRIPTOR_RANGE_TYPE_SRV;range.NumDescriptors=1;
    D3D12_ROOT_PARAMETER parameters[2]{};
    parameters[0].ParameterType=D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE;
    parameters[0].DescriptorTable={1,&range};parameters[0].ShaderVisibility=D3D12_SHADER_VISIBILITY_PIXEL;
    parameters[1].ParameterType=D3D12_ROOT_PARAMETER_TYPE_32BIT_CONSTANTS;
    parameters[1].Constants={0,0,16};parameters[1].ShaderVisibility=D3D12_SHADER_VISIBILITY_PIXEL;
    D3D12_STATIC_SAMPLER_DESC sampler{};sampler.Filter=D3D12_FILTER_MIN_MAG_MIP_LINEAR;
    sampler.AddressU=sampler.AddressV=sampler.AddressW=D3D12_TEXTURE_ADDRESS_MODE_CLAMP;
    sampler.MaxLOD=D3D12_FLOAT32_MAX;sampler.ComparisonFunc=D3D12_COMPARISON_FUNC_ALWAYS;
    sampler.ShaderVisibility=D3D12_SHADER_VISIBILITY_PIXEL;
    D3D12_ROOT_SIGNATURE_DESC signature{};signature.NumParameters=2;signature.pParameters=parameters;
    signature.NumStaticSamplers=1;signature.pStaticSamplers=&sampler;
    signature.Flags=D3D12_ROOT_SIGNATURE_FLAG_ALLOW_INPUT_ASSEMBLER_INPUT_LAYOUT;
    ComPtr<ID3DBlob> code,error;
    if(FAILED(D3D12SerializeRootSignature(&signature,D3D_ROOT_SIGNATURE_VERSION_1,&code,&error))||
        FAILED(device_->CreateRootSignature(0,code->GetBufferPointer(),code->GetBufferSize(),IID_PPV_ARGS(&root_))))return false;
    D3D12_GRAPHICS_PIPELINE_STATE_DESC state{};state.pRootSignature=root_.Get();
    state.VS={glass_vs,sizeof(glass_vs)};state.PS={glass_ps,sizeof(glass_ps)};
    auto& blend=state.BlendState.RenderTarget[0];blend.BlendEnable=TRUE;
    blend.SrcBlend=D3D12_BLEND_SRC_ALPHA;blend.DestBlend=D3D12_BLEND_INV_SRC_ALPHA;blend.BlendOp=D3D12_BLEND_OP_ADD;
    blend.SrcBlendAlpha=D3D12_BLEND_ONE;blend.DestBlendAlpha=D3D12_BLEND_INV_SRC_ALPHA;blend.BlendOpAlpha=D3D12_BLEND_OP_ADD;
    blend.RenderTargetWriteMask=D3D12_COLOR_WRITE_ENABLE_ALL;
    state.SampleMask=UINT_MAX;state.RasterizerState.FillMode=D3D12_FILL_MODE_SOLID;
    state.RasterizerState.CullMode=D3D12_CULL_MODE_NONE;state.RasterizerState.DepthClipEnable=TRUE;
    state.DepthStencilState.DepthFunc=D3D12_COMPARISON_FUNC_ALWAYS;
    state.DepthStencilState.FrontFace.StencilFailOp=state.DepthStencilState.FrontFace.StencilDepthFailOp=
        state.DepthStencilState.FrontFace.StencilPassOp=D3D12_STENCIL_OP_KEEP;
    state.DepthStencilState.FrontFace.StencilFunc=D3D12_COMPARISON_FUNC_ALWAYS;
    state.DepthStencilState.BackFace=state.DepthStencilState.FrontFace;
    state.PrimitiveTopologyType=D3D12_PRIMITIVE_TOPOLOGY_TYPE_TRIANGLE;
    state.NumRenderTargets=1;state.RTVFormats[0]=format_;state.SampleDesc.Count=1;
    return SUCCEEDED(device_->CreateGraphicsPipelineState(&state,IID_PPV_ARGS(&pipeline_)));
}
void Dx12Glass::beginFrame(){prepared_=hasRegion_=false;command_=nullptr;}
GlassDraw Dx12Glass::prepare(ID3D12Resource* source,unsigned index) {
    if(!source||index>=snapshots_.size())return {};
    auto d=source->GetDesc();auto display=ImGui::GetIO().DisplaySize;
    if(d.Dimension!=D3D12_RESOURCE_DIMENSION_TEXTURE2D||d.DepthOrArraySize!=1||d.MipLevels!=1||
        d.SampleDesc.Count!=1||d.Width!=static_cast<unsigned>(display.x)||d.Height!=static_cast<unsigned>(display.y))return {};
    if(format_!=DXGI_FORMAT_R8G8B8A8_UNORM&&format_!=DXGI_FORMAT_R8G8B8A8_UNORM_SRGB&&
        format_!=DXGI_FORMAT_B8G8R8A8_UNORM&&format_!=DXGI_FORMAT_B8G8R8A8_UNORM_SRGB&&
        format_!=DXGI_FORMAT_R10G10B10A2_UNORM&&format_!=DXGI_FORMAT_R16G16B16A16_FLOAT)return {};
    D3D12_FEATURE_DATA_FORMAT_SUPPORT support{format_};
    if(FAILED(device_->CheckFeatureSupport(D3D12_FEATURE_FORMAT_SUPPORT,&support,sizeof(support)))||
        !(support.Support1&D3D12_FORMAT_SUPPORT1_SHADER_SAMPLE)||!initialize())return {};
    auto& snapshot=snapshots_[index];
    // The backend only selects an allocator/snapshot after its fence completes.
    // Other in-flight frames have their own texture and immutable SRV descriptor.
    if(!snapshot.image||snapshot.width!=d.Width||snapshot.height!=d.Height) {
        snapshot.image.Reset();snapshot.width=snapshot.height=0;
        D3D12_HEAP_PROPERTIES heap{};heap.Type=D3D12_HEAP_TYPE_DEFAULT;heap.CreationNodeMask=heap.VisibleNodeMask=1;
        D3D12_RESOURCE_DESC copy{};copy.Dimension=D3D12_RESOURCE_DIMENSION_TEXTURE2D;
        copy.Width=d.Width;copy.Height=d.Height;copy.DepthOrArraySize=copy.MipLevels=1;
        copy.Format=format_;copy.SampleDesc.Count=1;
        if(FAILED(device_->CreateCommittedResource(&heap,D3D12_HEAP_FLAG_NONE,&copy,
            D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE,nullptr,IID_PPV_ARGS(&snapshot.image))))return {};
        D3D12_SHADER_RESOURCE_VIEW_DESC view{};view.Format=format_;view.ViewDimension=D3D12_SRV_DIMENSION_TEXTURE2D;
        view.Shader4ComponentMapping=D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING;view.Texture2D.MipLevels=1;
        device_->CreateShaderResourceView(snapshot.image.Get(),&view,snapshot.cpu);
        snapshot.width=static_cast<unsigned>(d.Width);snapshot.height=d.Height;
    }
    current_=index;prepared_=true;hasRegion_=false;
    return [this](ImDrawList& list,const GlassPanel& panel){
        const auto& s=snapshots_[current_];auto c0=list.GetClipRectMin(),c1=list.GetClipRectMax();
        float l=std::max({0.f,panel.min.x,c0.x}),t=std::max({0.f,panel.min.y,c0.y});
        float r=std::min({float(s.width),panel.max.x,c1.x}),b=std::min({float(s.height),panel.max.y,c1.y});
        if(r<=l||b<=t)return;
        float pad=panel.refraction+panel.blur+2;
        D3D12_BOX box{UINT(std::max(0.f,std::floor(l-pad))),UINT(std::max(0.f,std::floor(t-pad))),0,
            UINT(std::min(float(s.width),std::ceil(r+pad))),UINT(std::min(float(s.height),std::ceil(b+pad))),1};
        if(!hasRegion_){region_=box;hasRegion_=true;}else{
            region_.left=std::min(region_.left,box.left);region_.top=std::min(region_.top,box.top);
            region_.right=std::max(region_.right,box.right);region_.bottom=std::max(region_.bottom,box.bottom);
        }
        struct Payload{Dx12Glass* owner;GlassPanel panel;};Payload payload{this,panel};
        list.AddCallback([](const ImDrawList*,const ImDrawCmd* cmd){
            auto& p=*static_cast<const Payload*>(cmd->UserCallbackData);p.owner->drawPanel(p.panel,*cmd);
        },&payload,sizeof(payload));
        list.AddCallback(ImDrawCallback_ResetRenderState,nullptr);
    };
}
GlassCaptureStats Dx12Glass::capture(ID3D12GraphicsCommandList* cmd,ID3D12Resource* source) {
    if(!prepared_||!hasRegion_)return {};
    command_=cmd;auto& s=snapshots_[current_];
    barrier(cmd,source,D3D12_RESOURCE_STATE_PRESENT,D3D12_RESOURCE_STATE_COPY_SOURCE);
    barrier(cmd,s.image.Get(),D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE,D3D12_RESOURCE_STATE_COPY_DEST);
    D3D12_TEXTURE_COPY_LOCATION from{},to{};from.pResource=source;to.pResource=s.image.Get();
    from.Type=to.Type=D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX;
    cmd->CopyTextureRegion(&to,region_.left,region_.top,0,&from,&region_);
    barrier(cmd,s.image.Get(),D3D12_RESOURCE_STATE_COPY_DEST,D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE);
    barrier(cmd,source,D3D12_RESOURCE_STATE_COPY_SOURCE,D3D12_RESOURCE_STATE_RENDER_TARGET);
    return {1,region_.right-region_.left,region_.bottom-region_.top,false};
}
void Dx12Glass::drawPanel(const GlassPanel& p,const ImDrawCmd& cmd) {
    if(!command_)return;
    auto& s=snapshots_[current_];
    float values[16]={p.min.x,p.min.y,p.max.x,p.max.y,p.radius,p.refraction,p.blur,0,
        p.tint.x,p.tint.y,p.tint.z,p.tint.w,float(s.width),float(s.height),0,0};
    command_->SetPipelineState(pipeline_.Get());command_->SetGraphicsRootSignature(root_.Get());
    command_->SetGraphicsRootDescriptorTable(0,s.gpu);command_->SetGraphicsRoot32BitConstants(1,16,values,0);
    command_->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
    D3D12_VIEWPORT viewport{0,0,float(s.width),float(s.height),0,1};command_->RSSetViewports(1,&viewport);
    D3D12_RECT clip{LONG(std::max({0.f,cmd.ClipRect.x,p.min.x-1})),LONG(std::max({0.f,cmd.ClipRect.y,p.min.y-1})),
        LONG(std::min({float(s.width),cmd.ClipRect.z,p.max.x+1})),LONG(std::min({float(s.height),cmd.ClipRect.w,p.max.y+1}))};
    if(clip.right>clip.left&&clip.bottom>clip.top){command_->RSSetScissorRects(1,&clip);command_->DrawInstanced(3,1,0,0);}
}
void Dx12Glass::clear(){beginFrame();for(auto& s:snapshots_){s.image.Reset();s.width=s.height=0;}pipeline_.Reset();root_.Reset();attempted_=false;}
}
