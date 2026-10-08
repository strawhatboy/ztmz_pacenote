# In-game dashboard integration

## Product contract

WPF and Avalonia share `OverlayBackendSettings` and `OverlayBackendPolicy` in Base.
The persisted `OverlayDisplayBackend` is a preference: 0 is the existing separate
window, 1 is experimental in-game rendering. Missing or invalid values use 0.
Changing games must reevaluate the policy without overwriting the preference.
UI must distinguish the requested backend from the effective backend and explain
fallbacks. Existing dashboard layout and content settings stay shared; Web output
remains independently selectable.

EA WRC (Steam application 1849250, provider executable WRC) is excluded before any
injection or hook setup. This is a product compatibility decision, not a claim
that the current DLL has been tested against its anti-cheat. Enabling the global
experimental preference does not override this exclusion.

Standalone graphics API selection defaults to automatic; desktop attachment currently
requests the validated DX11 adapter. The renderer already selects from
observed presentation/device types. Desktop integration must additionally identify
the game window/swap chain, rather than letting a launcher or auxiliary surface
claim rendering. A loaded graphics DLL alone is not sufficient evidence.

## Implemented foundation (2026-10-08)

- Both dashboard pages offer the same persisted display-method preference and
  localized fallback explanation. The default remains the separate window.
- Windows session state exposes the shared policy; the Avalonia presentation
  uses the same policy on both platforms.
- Both Windows hosts use the shared session controller and x64 loader to attach
  the versioned DLL or reconnect to an existing one. Missing native bundles retain
  the window backend and integration-pending status. EA WRC is excluded before load.
- Backend transitions are serialized. Old drawing must acknowledge Stop before
  the replacement starts; stale attachment generations are discarded. Failures
  remain visible rather than starting an uncertain second renderer.
- The in-game session owns a loopback-only runtime on an OS-assigned port. It
  reuses live dashboard settings, Overlay routing and the existing pacenote/replay
  context. The public Web toggle cannot stop it. Empty scenes clear hidden HUDs.
- Attachment waits for a window and passes its HWND to the DXGI hook. Active
  requires recent matching-window submissions and a connected telemetry client.
  Window recreation currently requires reattachment.
- Compatibility tests cover WRC exclusion, unsupported platforms, absent games,
  unknown preferences, and reevaluation when switching games.
- MSVC 19.39 compiled the Windows x64 DX11 viewer and overlay DLL successfully.
- `ztmz_dashboard_viewer.exe --smoke-test` created a hardware DX11 device and
  submitted/presented three ImGui frames without network access or a visible
  window. This verifies basic device rendering, not dashboard fidelity or hooks.

Native build output belongs in `build/native-dashboard/` and is ignored by Git.
The normal managed build and installer do not include these experimental binaries.

## Integration status and remaining validation

1. Controlled DX11 hook baseline passed: early/late DLL loading, automatic API
   selection, GPU pixel readback, render-target/viewport/topology restoration,
   resize and three stop/start cycles. The test uses production hooks with a
   fixture transport, not game injection. Still validate main-window selection,
   flip-model presentation, concurrent stop, device loss, Alt-Tab and fullscreen
   before claiming game compatibility. Next implementation milestone is the
   control/status protocol below, followed by host integration and DR2 testing.
2. Replace environment-only bootstrap configuration with a versioned local
   control/status protocol: configure endpoint and API, start, stop, query actual
   API/window/error. Authenticate the local host, bound messages, and include
   process/session identity. Support reconnect after ZTMZ restarts while the
   pinned DLL remains loaded. Do not remotely invoke exports using guessed
   addresses or assume a successful load means rendering started.
   The first native control-pipe implementation now supports bounded messages,
   same-user/session access, instance validation, reconnect, start/stop and API
   selection status. Shared managed control and DX11 window/submitted-frame/error
   feedback are now implemented; controlled interoperability checks cover native
   and managed clients. Shared host connection now supports preloaded DX11 modules;
   the x64 loader is now connected; full real-game acceptance remains pending. See
   [protocol details](../src/ZTMZ.PacenoteTool.NativeDashboard/docs/ControlProtocol.md).
3. Introduce an overlay backend interface beneath the shared session controller.
   Adapt the existing window backend first, preserving its startup and stage
   synchronization. Add the Windows in-game backend only after the control
   protocol and policy gate are enforced at the attachment boundary.
4. Reuse the shared WebDashboard runtime as a loopback-only native transport with
   Overlay routing, independent of the public Web toggle and Web output selection.
   Reference-count or otherwise isolate its lifetime so disabling Web output does
   not stop in-game dashboards. Reuse telemetry/pacenote/replay context generation.
