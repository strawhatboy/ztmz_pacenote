#pragma once
#include "renderer.hpp"
#include "graphics/dx11/glass_capture.hpp"
#include <d3d11.h>
#include <wrl/client.h>
namespace ztmz {
class Dx11Glass {
    template<class T> using ComPtr=Microsoft::WRL::ComPtr<T>;
    ComPtr<ID3D11Device> device_;
    ComPtr<ID3D11DeviceContext> context_;
    ComPtr<ID3D11VertexShader> vertex_;
    ComPtr<ID3D11PixelShader> pixel_;
    ComPtr<ID3D11Buffer> constants_;
    ComPtr<ID3D11SamplerState> sampler_;
    ComPtr<ID3D11Texture2D> snapshot_;
    ComPtr<ID3D11ShaderResourceView> view_;
    ComPtr<ID3D11Texture2D> pendingSource_;
    D3D11_BOX captureBox_{};
    bool pendingMsaa_=false, hasRegion_=false;
    unsigned width_=0,height_=0;
    DXGI_FORMAT format_=DXGI_FORMAT_UNKNOWN;
    bool attempted_=false,initialized_=false;
    bool initialize();
    void drawPanel(const GlassPanel&,const ImDrawCmd&);
public:
    Dx11Glass(ID3D11Device* device,ID3D11DeviceContext* context):device_(device),context_(context){}
    GlassDraw prepare(ID3D11RenderTargetView* target);
    void beginFrame();
    Dx11GlassCaptureStats capture();
    void clear();
};
}
