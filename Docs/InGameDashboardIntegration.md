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

Graphics API selection defaults to automatic. The renderer already selects from
observed presentation/device types. Desktop integration must additionally identify
the game window/swap chain, rather than letting a launcher or auxiliary surface
claim rendering. A loaded graphics DLL alone is not sufficient evidence.

## Implemented foundation (2026-10-08)

- Both dashboard pages offer the same persisted display-method preference and
  localized fallback explanation. The default remains the separate window.
- Windows session state exposes the shared policy; the Avalonia presentation
  uses the same policy on both platforms.
- The experimental renderer is **not yet connected to either desktop host**.
  `GetDecision` deliberately reports integration pending on Windows and platform
  unavailable elsewhere. Merely copying an overlay DLL does not enable injection.
- Compatibility tests cover WRC exclusion, unsupported platforms, absent games,
  unknown preferences, and reevaluation when switching games.
- MSVC 19.39 compiled the Windows x64 DX11 viewer and overlay DLL successfully.
- `ztmz_dashboard_viewer.exe --smoke-test` created a hardware DX11 device and
  submitted/presented three ImGui frames without network access or a visible
  window. This verifies basic device rendering, not dashboard fidelity or hooks.

Native build output belongs in `build/native-dashboard/` and is ignored by Git.
The normal managed build and installer do not include these experimental binaries.

## Remaining integration, in order

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

## Acceptance scenarios

- ZTMZ before game; ZTMZ started while already on a stage.
- ZTMZ restarted with the game and injected module still running.
- Switch backends while paused/racing; toggle HUD and Web independently.
- Select EA WRC after another game: no injection attempt, clear fallback reason.
- Two games running, selected game exits, session falls back to the other game.
- Non-default transport port, process exit during attachment, stale IPC reply.
- Non-Latin labels, pacenote icons, frame timing and existing overlay coexistence.

Only actual rendering acknowledgement may produce an in-game Active status.
