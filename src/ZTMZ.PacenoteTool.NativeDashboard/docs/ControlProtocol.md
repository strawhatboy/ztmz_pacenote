# Windows overlay control protocol, version 1

The pinned DLL owns `\\.\pipe\ztmz-dashboard-<pid>` for the process lifetime,
including while rendering is stopped. The pipe is created on first startup,
before hook installation. It rejects remote clients, grants access only to the
process user, and checks that the client belongs to the same Windows session.
First-instance creation fails rather than sharing an existing endpoint. This
authenticates the Windows user/session, not the client executable; it is not a
security boundary against other programs already running as that user.

One connection carries one UTF-8 JSON request and response as Windows pipe
messages, each at most 4096 bytes. Oversized requests are disconnected. Clients
must select message read mode, read the response, then send a one-byte receipt
acknowledgement before closing. Server reads/writes/acknowledgement waits have a
two-second deadline; command execution (including renderer/transport shutdown)
is separate from those I/O deadlines. Clients must bound their own operations,
retry busy connections within a deadline, and never run them on the UI thread.
The server handles commands sequentially; an idle client cannot hold it forever.

Start with:

```json
{"version":1,"command":"status"}
```

Responses include `version`, `pid`, `instance`, `ok`, `started`, `port`,
`requestedApi`, and `selectedApi`. `availableApis` lists compiled adapters,
`candidateApis` lists installed hook candidates, and `hookWarnings` reports
individual installation failures in auto mode. Failures add `error`. `instance` combines PID
and process creation FILETIME, preventing commands for an old PID incarnation.
The shared Windows `InGameOverlayControlClient` verifies the pipe server PID and
process creation time before sending commands, and validates instance identity
again in replies. It supports asynchronous cancellation, a total request deadline,
bounded response reads and per-client serialization. A timed-out mutation has an
unknown result; query status before retrying. The client lives in Platform.Windows
for both UI hosts. Their shared Windows session loads the versioned x64 or x86 module
or reconnects to an existing one. Responses also require `moduleVersion: 1`.

The desktop host remains x64. For WOW64 games it starts the native x86 helper
beside the x86 DLL with PID and creation FILETIME. The helper independently checks
architecture and process identity, and only loads its adjacent overlay DLL.
No .NET x86 runtime is required. Both architectures use the same control protocol.

Use the returned instance for mutations:

```json
{"version":1,"command":"start","instance":"<from status>","port":8080,"api":"auto"}
{"version":1,"command":"stop","instance":"<from status>"}
```

The endpoint is always IPv4 loopback; arbitrary hosts/URLs are not accepted.
Start accepts optional unsigned `targetWindow` (numeric HWND, zero for legacy
automatic selection). Nonzero handles must belong to this DLL's process. The
DXGI hook only selects a swap chain for that HWND. Desktop sessions wait for a
window and pass its handle, requesting `auto`. DX9 and Vulkan also require the
captured target window to match before drawing. DLL startup does not detect an
API from module names; the presentation/device events select the renderer.
Port must be an integer from 1 through 65535. API names use the existing renderer
catalog. Repeating start with the same settings succeeds without restarting.
Changing the port or API while started is rejected: stop, then start with new settings.
Repeating start with the same port/API and a new validated `targetWindow` updates
the target without reconnecting telemetry. Renderer selection and frame acknowledgement
are reset. Hosts must send this update when a startup window becomes the game window;
transient zero handles are ignored by the desktop host.
Stopping retains the pipe, pinned DLL and pass-through hooks, allowing a new
client connection after ZTMZ restarts. Bootstrap opens control only; rendering and
networking require an explicit Start command. Requests do not mutate environment variables.

An optional native loader may call `BOOL WINAPI ZtmzDashboardPrepare(UINT version)`
with version 1 outside DllMain/loader lock, before swapchain creation. This installs
DX12 factory queue capture only, without networking, rendering or API selection.
It is idempotent, rejects unknown ABI versions and returns false when DX12 was not
compiled or capture failed. Status reports `dx12CapturePrepared`; this means the
explicit preparation call succeeded, not that an existing game's queue was found.
Stop preserves capture and swapchain-owned queue associations. No desktop host
currently invokes this optional entry point; it does not itself solve late attachment.

`started` means the worker/hooks were enabled; `selectedApi` means a presentation
path claimed the router. Neither proves successful ongoing drawing. All adapters
reports `submittedFrames`, `window` (numeric HWND), `lastFrameAgeMs` (-1 when none),
and `renderError`. Acknowledgement is recorded only after the backend render call
submits work, not when its presenter silently returns early. It does not prove
GPU completion, scan-out, successful presentation or telemetry arrival.

`queueSource` describes the DX12 association used for an acknowledged frame:
`dxgi-creation-or-resize` or `calibrated-private-layout`; it is empty for other
APIs and after Stop. The latter is a runtime-calibrated compatibility fallback,
not a public DXGI contract. Missing or inconsistent matches do not activate it.

The managed snapshot's `HasRecentSubmission` requires a started concrete API worker, a
nonzero window/frame count, no renderer error and a frame age below one second.
It also accounts for time elapsed since receiving the snapshot, so cached status
cannot remain current indefinitely. Stop/restart resets acknowledgement; renderer
failures/device removal invalidate it. Hosts must additionally check their target
window and data/session readiness before reporting Active.
Game eligibility (including the EA WRC exclusion) must be enforced before loading
the DLL, not after it auto-bootstraps.

The controlled DX11 tests exercise a separate client process, reconnects,
status, idempotent start, stop/start with GPU pixel assertions, stale instances,
invalid versions/ports, malformed and oversized messages. Unauthorized-user
and cross-session rejection are implemented but not exercised by these tests.

## Managed/native interoperability probe

From the repository root, after building the native Release targets:

```powershell
dotnet build src/ZTMZ.PacenoteTool.NativeDashboard/tests/ManagedControlProbe/ManagedControlProbe.csproj -c Release
$nativeRoot = (Resolve-Path build/native-dashboard/hook-test).Path
$probe = (Resolve-Path src/ZTMZ.PacenoteTool.NativeDashboard/tests/ManagedControlProbe/bin/Release/net10.0-windows/ManagedControlProbe.exe).Path
& "$nativeRoot/dx11_hook_host.exe" "$nativeRoot/ztmz_dashboard_overlay_test.dll" late $probe
& "$nativeRoot/dx11_hook_host.exe" "$nativeRoot/ztmz_dashboard_overlay_test.dll" early $probe
```

This compiles the production client source into a small independent test program.
It queries actual native state, stops/restarts the hook, checks frame reset and
snapshot expiry, preserves caller cancellation and times out an absent endpoint.
The parent resumes GPU pixel checks after the managed client finishes.
