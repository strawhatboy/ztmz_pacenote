# Reliable graphics attachment

## Goal

Both startup orders must work: ZTMZ before the game and ZTMZ after the game
has entered a stage. Preserve x86 RBR support and the shared WPF/Avalonia
controller. Do not describe missing implementation as an API impossibility.

## Evidence and decision (2026-10-09)

The current DX12 adapter captures a queue through factory creation hooks and
ResizeBuffers1. Installing these after creation misses the association.

Reviewed https://github.com/veeenu/hudhook/blob/main/src/hooks/dx12.rs:
`InitializationContext::insert_command_queue` observes ExecuteCommandLists;
`check_command_queue` scans up to 512 pointer slots in the swapchain's readable
private memory for that queue. Creation/ResizeBuffers1 paths also exist.
This demonstrates a late-attachment technique, not a documented DXGI guarantee.
Readable memory and matching device identity alone do not prove ownership of
the presenting queue. Do not select the first direct queue on a device.

Do not require a startup proxy merely because our initial implementation lacks
late discovery. Evaluate dynamic association before installing game-directory
components. Do not adopt a private-layout scan as the sole production contract.
Keep association discovery separate from rendering and expose its provenance.

## Implementation order and acceptance gates

1. Separate capture from activation. A callable, versioned native preparation
   entry point installs DX12 queue capture outside DllMain without networking,
   rendering or selecting an API. Stop leaves capture intact. Verify a queue
   created during this inactive period remains associated with its swapchain.
2. Evaluate late association in a controlled DX12 host: multiple direct queues,
   unrelated queues, different devices, swapchain replacement and resize.
   Compare hudhook-style private-layout discovery with tracking documented
   backbuffer usage/submissions. Record false positives and unsupported cases.
   Do not ship a guessed association or label it Active.
3. Integrate the chosen resolver into the shared native lifecycle. Validate COM
   device identity, queue type, target window and GPU synchronization; invalidate
   on device removal and queue replacement. Log discovery source, failure reason,
   first submission and loss/recovery without per-frame log spam.
4. If a startup component is necessary for a validated backend, implement it as
   an optional shared Windows service used by both hosts: explicit consent,
   per-game ownership manifest and hashes, atomic install/rollback/uninstall,
   host relocation/update handling. Never replace unknown ReShade/DXVK DLLs.
   No hook setup, graphics initialization or waits inside DllMain. A worker
   racing the game's device creation is not a guaranteed early-loading solution.
5. Build/publish both architectures and all required helper resources. Keep
   fonts shared. Test staged output, missing-input failure and uninstall without
   repeatedly rebuilding the entire installer. EA WRC remains excluded.
6. Game acceptance: ACR DX12, RBR x86 DX9, then BeamNG Vulkan independently.
   Vulkan needs its own device/queue/surface discovery; DX12 success proves
   nothing about Vulkan. Re-run DR2's previously passing scenarios.

For every supported game/backend: both startup orders, host restart, switching
backends, Web disabled, fullscreen round trip, swapchain/device recreation,
disconnect and game exit. Controlled-host tests precede real-game tests; neither
a successful compilation nor successful injection counts as rendering success.

## Progress

- Research and revised architecture recorded.
- Capture-only preparation implemented as `ZtmzDashboardPrepare(1)`; x64 and
  x86 native builds pass. Production-DLL DX12 WARP test passes: unknown ABI
  rejection, repeat preparation, two distinct direct queues, no invented
  association for a preexisting swapchain, capture retained through Stop and
  swapchain replacement. This is a foundation, not late-attachment completion.
- Dynamic resolver implemented as a separate compatibility path, after exact
  factory/ResizeBuffers1 capture. It calibrates pointer slots against two owned
  swapchains with distinct queues (no fixed offset); requires at least two slots
  that all agree, the same DXGI vtable module, an observed nonempty direct-queue
  submission and matching COM device identity. Reads use ReadProcessMemory and
  never dereference a discovered pointer. Candidate ownership is bounded to 64
  retained queues. Calibration failure is terminal for that process to avoid
  stalling every frame with retries. It is **still private-layout dependent**;
  calibration reduces false matches, not an official ownership guarantee.
- Controlled x64 WARP and NVIDIA GTX 1650 SUPER tests passed for two swapchains,
  an unrelated direct queue, another device's submission, resize and replacement
  with three buffers. The fixture DLL also passed
  late-attachment GPU pixel readback, Stop/restart and resize on both adapters.
  Shared host status logs `queueSource` after actual rendering: either
  `dxgi-creation-or-resize` or `calibrated-private-layout`.
- Backbuffer submission tracking was not selected as an ownership proof: use of
  a resource by a direct queue alone does not establish which queue presents it.
  Cross-queue handoffs require synchronization, not a guess based on last use.
