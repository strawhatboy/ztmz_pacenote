# Liquid Glass dashboard

## Glass Compact

`dashboards/glass_compact` is a separate **Glass Compact / 极简玻璃** option,
inspired by the 3.0 release thumbnail. Its 340-by-124 layout contains italic
gear/speed numerals, a slanted divider and a rising RPM bar with 18 segments.
It defaults to the bottom right, disabled, with a height of 12% of the canvas.
It shares the existing glass controls and translucent standalone/browser fallback.
The slanted, rounded outline uses the existing geometry API. An optional final
`slant` argument on `DrawGlassPanel` makes the native glass mask follow that outline
on DX9, DX10, DX11, DX12 and Vulkan. Slant defaults to zero for existing dashboards
and is bounded to 40% of panel width or 80% of height, whichever is smaller.
Browser/command-player and native translucent fallbacks use the same shear.
The CLR command capture/proxy also forwards the optional slant, preserving the
old serialized command when it is zero or omitted.
Capture bounds remain rectangular; foreground text and lamps stay sharp.
Compact defaults to tint opacity 0.32, refraction 8 and diffusion 4.
No additional telemetry fields are required. The full Liquid Glass dashboard
retains its controls, damage and powertrain information.

The RPM bar fills from zero to the game's maximum RPM, with a smooth near-white,
green, yellow and red gradient along the segments. Its scale is independent of
the shift-light window. The entire bar flashes
red at the provider's shift point, even when that is below maximum RPM; when the
provider range is unavailable, maximum RPM times the configured red-zone fraction
sets the warning threshold. Below that threshold it returns to the actual RPM fill.
Invalid RPM or maximum RPM clears the bar.
Unknown gear/speed uses a dash instead of retaining an earlier reading.
Names/descriptions are localized in English and Simplified Chinese; the face
contains only gear notation, numbers and km/h.

The bundled preview is rendered from the actual browser Lua dashboard using
synthetic telemetry and the translucent fallback. It is not a live-game or
refraction screenshot. Nine focused CLR/Lua checks cover shift timing, invalid
readings, neutral/reverse and fallback behavior; the browser smoke check includes
this dashboard. Native pixel tests cover the slanted mask and unchanged rectangular
panels on all five graphics backends in x86 and x64; the complete Lua package was
also rendered in the DX11 offscreen harness. The dashboard content globs and installer recursion include its
script, metadata, settings, preview and language resources.

## Full Liquid Glass dashboard

`dashboards/liquid_glass` is a separate dashboard with speed, gear, RPM shift
lights, throttle/brake/clutch, steering, handbrake, optional hybrid and induction
pressure, engine temperatures, local-best delta, and supported damage indicators.
It is selectable in both desktop hosts and browser/native dashboard clients.
Enable **Liquid Glass / 液态玻璃** in the HUD page; it is disabled by default so
installing the new package does not cover an existing dashboard.

The shift-light banks use the shared provider-aware blank/green/yellow/red/flashing
red states. Handbrake is grey when released and red when applied; an unknown
handbrake reading is hidden. Hybrid charge and boost-cycle progress are separate
rings. Pressure retains its reference: gauge boost, absolute intake pressure, or
pressure with an unspecified reference. Zero remains a valid measurement.

The layout measures available telemetry before drawing. The 370-unit core contains
speed, gear, shift lights and controls. Hybrid adds 96 units and pressure adds 110;
missing or disabled columns collapse completely. Temperatures and local-best delta
share compact 28-unit rows (up to three readings each), only when readings or
explicit warning flags exist. The damage strip disappears
when no supported components exist. Instrument scale is stable across these changes;
left/center/right and top/center/bottom anchoring follows the complete visible panel.
The core is 146 units high; the optional damage strip is 36 units with a 4-unit
gap. With one readings row and damage, total height is 214 units (previously 272),
about 21% shorter at the same instrument scale. Speed and gear font sizes remain
unchanged; tighter padding and row spacing provide the reduction.

