#pragma once
#include <d3d12.h>
#include <dxgi.h>
#include <wrl/client.h>
namespace ztmz {
// Compatibility resolver. Never replaces a queue captured through a DXGI API.
Microsoft::WRL::ComPtr<ID3D12CommandQueue> findLateDx12Queue(IDXGISwapChain* swap);
}
