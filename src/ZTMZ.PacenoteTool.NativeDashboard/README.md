# Native Lua dashboard

Native C++ dashboard client for ZTMZ. **Lua 5.4.8 is compiled into this project**;
each dashboard has its own Lua VM. The client downloads existing dashboard
packages and runs their `onInit`, `onUpdate`, and `onExit` functions locally.
The Lua source and telemetry do not need a browser or .NET runtime in the client.

```text
ZTMZ /api/dashboard/{id} -> Lua/common scripts/settings/images -> native Lua VM
ZTMZ /ws telemetry      -> GameData/GameContext                -> onUpdate
                                                               -> native draw commands
                                                               -> ImGui -> graphics adapter
```

## Targets and layout

- `ztmz_dashboard_core`: portable Lua host, package/scene lifecycle, and protocol.
- `ztmz_dashboard_renderer`: portable ImGui commands, rendering session, and API selection.
- `ztmz_backend_dx9/dx10/dx11/dx12/vulkan`: API-specific GPU resources and submission.
- `ztmz_dashboard_viewer.exe`: standalone Win32/DX11 rendering harness.
- `ztmz_dashboard_overlay.dll`: registered DX9, DXGI, and Vulkan hook families,
  sharing the same Lua runtime and rendering session as the viewer.
  Its lifecycle is independent of the selected graphics adapter.
- `lua/host.lua`: implementation of the existing dashboard host API, embedded
  into the binary at build time. It does not need to be deployed separately.
- `src/lua_dashboard.cpp`: actual Lua C API integration, VM ownership, limits,
  JSON bridging, and script execution.
- `src/dashboard_client.cpp`: package loading, scene revisions, reloads, telemetry,
  hybrid composition, and explicit error fallback.

This project has its own CMake build alongside WebDashboard. The managed solution
does not build it.

See [graphics backend architecture](docs/RenderBackends.md) for the DX9/DX10/DX11/
DX12/Vulkan implementations, loading requirements, and validation boundaries.
All five adapters are implemented; controlled Windows DX11 validation is described
below. Other native GPU paths and game compatibility remain unverified.

## Build and test

Requires CMake 3.24+, a C++20 compiler, and Python 3 when tests are enabled.
The first configure downloads pinned, SHA-256-verified dependencies: Lua 5.4.8,
nlohmann/json 3.11.3, ImGui 1.91.9b, and MinHook 1.3.4 (Windows only).

From this directory on macOS/Linux:

```sh
cmake -S . -B out -DCMAKE_BUILD_TYPE=Debug
cmake --build out --parallel
ctest --test-dir out --output-on-failure
```

On Windows, use a Visual Studio 2022 developer terminal:

```powershell
cmake -S . -B out -A x64 "-DZTMZ_RENDER_BACKENDS=dx9;dx10;dx11;dx12;vulkan"
cmake --build out --config Debug --parallel
ctest --test-dir out -C Debug --output-on-failure
.\out\Debug\ztmz_dashboard_viewer.exe 8080
```

`ZTMZ_RENDER_BACKENDS` defaults to `dx9;dx10;dx11;dx12` on Windows and empty
elsewhere. Add `vulkan` with Vulkan SDK 1.3.290+ installed. A Vulkan-enabled DLL
also requires the Vulkan loader on the target machine; omit it for machines
without that runtime. Unknown APIs and native backends on non-Windows builds
fail configuration explicitly.

Enable the ZTMZ web dashboard and client-side Lua execution. Enable the desired
dashboards for Web output; the native client currently uses that same selection.
The viewer's status reports `Local Lua: N dashboard(s)` when scripts execute in
its native VMs. It explicitly reports compatibility output or a Lua failure when
the server selects command mode.

## DLL behavior

The DLL starts a bootstrap thread when loaded. Outside the loader lock, it pins
itself for the process lifetime and opens the control pipe without installing
hooks or connecting to telemetry. The host explicitly starts it with a loopback
port, API and target HWND. The legacy Start export uses `ZTMZ_RENDER_API` (default
auto); desktop hosts use the control protocol and DX11 during this validation phase.
Unavailable APIs fail startup. Auto selects a supported native presentation event;
it skips hook families that fail initialization. DX12 requires loading before
swap-chain creation, and Vulkan before graphics-object creation. Late injection
into already-created DX12/Vulkan objects is not supported by the automatic path. Lua, HTTP, image decoding, and WebSocket work run
on the worker; texture upload and drawing run on the presentation thread.

Exports `ZtmzDashboardStop()` and `ZtmzDashboardStart(unsigned short port)` allow a
host to stop/restart the client. Stop releases client/rendering resources while
hooks remain pass-through. **Hot unloading is intentionally unsupported**; restart
the process to unload the pinned module. Do not call these exports from DllMain.
The shared Windows desktop library provides the loader; both UI hosts reuse it.

