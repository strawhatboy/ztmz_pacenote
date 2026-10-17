#pragma once
#include "renderer.hpp"
#include "graphics/dx10/glass_capture.hpp"
#include <d3d10.h>
#include <wrl/client.h>
namespace ztmz {
class Dx10Glass {
    template<class T> using ComPtr=Microsoft::WRL::ComPtr<T>;
    ComPtr<ID3D10Device> device_;
    ComPtr<ID3D10VertexShader> vertex_;
    ComPtr<ID3D10PixelShader> pixel_;
    ComPtr<ID3D10Buffer> constants_;
    ComPtr<ID3D10SamplerState> sampler_;
    ComPtr<ID3D10Texture2D> snapshot_;
    ComPtr<ID3D10ShaderResourceView> view_;
    ComPtr<ID3D10Texture2D> pendingSource_;
    D3D10_BOX captureBox_{};
    bool pendingMsaa_=false, hasRegion_=false;
    unsigned width_=0,height_=0;
    DXGI_FORMAT format_=DXGI_FORMAT_UNKNOWN;
    bool attempted_=false,initialized_=false;
    bool initialize();
    void drawPanel(const GlassPanel&,const ImDrawCmd&);
public:
    Dx10Glass(ID3D10Device* device):device_(device){}
    GlassDraw prepare(ID3D10RenderTargetView* target);
    void beginFrame();
    Dx10GlassCaptureStats capture();
    void clear();
};
}
