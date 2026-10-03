# Graphics backends

All five adapters are implemented and registered. Lua, telemetry, scene retention,
and ImGui draw-data generation are shared. Native resources, submission, and
lifecycle handling belong to each graphics adapter.

| API | GPU adapter | Automatic game integration | Validation |
| --- | --- | --- | --- |
| DX9/9Ex | Default-pool textures, RGBA-to-BGRA upload, state restoration | Device Present/PresentEx and Reset/ResetEx | Windows x64 cross-build |
| DX10 | Immutable textures/SRVs, ImGui DX10 submission, target restoration | Shared DXGI dispatcher | Windows x64 cross-build |
| DX11 | Immutable textures/SRVs, immediate-context submission | Shared DXGI dispatcher | Windows x64 cross-build |
| DX12 | SRV/RTV heaps, texture uploads, command lists, frame fences, PRESENT transitions | DXGI dispatcher plus swap-chain creation/queue tracking | Windows x64 cross-build |
| Vulkan | Image staging, descriptors, LOAD render pass, layouts, command buffers, fence completion | Loader/proc-address interception and device/queue/swap-chain tracking | Windows x64 cross-build; adapter also syntax-checked on macOS |

These are initial implementations. **No native GPU path or game has been run on
this macOS development machine.** Cross-compilation proves the source compiles
and links against the Windows/Vulkan APIs; it does not establish runtime behavior,
performance, or game compatibility.

## Selection and build

`ZTMZ_RENDER_BACKENDS` is a semicolon-separated CMake list. Windows defaults to
`dx9;dx10;dx11;dx12`; Vulkan is opt-in because it requires the Vulkan SDK (1.3.290
or newer) at build time and the Vulkan loader on the target machine.

```powershell
cmake -S . -B out -A x64 "-DZTMZ_RENDER_BACKENDS=dx9;dx10;dx11;dx12;vulkan"
cmake --build out --config Debug --parallel
```

The DLL reads `ZTMZ_RENDER_API=auto` (default), `dx9`, `dx10`, `dx11`, `dx12`, or
`vulkan` from the game process environment. Explicit unavailable selections fail
startup. Auto mode continues with other hook families if one cannot initialize.
The first verified supported presentation claims the overlay; loading a graphics
DLL or creating a probe device does not claim it. Stop before changing selection.
The standalone viewer remains a DX11 harness.

## Hook and loading requirements

DX10/11/12 share one DXGI interceptor for Present/Present1 and
ResizeBuffers/ResizeBuffers1. Per-API presenters verify the native device and
invoke their own adapter. Nested Present/Present1 calls are guarded against double
drawing. Present1 sends a full-frame update when the HUD is drawn, because the
HUD can change pixels outside the game's dirty rectangles.

DX9 handles the primary device's Present/PresentEx and reset paths. Additional
swap-chain-only presentation and unusual EndScene-only integrations are not
covered. Reset releases all default-pool resources before forwarding the call.

**DX12 automatic tracking requires loading before swap-chain creation.** Factory
CreateSwapChain, CreateSwapChainForHwnd, CreateSwapChainForCoreWindow, and
CreateSwapChainForComposition capture the actual direct command queue. It is
stored on the swap chain through a private COM interface reference, so its
lifetime follows the swap chain. ResizeBuffers1 updates the association when all
buffers use one queue. Multi-queue resize associations are rejected. An already
existing untracked DX12 swap chain is skipped; the client never guesses a queue.
See [Microsoft's queue contract](https://learn.microsoft.com/en-us/windows/win32/api/dxgi/nf-dxgi-idxgifactory-createswapchain).

**Vulkan automatic tracking requires early loading**, before physical-device
enumeration, device creation, queue retrieval, and swap-chain creation. Exported
loader functions and pointers returned through GetInstanceProcAddr/GetDeviceProcAddr
are intercepted. Already-created devices or dispatch pointers cached before the
DLL was loaded cannot be reconstructed by this implementation and are skipped.

The Vulkan path currently accepts one swap chain per presentation, one image
layer, an unprotected device/image, a graphics-capable presenting queue, and a
swap chain with COLOR_ATTACHMENT usage. Exclusive-sharing images require a
single created graphics queue family; concurrent images require the presenting
family in their sharing list. Device groups, incremental-present regions, separate
present-only queues, and multi-swap-chain presents pass through without a HUD.
Swap-chain/device destruction releases their overlay resources before forwarding.

## Synchronization and lifetime

DX12 maintains a ring of overlay command allocators/lists and completion fences.
A frame slot is reset only after its previous submission completes. Texture or
SRV replacement waits for outstanding overlay work; descriptors are not reused
while the GPU may read them. Back buffers transition PRESENT -> RENDER_TARGET ->
PRESENT on the captured direct queue. Resize destroys the rendering session after
waiting for its fences, releasing back-buffer references before DXGI resizes.

Vulkan currently uses a **synchronous completion fence per overlay submission**.
The overlay submission consumes the game's presentation wait semaphores exactly
once, preserves existing image contents through a LOAD render pass, and completes
before the original presentation is forwarded with an empty wait list. This
avoids introducing presentation-semaphore reuse hazards, but it adds a CPU/GPU
synchronization point each frame. Performance has not been measured; an
asynchronous version must implement correct per-image semaphore retirement.
See [Khronos' presentation synchronization discussion](https://docs.vulkan.org/guide/latest/swapchain_semaphore_reuse.html).

GPU errors after the original waits have been consumed are propagated instead of
waiting on those binary semaphores a second time. Native resource retirement is
serialized with the hook's render path. Stop releases renderer resources while
the process-lifetime hooks remain pass-through; hot unloading is unsupported.

## Shared contracts

`RenderBackend` owns initialization, images, per-frame preparation, typed native
submission, invalidation, and shutdown. `RenderSession` owns ImGui context/frame
lifetime and generates the same draw data for every adapter. It rejects an API
mismatch and restores the previous context on success and failure. Lua runtime
state survives graphics-device resets; GPU resources are rebuilt independently.

`NativeFrame` carries typed API-specific handles. DX9/10/11 use target surfaces or
views, DX12 receives the current back-buffer resource, and Vulkan receives the
present image, extent, and original wait list plus its completion outcome.

The source is split into `graphics/dx9`, `dx10`, `dx11`, `dx12`, and `vulkan`.
`ztmz_transport_winhttp` remains independent of graphics. The Vulkan SDK dependency
is linked only when Vulkan is requested.

## Remaining runtime work

Windows driver/game validation is needed for initialization, texture fidelity,
Alt-Tab/fullscreen changes, resize/reset, device removal, interactions with other
overlays, and shutdown. HDR color handling, VR/multi-view rendering, custom fonts,
and broad game coverage are not established. The early-loading and Vulkan
synchronous-submission limits above are intentional and visible.