5. Serialize backend changes and cancel stale attachments. Stop old drawing before
   enabling the replacement. Report failure or an explicit separate-window fallback;
   keep requested and effective state distinct. A stopped hook DLL remains pinned
   until the game exits: switching backend is not DLL unloading.
6. Add advanced API override only when the native control protocol accepts it.
   DX12 currently requires attachment before swap-chain creation; Vulkan requires
   attachment before graphics-object creation. Return a restart-required status
   when those requirements cannot be met. Do not promise universal late attachment.
7. Windows font resolution now uses installed fonts plus bundled WRC fonts, with
   CJK fallback and shared Lua/render glyph metrics. Continue validating image/layout
   fidelity and implement non-Windows font resolution, then extend
   validation to DX9/10/12 and Vulkan. Package only validated binaries and expose
   per-game capabilities. Vulkan support in the current Windows DLL is not a Linux
   layer implementation; Linux/Proton attachment is a separate platform backend.

WPF and Avalonia should only bind shared commands/state. Do not copy injection,
compatibility, session coordination, or dashboard transport logic into either UI.

The lifetime/transport/serialization work in items 3–5 is now connected in the
shared Windows controller and `InGameOverlaySession`. It has not yet been validated
as one complete game session: automated tests cover native/.NET control and GPU
pixels separately from private transport routing, dynamic ports and empty scenes.
The policy-gated x64 loader now checks PE architecture, file version/product identity,
target architecture and process creation time. It resolves the actual system loader
module and its export offset in the target rather than reusing a local address.
It does not auto-elevate. A remote-load timeout is ambiguous: no thread termination
or premature freeing of its argument is attempted; restart the game before retrying.
Versioned DLL loading, inert bootstrap, explicit DX11 frame submission and Stop
passed in a self-owned controlled process using the production DLL. DR2 is next.

## DR2 E2E test

Windows WPF and Avalonia builds now compile and stage the production DX11 DLL
automatically through the shared `build/NativeDashboard.Development.targets`.
This also applies to `build.bat`, `build_avalonia.bat` and direct `dotnet build`.
Visual Studio 2022 C++ tools, a Windows SDK and CMake are required. The first build
downloads hash-pinned native dependencies; subsequent builds reuse the CMake cache
and compile only changed sources. Both hosts share a serialized build tree at
`build/native-dashboard/production` (override `NativeDashboardBuildDirectory`).
The DLL uses Release with a static C runtime even when the managed host is Debug.
Only the production DX11 DLL is built, not the viewer or test binaries.

`dotnet publish` (including `--no-build`) ensures the native bundle is built and
includes the DLL under `native-dashboard/win-x64`, outside any single-file executable.
The injected DLL resolves the existing `<host>/fonts` relative to its own module
location, not the game executable. No duplicate fonts are staged in the native
subdirectory. Standalone CMake viewer/test outputs still use their adjacent fonts.
Native compilation failures fail the build/publish.
The WPF Inno installer includes that directory and rejects missing DLL/font inputs.
Non-Windows and non-x64 target builds skip this Windows-only integration.

For a manually built native test bundle, the staging helper remains available:

```powershell
./build/stage_native_dashboard.ps1 -NativeOutputDirectory ./build/native-dashboard/hook-test -HostOutputDirectory ./src/ZTMZ.PacenoteTool.Avalonia/bin/Release/net10.0-windows
```

The same command accepts the WpfGUI output directory. It copies the production
DLL under `native-dashboard/win-x64` and validates the existing shared host fonts;
missing bundles keep window mode.
Restart DR2 first if any earlier overlay DLL was loaded, then run only one ZTMZ host.
Enable an Overlay-routed dashboard and choose experimental in-game rendering.
Check late startup on a stage, both backend switch directions, public Web disabled,
HUD disabled/re-enabled, and ZTMZ restart while DR2 stays running. The host must
report Active only after the target HWND submits frames and telemetry is connected.
Check icon/text/replay content and game responsiveness; report the log if attachment
fails.

## Acceptance scenarios

- ZTMZ before game; ZTMZ started while already on a stage.
- ZTMZ restarted with the game and injected module still running.
- Switch backends while paused/racing; toggle HUD and Web independently.
- Select EA WRC after another game: no injection attempt, clear fallback reason.
- Two games running, selected game exits, session falls back to the other game.
- Non-default transport port, process exit during attachment, stale IPC reply.
- Non-Latin labels, pacenote icons, frame timing and existing overlay coexistence.

Only actual rendering acknowledgement may produce an in-game Active status.