Dashboard settings include **Enable glass effect**, **Glass tint opacity** (0–1),
**Glass refraction** (0–24) and **Glass diffusion** (0–8). Distances are scaled with
the dashboard. Turning glass off emits ordinary rounded rectangles for every panel,
so this dashboard requests no backdrop copy or glass shader. Another enabled glass
dashboard could still request capture. Zero diffusion alone does not disable glass.
These controls affect this dashboard only; arbitrary transparent drawing commands
in other dashboards are not converted to glass.

Default refraction is **8** and diffusion is **4**, with tint opacity **0.44**.
Compared with the previous 12/2 settings, this gives gentler edge lensing and a
softer backdrop while keeping instruments sharp. These are visually tuned values
for our renderer, inspired by Apple's Regular Liquid Glass material; they are not
Apple's numeric defaults. Apple's material adapts tint, shadows and other layers
to its surroundings, which two fixed sliders cannot reproduce. See Apple's
[Meet Liquid Glass](https://developer.apple.com/videos/play/wwdc2025/219/).
Saved dashboard preferences still take precedence; existing users can set
refraction to 8 and diffusion to 4 to try this preset.

The footer includes engine, radiator/cooling, gearbox and individual tyres only
when the game reports that component. Healthy icons are muted, damage is amber,
and severe damage is red. Missing telemetry is never depicted as healthy.
Coolant temperature is separate from radiator damage; its warning color uses a
reported overtemperature warning rather than a universal temperature threshold.
Temperature and pressure snapshots are optional, like the existing hybrid data.

## Language

Dashboard labels follow the language selected in WPF or Avalonia, including changes
while rendering. The browser/native scene configuration carries `CultureName` and
an effective `Translations` snapshot (including the host's English fallback).
Only dashboard/overlay keys and the game-state caption are included. The snapshot
is reused until the host replaces its effective culture dictionary; translations
travel with scene changes, not telemetry deltas. Scene changes recreate cached
browser/native Lua labels. Standalone Liquid Glass and modern WRC dashboards refresh
their cached labels on a culture change without recreating brushes or fonts.

Liquid Glass also localizes pedal, tyre-position and hybrid-map captions. Units
(`km/h`, `bar`, `°C`, RPM) and the handbrake symbol remain standard notation.
Missing Liquid Glass labels retain their readable English defaults.

## Rendering contract

Lua uses `DrawGlassPanel(brush, left, top, right, bottom, radius, refraction, blur)`.
The command carries its position, size, corner radius, ARGB tint, and refraction
and diffusion distances in dashboard pixels. Parameters are bounded by the
bridges/renderers. Foreground text, gauges and icons are ordinary sharp drawing
commands after the glass panel.

The native renderers copy the union of visible glass panels, padded for
refraction and diffusion sampling, into a cached full-size GPU texture before any
HUD commands. Single-sample targets use a regional copy; multisampled targets still
require a full-target resolve. A rounded-panel distance field shapes edge
refraction, with a small background diffusion kernel and directional rim lighting. The original scene is
sampled in the shader; no per-frame CPU screenshot or readback is needed. Capture
is skipped for scenes without glass. Resizing and device invalidation release the
cached texture. Unsupported target formats or unavailable shader resources use the
same translucent fallback as external overlays.

DX9, DX10, DX11, DX12 and Vulkan have native glass implementations, independent
of whether the UI host is WPF or Avalonia. Backend requirements are:

| Backend | Capture and limits |
|---|---|
| DX9 | Shader Model 3, A8R8G8B8/X8R8G8B8 render targets. Regional `StretchRect`, or full resolve for MSAA. Failed copies use the translucent fallback. Default-pool resources are released before Reset. |
| DX10 / DX11 | Regional GPU copy, full resolve for supported MSAA targets. Common RGBA/BGRA8, RGB10A2 and RGBA16F formats; unsupported formats/subresources fall back. |
| DX12 | Single-sample presentation targets; the same common color formats. Each frame slot has its own snapshot and descriptor, reused only after its submission fence completes. |
| Vulkan | Requires swapchain `TRANSFER_SRC` usage and a supported sampled color format. The creation hook requests transfer usage only when the surface supports it. An existing swapchain without that usage falls back until it is recreated. |

Browser canvases and external desktop overlays use the translucent fallback:
transparent windows do not provide the
game's pixels to a shader. A fallback preview is therefore not evidence of scene
refraction. The APIs named above are the APIs intercepted by the bridge; a game's
graphics menu can name a different underlying API when a translation layer is used.

Glass panels sample the scene before the HUD, not earlier HUD panels. Overlapping
glass therefore does not refract another dashboard. The GPU copy/resolve and panel
shader add cost. Zero diffusion uses one texture sample per pixel instead of five; turning the effect off avoids both capture and
the glass shader. Hardware frame-time measurement in a live game remains separate
from offscreen correctness testing. The effect is intentionally confined
to panel backgrounds to preserve instrument readability.

See [DX11 measurements and reproduction steps](../src/ZTMZ.PacenoteTool.NativeDashboard/docs/liquid-glass-performance.md)
for the offscreen hardware benchmark and its limits. Those measurements apply
only to DX11; they are not performance estimates for the other backends.

## References

The implementation uses our existing drawing protocol and native graphics backend,
not a React or Android runtime dependency. Reference implementations inspected for
the effect and background/foreground separation:

- [rdev/liquid-glass-react](https://github.com/rdev/liquid-glass-react/blob/master/src/index.tsx)
- [ybouane/liquidglass](https://github.com/ybouane/liquidglass#how-it-works)

The new dashboard and shader are repository-native code; no source or image assets
from those libraries are bundled.

## Validation

The `dx11_glass` CTest uses WARP and the D3D11 debug layer to compare refraction
against an unwarped scene, require unchanged foreground pixels, and check clipping,
scene capture ordering, multisampling, state restoration, resize, invalidation and
fallback. It also accepts `--package input.json output.bmp` for real Lua dashboard
previews with synthetic telemetry; the native Lua canvas is 1920 by 1080.

`dx12_glass` uses WARP and the D3D12 debug layer. It compares rendered pixels for
refraction, diffusion, sharp foreground, clipping, pre-HUD capture, zero-capture
frames and fallback, and exercises resize/invalidation. A deliberately blocked
test queue lets two frames remain in flight; their distinct backgrounds must stay
independent, and the backend must skip a busy frame slot instead of resetting its
allocator. This queue blocking exists only in the test.

`dx9_glass` and `dx10_glass` cover pixel output, regional versus full capture,
state restoration, multisample resolve and device-object recreation; DX9 also
performs a real device Reset. `vulkan_glass` covers the transfer-usage fallback and
GPU capture/shader path. Vulkan shader bytecode is checked in with its source hash,
so ordinary builds do not require an installed Vulkan SDK or shader compiler.

On 2026-10-17, all five glass tests passed in both Release x64 and x86 builds.
DX9 and Vulkan used the Radeon RX 7900 XT; DX10/11/12 used WARP. DX12's debug
layer reported no errors. The Khronos validation layer was unavailable, so Vulkan
results establish actual pixel and lifecycle behavior without claiming validation
layer coverage. These are standalone test processes, not live-game verification.

`build/verify_glass_bridge.js` verifies bridge command validation and browser
fallbacks. The bundled browser check includes this dashboard. Portable tests cover
real CLR snapshot access, clearing missing readings, pressure references, and
grey/red/unknown handbrake states, adaptive layout bounds, tint clamping and the glass-off path. Provider and wire tests cover validity and
staleness separately. Both WPF and Avalonia Debug builds pass.
