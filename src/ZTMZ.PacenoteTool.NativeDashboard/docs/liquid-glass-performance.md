# DX11 liquid-glass performance

## Work avoided

- A frame without `drawGlassPanel` never calls the backend's glass preparation. Its submission issues zero scene copies/resolves. Turning glass off on an already-used backend has the same behavior.
- Zero blur uses one background texture sample per pixel. Positive blur uses the existing five-tap filter. The condition is uniform for each panel.
- Non-MSAA targets use one `CopySubresourceRegion` for the union of visible glass panels, clipped to the viewport and expanded by refraction + blur + two pixels for filtering/antialiasing. Coordinates remain relative to the full-size snapshot, so sampling and screen-edge clamping are unchanged.
- Capture happens after CPU draw-list construction but before any HUD draw submission. Earlier HUD commands cannot enter the background snapshot.
- Entirely offscreen/clipped panels issue no capture. MSAA retains a full-surface `ResolveSubresource`; the basic DX11 resolve API cannot resolve a rectangle.
- The snapshot allocation remains full-size and is reused until target dimensions/format change or device objects are invalidated. This saves copy bandwidth, not reserved texture memory. Distant panels can enlarge the union; there is no per-panel texture allocation or CPU image readback.

## Offscreen hardware measurement — 2026-10-17

Measured on **AMD Radeon RX 7900 XT**, Release x64, debug layer disabled, RGBA8 single-sample offscreen targets. The real compact liquid-glass Lua package was evaluated with synthetic telemetry and all optional readouts. Dashboard size was 0.6, refraction setting 12, tint opacity 0.44. The benchmark replaces glass with the same tinted rounded fill for its glass-off control and forces blur to zero or four logical pixels for the other variants.

Each case has 40 warmup frames and 160 measured frames. D3D11 timestamp queries bracket the complete `RenderSession` HUD submission, including scene capture and foreground rendering. Results reject disjoint GPU clock intervals. The backdrop clear is outside the timed interval. Lua execution, asynchronous font preparation, initial shader/resource creation, Present, vsync, and game rendering are excluded. GPU queries exist only in the benchmark, not the production renderer.

| Output | Glass off median / p95 GPU ms | Zero blur median / p95 GPU ms | Blur 4 median / p95 GPU ms |
|---|---:|---:|---:|
| 1920×1080 | 0.00624 / 0.00668 | 0.01748 / 0.02120 | 0.02044 / 0.04952 |
| 2560×1440 | 0.00880 / 0.00968 | 0.02864 / 0.03064 | 0.03248 / 0.04092 |
| 3840×2160 | 0.01896 / 0.01956 | 0.05616 / 0.05968 | 0.06428 / 0.07244 |

At 1080p, zero blur copied **1434×710 = 1,018,140 pixels**, versus 2,073,600 for a full surface (50.9% fewer copied pixels). Blur 4 expanded the copy to 1442×718. Glass off recorded zero captures at every resolution. The reusable snapshot allocation is still 1920×1080.

CPU submission medians ranged from 0.436 to 0.883 ms across these cases; p95 ranged from 0.969 to 1.590 ms. CPU scheduling and background work varied during this one run, so these values do not establish a CPU speedup. GPU numbers are also a single-machine, isolated workload observation: they are **not live-game FPS, frame-time guarantees, or a pre/post benchmark of the old implementation**. A saturated GPU, HDR target, MSAA resolve, different panel coverage, driver, or adapter can behave differently.

Package SHA-256 for this run: `1b2236c9af56941a88807dd938f3ae24a3da99f6a50dffe1dcd5a90a6f6b0a1c`.

## Reproduce

Build `dx11_glass_benchmark` with `BUILD_TESTING=ON`, then run:

```text
dx11_glass_benchmark.exe package.json report.json
```

The package uses the native Lua package format with optional `telemetry`, `context`, and `config` objects. Native Lua produces a 1920×1080 logical scene; the benchmark scales that scene to the three output sizes. It explicitly requests hardware DX11 and exits with code 77 if unavailable; it never substitutes WARP performance figures. Output JSON includes adapter identity, GPU median/p95, CPU submit median/p95, and issued capture dimensions/counts. It does not inject into or launch a game.

## Correctness coverage

`backend_contract` checks that ordinary frames skip preparation and switching glass on/off does not retain it. `dx11_glass` uses WARP with the debug layer when available and checks:

- Regional and full-surface reference results match pixel-for-pixel, including maximum refraction/blur, changing backgrounds, edge clipping, and multiple panels.
- No-glass and fully offscreen frames issue zero captures, including after active glass frames.
- Foreground pixels remain exact and the background snapshot excludes prior HUD commands.
- Blur, clip rectangles, MSAA full resolve, unsupported-format fallback, resize, resource invalidation, and D3D state restoration remain valid.

No live injection or .NET build is required for these tests or measurements.