A versioned local [control pipe](docs/ControlProtocol.md) now supports status,
start and stop, including reconnection while the pinned DLL remains loaded.
The shared Windows desktop session loads or reconnects to versioned x64 modules.
API selection alone does not establish an Active rendering status. See the
[DR2 E2E instructions](../../Docs/InGameDashboardIntegration.md#dr2-e2e-test).

DX9/9Ex, DX10, DX11, DX12, and Vulkan now have native adapters. DXGI supports
Present/Present1 and ResizeBuffers/ResizeBuffers1. Vulkan's initial implementation
uses a CPU fence wait before forwarding presentation, which may affect performance.
Its single-swap-chain, queue-family, image-usage, and early-loading requirements
are documented in the architecture guide. HDR, VR, and broad game compatibility
remain unverified.

## Lua and drawing contract

The host exposes `Graphics`, `GameData`, `GameContext`, `Self.GetConfigByKey`,
`Self.ImageResources`, animation time, localization labels, pacenote resolution,
replay interpolation, and static layers. Wire collections keep the existing
zero-based indexing with `Count`/`length`. Script-created Lua arrays remain
normal one-based Lua arrays. Dashboard code is loaded unchanged.

Rendering covers basic shapes, concave geometry and sampled arcs, gradients,
clipping, text/background text, images, and opacity. Windows uses WIC to decode
package images; the selected backend uploads GPU textures on the render thread. Gradients use
bounded CPU tessellation; exact browser rasterization is not guaranteed.

On Windows, text resolves installed font families/styles through DirectWrite;
the existing WRC Clean regular/bold files are copied beside the native binaries.
Missing glyphs fall back to installed Segoe UI/CJK fonts. Only encountered glyphs
are added to each session's atlas; GPU textures are rebuilt when its contents change.
Lua measurement and ImGui rendering use the same font files and glyph advances.
Missing families/glyphs are reported specifically. This does not implement complex
text shaping, variable-font axes, or exact DirectWrite rasterization. Portable
non-Windows tests retain estimated metrics/default fonts until that platform's
font resolver is implemented. Transform commands from legacy server-only packages
are also not implemented.

The existing protocol names (`browserLuaCapabilities`, `browserLuaTelemetry`)
are reused for interoperability; they do not imply browser execution. HTTP
carries packages/assets, while WebSocket carries telemetry and scene updates.
Unchanged packages retain their Lua state. Configuration revisions reload them;
disconnects discard state. Mixed command/Lua scenes preserve dashboard order.
The server can select compatibility mode if Lua is disabled or another connected
client does not support it. On script/package failure this client clears stale
content, reports the error, and explicitly requests the server fallback.

Each VM has a 32 MiB allocator limit and a two-million-instruction limit per call.
Only base/table/string/math/UTF-8 libraries are exposed; file/OS/debug/module
loading and coroutine/protected-call escape paths are unavailable. Network
messages, downloaded resources, decoded images, and drawing output are bounded.
These are runtime fault limits, not a hardened security boundary for hostile code.

## Validation

CMake build and all five CTest suites passed on macOS with address and undefined
behavior sanitizers. Tests cover lifecycle, VM isolation, telemetry mapping,
static retention, zero-based collections, replay interpolation, reload/reorder,
scene recovery, fallback, memory limits, and infinite-script termination.
Backend contract tests additionally exercise API selection, exclusive ownership,
context restoration, resets, and wrong-API frame rejection using test adapters.
Configuration tests reject unknown APIs and unsupported host-platform selections.

The built-in suite loads all 17 opted-in repository dashboard packages unchanged,
runs four telemetry frames per package, and turns their drawing output into
ImGui vertices in a headless context. It exercises real damage data and pacenote
collections. Image dimensions come from repository PNGs; GPU texture IDs are
substituted in these CPU tests.

The complete Windows x64 viewer and overlay DLL **cross-compile and link** with
all five adapters using llvm-mingw 20260922 (Clang 23). The Vulkan adapter also
passes a native macOS syntax check against Vulkan 1.3.290 headers. The exported
start/stop functions were verified in the resulting PE DLL.

HTTP/WIC/WinHTTP runtime integration, DLL injection, in-game state restoration,
and games remain **unverified**. Neither the portable tests nor cross-compilation
substitute for Windows runtime checks. Basic Windows DX11 validation is below.
See [validation notes](docs/Validation.md) for exact scope and reproduction.

### Windows DX11 follow-up (2026-10-08)

The DX11-only viewer and DLL now compile with MSVC 19.39 on Windows x64.
`ztmz_dashboard_viewer.exe --smoke-test` also passed: it creates a hardware device
and submits/presents three ImGui frames, without a network connection or visible
window. Controlled `dx11_hook_early` and `dx11_hook_late` tests also pass using
the actual hook code in our own host. GPU readback verifies drawing and stopped
pass-through; both tests check API detection, selected host state restoration,
resize and three stop/start cycles. Test transport is a deterministic fixture.
Game injection, other GPU APIs, fullscreen/device-loss behavior and desktop-host
integration remain pending. See the [desktop integration plan](../../Docs/InGameDashboardIntegration.md).