- ACR game E2E, additional drivers/runtime wrappers and device-loss recovery
  remain unverified. Vulkan late discovery and optional bootstrap deployment
  remain pending. No game-directory component is installed by this change.

## DX12 texture/latency regression (2026-10-09)

ACR gameplay exposed a gap in the original solid-color-only GPU fixture.
With pinned ImGui 1.91.9b, font invalidation frees its descriptor through the
callback, but recreation reuses the original handle without allocating again.
The shared descriptor pool allowed an image to take that slot; the subsequent
font upload overwrote its SRV. The font now owns a dedicated descriptor for the
backend lifetime. Image descriptors never use that slot.

Font invalidation also unnecessarily discarded every dashboard image; each image
was then synchronously reuploaded with its own GPU wait. Images now survive font
rebuilds, new images are submitted as one batch, and already-baked U+0020..U+00FF
characters do not request atlas rebuilds when telemetry text changes.

The GPU regression now samples a real image after two successive CJK glyph
updates, plus Stop/restart/resize. It passes on WARP and NVIDIA hardware; shared
font lifecycle tests verify changing numeric text does not invalidate the atlas.
Real ACR frame-time improvement remains to be measured. New CJK glyphs can still
require a synchronous font rebuild, so this is not a claim of zero frame stalls.

## Asynchronous font preparation (supersedes synchronous rebuild above)

Small pixel-size fonts exposed the cost of discovering sizes/glyphs during Present.
RenderSession no longer resolves system fonts or builds the dashboard atlas there.
A process-lifetime worker coalesces text requests and builds independent CPU atlases
without changing the render thread's ImGui context. Until ready, rendering keeps
the previous set (nearest available size) or the bootstrap font. Session destruction
does not join a rasterizing worker. Completed atlases are ordinary image textures;
adopting them does not invalidate ImGui's pipeline or dashboard image cache.

DX12 submits texture uploads before draws on the same queue without a CPU fence
wait. Upload buffers/command lists and replaced image descriptors remain alive
until their fences complete. If a frame resource is busy, the overlay skips its
frame instead of waiting; skipped work does not increment submittedFrames.

Tests cover background atlas arrival without backend invalidation, busy-frame
skipping, repeated CJK updates with GPU image readback, restart and resize. This
removes synchronous rasterization across the shared renderer and font-update GPU
waits in DX12. It is not a universal latency guarantee: driver resource allocation,
initial backend setup, teardown and other API-specific upload paths still have
their own costs. ACR gameplay frame-time validation is still required.

## First-frame preparation across graphics APIs

- DX10/11/12 ImGui shaders are extracted from the pinned upstream backend and
  compiled by a build-only tool. Bytecode is embedded in the DLL; there are no
  additional runtime shader files to package, and no runtime HLSL compilation.
  CMake fails if the expected upstream compilation sites change.
- All injected paths build the bootstrap ImGui context/atlas on a worker.
  DX9 and DX10 retain device access on the game thread (including single-threaded
  devices). Image creation is paced to one missing texture per frame. This avoids
  a package-sized upload burst, but cannot bound an individual driver's API call.
- DX11 prepares device-only resources on a worker when the device supports it;
  `D3D11_CREATE_DEVICE_SINGLETHREADED` uses CPU-only preparation instead. No worker
  uses the game's immediate context. Resize keeps device resources because no
  swap-chain target is retained. New device/swap-chain ownership still resets them.
- DX12 queue calibration and renderer warmup run off Present. DX11/12 share the
  same nonblocking preparation handoff; abandoned work owns COM references and
  never keeps a game backbuffer alive.
- Vulkan builds its private pipeline/device resources on a worker, without any
  worker access to the application queue. Device/swapchain destruction drains
  outstanding preparation before releasing borrowed handles. Bootstrap/image
  uploads join the drawing command buffer, with staging resources held until the
  fence completes. A busy command buffer skips the overlay instead of waiting.
  Presentation waits on an overlay semaphore per swapchain image; these semaphores
  survive overlay stop/restart and are destroyed with their swapchain.
- Vulkan **late attachment remains unsupported**. These changes optimize an
  already captured Vulkan device/queue/swapchain; they do not discover objects
  created before hooks were installed.

Tests include CPU preparation cancellation/error/context ownership, DX9/10/11/12
GPU pixel readback and restart/reset, and a hardware Vulkan early-hook lifecycle
test with texture updates and swapchain recreation. Present timings include driver
presentation and are diagnostics, not a guarantee of game frame times. Real games,
especially legacy translation layers and Vulkan validation-layer coverage, still
need E2E testing. No additional fixed startup delay is introduced.
