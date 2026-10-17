#pragma once
#include "renderer.hpp"
#include "graphics/dx9/glass_capture.hpp"
#include <d3d9.h>
#include <wrl/client.h>
namespace ztmz {
class Dx9Glass {
    template<class T> using ComPtr=Microsoft::WRL::ComPtr<T>;
    ComPtr<IDirect3DDevice9> device_;
    ComPtr<IDirect3DVertexShader9> vertex_;
    ComPtr<IDirect3DPixelShader9> pixel_;
    ComPtr<IDirect3DVertexDeclaration9> declaration_;
    ComPtr<IDirect3DStateBlock9> state_;
    ComPtr<IDirect3DTexture9> snapshot_;
    ComPtr<IDirect3DSurface9> surface_,pendingSource_;
    RECT region_{};
    unsigned width_=0,height_=0;
    D3DFORMAT format_=D3DFMT_UNKNOWN;
    bool attempted_=false,initialized_=false,hasRegion_=false,pendingMsaa_=false,captureReady_=false;
    bool initialize();
    void drawPanel(const GlassPanel&,const ImDrawCmd&);
public:
    explicit Dx9Glass(IDirect3DDevice9* device):device_(device){}
    GlassDraw prepare(IDirect3DSurface9* target);
    void beginFrame();
    Dx9GlassCaptureStats capture();
    void clear();
};
}
