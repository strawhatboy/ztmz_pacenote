#pragma once
#include <dxgi.h>
#include <d3d12.h>
#include <wrl/client.h>
namespace ztmz {
void installDx12QueueCapture();
void setDx12QueueFor(IDXGISwapChain*,IUnknown*) noexcept;
Microsoft::WRL::ComPtr<ID3D12CommandQueue> dx12QueueFor(IDXGISwapChain* swap);
}
