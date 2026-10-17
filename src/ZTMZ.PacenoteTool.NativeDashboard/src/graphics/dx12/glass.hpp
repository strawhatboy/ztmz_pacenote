#pragma once
#include "renderer.hpp"
#include "graphics/glass_capture.hpp"
#include <d3d12.h>
#include <wrl/client.h>
#include <vector>
namespace ztmz {
class Dx12Glass {
    template<class T> using ComPtr=Microsoft::WRL::ComPtr<T>;
    ComPtr<ID3D12Device> device_;
    ComPtr<ID3D12RootSignature> root_;
    ComPtr<ID3D12PipelineState> pipeline_;
    struct Snapshot {
        ComPtr<ID3D12Resource> image;
        D3D12_CPU_DESCRIPTOR_HANDLE cpu;
        D3D12_GPU_DESCRIPTOR_HANDLE gpu;
        unsigned width=0,height=0;
    };
    std::vector<Snapshot> snapshots_;
    DXGI_FORMAT format_;
    unsigned current_=0;
    bool attempted_=false,prepared_=false,hasRegion_=false;
    D3D12_BOX region_{};
    ID3D12GraphicsCommandList* command_=nullptr; // borrowed during submit only
    bool initialize();
    void drawPanel(const GlassPanel&,const ImDrawCmd&);
public:
    Dx12Glass(ID3D12Device*,DXGI_FORMAT,
        const std::vector<D3D12_CPU_DESCRIPTOR_HANDLE>&,const std::vector<D3D12_GPU_DESCRIPTOR_HANDLE>&);
    void beginFrame();
    GlassDraw prepare(ID3D12Resource*,unsigned frameIndex);
    // Called before HUD submission. Captured source is left in RENDER_TARGET.
    GlassCaptureStats capture(ID3D12GraphicsCommandList*,ID3D12Resource*);
    void endFrame() {command_=nullptr;}
    // Caller must first wait for all submitted frames to finish.
    void clear();
};
}
