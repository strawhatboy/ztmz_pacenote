# SteamOS and Steam Deck Support Roadmap

## Status

Implementation in progress. The current Windows WPF application remains the
supported product while platform-neutral services and a Linux host are
developed incrementally. SteamOS support does not require a one-time rewrite
of the working Windows application.

The first native bootstrap was verified on Arch Linux on August 18, 2026:

- .NET 10 SDK `10.0.111` built `Base`, `Core`, and the Linux host natively;
- the Linux host resolved and wrote its XDG-compatible data root;
- nine platform-boundary tests passed on both Windows and Arch Linux;
- the existing 242-test Windows suite still passed;
- a permission-minimal Flatpak development package built, installed, and launched successfully;
- the Flatpak loaded 349 official pacenote tokens and three Codemasters UDP game providers;
- a DiRT Rally 2.0 UDP listener bound successfully inside the Flatpak sandbox;
- a host-side simulator sent 20 valid Codemasters frames across the Flatpak boundary,
  all of which were received and decoded with the expected speed, gear, RPM, and distance;
- Flathub exposes Freedesktop `25.08` and the matching `dotnet10` SDK extension.
- a self-contained x86_64 AppDir and AppImage were built and launched on Arch Linux;
- both FUSE mounting and the AppImage extract-and-run fallback loaded 349 tokens,
  three Codemasters providers, Web Dashboard assets, and the PortAudio backend;
- the AppImage's initial `/proc` scan detected a Proton-style DiRT Rally process
  that was already running before ZTMZ started.
- the Linux host now activates the same complete `ZTMZPacenoteTool` game session
  as Windows, covering profile and co-driver selection, pacenote processing,
  PortAudio playback, and Web Dashboard context/telemetry publication;
- the AppImage provisions its default co-driver and default profile on a fresh
  XDG data root instead of starting with an audio-incomplete empty profile;
- a DR1-tagged process on the Arch VM initialized that full session with the
  default profile, 796-file co-driver package, PortAudio output, and dashboard
  bridge, then released it cleanly when the process exited.

This is an infrastructure milestone, not a playable Steam Deck release. The
Linux host now has a PortAudio output implementation and loads co-driver
packages through a cross-platform decoder boundary. WAV, AIFF, MP3, Ogg, FLAC,
and Opus use libsndfile; WMA, AAC, MP4, and M4A fall back to FFmpeg. Audible
playback inside the rebuilt Flatpak remains to be verified on the Arch VM. The
native overlay still reports unavailable rather than invoking Win32 code.
The installed native Linux DR1 build is recognized through Steam/Feral paths,
but a real rendered stage cannot yet be used for end-to-end telemetry and
audible playback validation because the VMware guest exposes no Vulkan device.

## Product Goal

The first SteamOS release should provide the useful core of ZTMZ on a Steam
Deck:

- receive supported game telemetry and live pacenotes;
- play co-driver audio through the selected Linux output device;
- expose the control UI and Web Dashboard locally and over the LAN;
- load the existing profiles, co-driver packages, dashboards, icons, and
  localization data;
- run without Windows registry, WPF, WebView2, Direct2D, or Win32 window APIs.

Feature parity with the Windows native overlay, ACR injection, RBR process
memory, SteamVR overlay, and replay capture is not required for the first
Linux release. Each is a separate platform integration.

## Current Platform Boundaries

| Area | Current Windows dependency | Cross-platform direction |
| --- | --- | --- |
| Main UI | WPF, WPF-UI, WinForms, `net10.0-windows` | Avalonia 12 desktop host with FluentAvalonia; browser remains the dashboard renderer |
| Audio output | NAudio WASAPI | WASAPI remains on Windows; PortAudio is implemented for Linux |
| Audio decoding and resampling | NAudio `AudioFileReader` and Media Foundation | `IAudioFileDecoder`, libsndfile, FFmpeg fallback, and managed WDL resampling on Linux |
| Native overlay | GameOverlay.Net, SharpDX, Direct2D, `user32.dll`, HWND attachment | Keep the current external overlay on Windows; research an opt-in Vulkan Layer for supported Proton games separately |
| Web Dashboard | ASP.NET Core, WebSocket, browser JavaScript | Extracted into the platform-neutral `WebDashboard` project and hosted by the Linux runtime |
| Script editor | Monaco hosted by WPF WebView2 | Shared Monaco protocol and catalog with Avalonia `NativeWebView`; WPE WebKit or WebKitGTK renders the loopback-hosted assets on Linux |
| Game discovery and IPC | Windows process/window APIs, registry, named maps, named pipes, injection | Shared activity monitor implemented; native Linux uses `/proc`, while Flatpak and Windows-only IPC require Proton relays |
| Replay capture | WASAPI loopback and Windows window capture | Add PipeWire/FFmpeg capture only after the core Linux release |
| Packaging and updates | Inno Setup and Windows executable updates | AppImage is the primary SteamOS package; retain Flatpak as a later store channel |

WPF itself only runs on Windows, and the current WebView2 desktop runtime does
not provide a Linux host. Porting only the audio backend therefore cannot make
the current WPF executable a native SteamOS application.

References:

- [Microsoft WPF overview](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/)
- [Microsoft WebView2 platform overview](https://learn.microsoft.com/en-us/microsoft-edge/webview2/)
- [Avalonia supported platforms](https://v11.docs.avaloniaui.net/docs/overview/supported-platforms/)
- [Avalonia WebView documentation](https://docs.avaloniaui.net/docs/app-development/embedding-web-content)
- [PortAudio API overview](https://portaudio.com/docs/v19-doxydocs/api_overview.html)
- [Valve Gamescope](https://github.com/ValveSoftware/gamescope)

## Target Architecture

```text
Windows WPF host                         SteamOS/Linux host
       |                                       |
Windows platform services              Linux platform services
  - WASAPI output                        - PortAudio/ALSA output
  - WebView2                              - system browser/web UI
  - Win32 process/window access           - Linux process and path access
  - Direct2D native overlay               - no native overlay initially
       |                                       |
       +---------- platform contracts --------+
                           |
                    platform-neutral core
              pacenotes, profiles, replay data,
              game state, audio queue, dashboards
                           |
                 ASP.NET Core + WebSocket host
                 control UI and Web Dashboard
```

The existing WPF application should remain a host, not become the platform
contract. Platform-neutral code must not reference WPF, WPF-UI, WebView2,
SharpDX, `Microsoft.Win32.Registry`, Windows Core Audio, or Win32 P/Invoke.

## WPF Host Extraction Inventory

The `WpfGUI` project still contains some platform-neutral behavior, but its
directory is not evidence that the behavior belongs to WPF. Extraction should
follow dependencies and ownership rather than copying current view models into
Avalonia.

Already shared outside `WpfGUI`:

- runtime startup, resource provisioning, game catalogs, profiles, co-drivers,
  prerequisites, and ordered status reporting;
- runtime controls for co-driver selection, mute, example playback, and Web
  Dashboard enablement, behind a host-supplied dashboard controller;
- runtime support-diagnostics collection for game prerequisites, UDP readers,
  local bridges, audio, and Web Dashboard state;
- versioned configuration migrations, with the application version and data
  paths supplied by the host;
- application update-definition retrieval, stable/Beta channel selection,
  version filtering, skipped-version handling, and localized changelog lookup;
- streamed application-update download execution and progress reporting, while
  launch, shutdown, and replacement remain host responsibilities;
- data-folder planning, free-space checks, copying, verification, and deletion;
- replay listing, detail lookup, best-run selection, locking, export, and
  pending-save flushing through a shared catalog service;
- co-driver online catalog comparison, streamed package download with explicit
  download/install phases, managed import/update installation, export, runtime
  refresh, and temporary-file cleanup;
- game activity contracts and telemetry-reader lifecycle;
- pacenote definitions, configuration models, replay data, and game contracts;
- audio decoding, mixing, sequencing, device contracts, and playback state;
- Web Dashboard hosting, packages, browser protocol, fonts, and pacenote icons.

The scoped extraction inventory above is now complete. Avalonia still needs
platform views that consume these services, but those views should not require
a second implementation of the underlying update, replay, diagnostics,
co-driver, or data-migration workflows.

The first `net10.0` Avalonia desktop host now builds with Avalonia `12.1.1` and
FluentAvalonia `3.0.2`. Its initial runtime view consumes the shared startup
status, game-session state, co-driver selection and example playback, mute
state, Web Dashboard controls, and the Monaco pacenote editor. The editor now
uses a platform-neutral controller and language catalog, with thin WebView2 and
Avalonia `NativeWebView` adapters. Monaco and pacenote icons are served only on
a random loopback port so Linux WebKit does not depend on `file://` worker or
cross-origin behavior. A shared Web Dashboard controller owns the
server and telemetry bridge lifecycle, while a shared game-activity session
coordinator preserves initial detection, automatic activation, fallback, and
release behavior across the CLI and desktop hosts. Native Arch rendering and
interaction validation, localization, and the remaining settings views are
still pending. The AppImage build now publishes this desktop host as its primary
entry point while retaining the separate CLI project for diagnostics.

Must remain host-specific:

- XAML views, WPF converters, controls, navigation, dispatcher access, and
  WPF-UI dialogs;
- WebView2 printing, platform file/folder pickers, registry, tray integration,
  and system notifications; the Monaco editing protocol itself remains shared;
- update confirmation/download progress UI, application shutdown, executable
  replacement, and data-folder migration process relaunch/progress windows;
- native overlay, window attachment, replay window capture, and other Win32
  integrations.

The canonical application icon is still stored under `WpfGUI/Assets` even
though it is a product asset used by every package. AppImage now carries a
correctly sized Linux packaging derivative so its build no longer depends on
the WPF source tree. Move canonical branding and other genuinely shared static
assets to a neutral asset root before the Avalonia host becomes another source
of independently maintained copies.

Recommended contracts include:

- `IAudioOutputBackend` and `IAudioOutputDeviceEnumerator`;
- `IAudioFileDecoder` and the shared sample-provider resampler;
- `IApplicationPaths` and `IConfigurationLocation`;
- `IGameProcessService` and `IGameWindowService`;
- `IGameTransport` for UDP, shared memory, pipes, or relays;
- `IOverlayHost` as an optional capability;
- `IExternalBrowser`, `IFileDialog`, `INotificationService`, and
  `IAutoStartService`.

## Audio Platform Decision

### Windows ducking diagnosis

The previous Windows playback path could cause every other application to be
attenuated. Windows exposes this behavior in:

```text
Control Panel -> Sound -> Communications
```

The default policy commonly reduces other sounds when Windows detects
communications activity. Reproducing the problem together with that policy
is strong evidence that an active audio session was being treated as a
communications stream. The previous `WaveOutEvent` path could neither declare
an explicit media stream category nor opt the session out of Windows' default
ducking behavior, so ZTMZ could not prevent the attenuation.

The Windows backend now uses WASAPI shared mode with the `Media` stream
category and calls `IAudioSessionControl2::SetDuckingPreference(true)` after
stream initialization. Microsoft documents this call as opting the session
out of the default stream-attenuation experience.

Reference:

- [IAudioSessionControl2::SetDuckingPreference](https://learn.microsoft.com/en-us/windows/win32/api/audiopolicy/nf-audiopolicy-iaudiosessioncontrol2-setduckingpreference)

### Cross-platform output policy

Do not replace the working Windows WASAPI backend merely to obtain Linux
support. Use the existing output interface for platform implementations:

```text
IAudioOutputBackend
  - WasapiAudioOutputBackend       Windows
  - PortAudioAudioOutputBackend    Linux/SteamOS
```

PortAudio supplies real-time device and stream I/O across Windows, macOS, and
Linux host APIs, including ALSA. It does not decode audio files, resample them,
or replace ZTMZ's mixer and sequencing logic. Decoder and resampler portability
is addressed independently through `IAudioFileDecoder`, libsndfile/FFmpeg, and
the managed WDL resampler. The existing mixer, sequential lanes, speed control,
intercom effect, mute state, and playback diagnostics are shared by both output
backends.

## Web Dashboard Extraction

The Web Dashboard runtime is now extracted into the platform-neutral
`ZTMZ.PacenoteTool.WebDashboard` `net10.0` project. It owns the ASP.NET Core
server, WebSocket protocol, browser-side Lua transport, neutral drawing DTOs,
dashboard catalog, static client, dashboard packages, fonts, and pacenote icon
assets. The project has no WPF, GameOverlay.Net, SharpDX, or WebView2 reference.

Windows keeps its native overlay renderer in `Base.UI` and adapts the existing
`Dashboard` objects to portable `WebDashboardDefinition` objects. Linux hosts
`WebDashboardRuntime` directly. `WebDashboardGameSessionBridge` subscribes to
the shared core's track/car, game-state, pacenote, race, and telemetry events,
so browser Lua dashboards receive the same evolving `GameContext` rather than
an independent Linux-only telemetry model. `--web-dashboard` starts the server,
and combining it with `--listen-game` keeps the complete game session alive
until `Ctrl+C` unless a bounded `--listen-seconds` value is supplied.

The next Web Dashboard work is product integration rather than platform
extraction: expose its settings in the future Avalonia UI and validate the
browser renderer against real Proton game traffic on SteamOS hardware.

## Game Activity Monitoring

Game discovery now uses the platform-neutral `IGameActivityMonitor` contract.
The native Linux implementation polls `/proc` and recognizes direct executable
names, Windows executable paths in Proton command lines, Steam application
environment variables, and `compatdata/<app-id>` paths. Its initial scan also
detects a game that was already running before ZTMZ started.

The Linux monitor activates games through the shared `ZTMZPacenoteTool` session
controller. Starting or changing games initializes the corresponding reader,
selected profile, co-driver audio pipeline, pacenote core, and Web Dashboard
bridge once; leaving the game releases the reader and all session subscribers.
The explicit `--listen-game` option uses that same complete session as a manual
override, and `--no-game-watch` retains the bounded diagnostics behavior.

Flatpak deliberately cannot inspect host processes. ZTMZ does not request
`org.freedesktop.Flatpak` access or use `flatpak-spawn --host`, because that
would be a broad sandbox escape and unsuitable as the normal Flathub design.
The Flatpak build therefore reports that automatic process discovery requires
the planned versioned Proton lifecycle relay; manual `--listen-game` selection
continues to work in the meantime.

## Game Support Order

### Tier 1: native UDP candidates

Validate these first because their runtime telemetry already arrives over UDP:

- DiRT Rally;
- DiRT Rally 2.0;
- EA SPORTS WRC;
- BeamNG.drive OutGauge and Rally Mode bridge.

Required work is limited mainly to Proton configuration paths, process
detection, loopback binding, and launch testing. UDP parsing and the game-state
contracts should remain shared.

Proton configuration discovery is shared through `IGameDocumentsLocator`.
Windows resolves the existing Documents folder, while Linux parses Steam's
library list, finds the library containing the game's AppID, and resolves the
Windows Documents directory in that Proton prefix. The DiRT Rally prerequisite
checker and XML repair implementation remain a single cross-platform code path.

### Tier 2: Proton relay candidates

ACR uses Windows named shared-memory pages plus an injected DLL/named-pipe
bridge. A native Linux process should not attempt to open these Windows IPC
objects directly. Run a small Windows relay inside the same Proton prefix and
forward versioned telemetry, lifecycle events, and live pacenotes to the native
Linux host over localhost UDP or another explicit cross-boundary transport.

RBR should be split by capability. Reuse UDP telemetry where available, then
relay registry-, process-memory-, or plugin-dependent data from the Proton
environment. Missing relay capabilities must be reported explicitly rather
than silently treated as a supported full integration.

### Tier 3: platform-native integrations

- Gamescope in-game overlay;
- SteamVR overlay on Linux;
- PipeWire replay audio and window capture;
- SteamOS notifications, autostart, packaging, and update integration.

Gamescope is a compositor, and Steam Deck Gaming Mode isolates games in its
own Xwayland environment. A normal Avalonia transparent window must not be
assumed to behave like the current Win32 click-through overlay. Treat this as
an independent feasibility project.

### Experimental Vulkan in-game overlay route

The viable route for an actual SteamOS/Proton in-game overlay is a small native
Vulkan Layer, not an always-on-top Avalonia window and not a dependency on a
private Gamescope protocol. It is an optional post-MVP feature and is not part
of the initial AppImage feature promise.

```text
ZTMZ Linux host
  game session, pacenote core, configuration
                  |
     versioned local IPC (Unix-domain socket)
                  |
ZTMZ Vulkan Layer inside the selected game process
  receive current/previous pacenote rows and settings
  render the native roadbook overlay before presentation
                  |
Proton / DXVK / vkd3d -> Vulkan -> Gamescope
```

The layer should remain a narrow C/C++ rendering component. It should not host
Avalonia, WPF, WebView, the pacenote database, game detection, audio playback,
or a second copy of the dashboard business logic. The managed host continues to
resolve names, icons, translations, history, opacity, and layout settings, then
sends an explicitly versioned display snapshot to the layer. The first renderer
should be native and minimal; embedding a browser inside the injected layer is
out of scope.

#### Loading and packaging

- Provide a dedicated Steam launch wrapper such as `ztmz-overlay %command%`.
  It enables the layer only for the selected game launch and lets the normal
  Steam/Proton launch path remain intact.
- Do not make an AppImage attempt global or silent injection into unrelated
  Steam games.
- Build and ship matching 64-bit and, where a target requires it, 32-bit layer
  binaries. Report the layer version, renderer detection result, IPC connection,
  and active capability in ZTMZ diagnostics.
- If the game is not presenting through Vulkan, the layer reports unavailable
  and ZTMZ continues to offer the Web Dashboard.

#### Compatibility and safety boundary

ProtonDB Gold means that the game itself is usable under Proton. It does not
prove that a third-party Vulkan Layer loads successfully, maintains frame
pacing, or is allowed by a game's anti-cheat policy. The layer must therefore
be opt-in, title-specific, and disabled by default.

- Never load it automatically for a game with active anti-cheat protection.
- Enable a protected online game only after documented approval from the game
  publisher and, where applicable, the anti-cheat provider; no evasion,
  concealment, or compatibility bypass is a product feature.
- Keep the Web Dashboard as the supported fallback for all protected or unknown
  games.
- Treat launch failure, a blocked module, an anti-cheat warning, a renderer
  mismatch, or unstable frame pacing as an immediate unsupported result rather
  than trying alternative injection techniques.

#### First validation sequence

1. Establish an automated Vulkan Layer smoke test against a small Vulkan sample:
   layer load, IPC reconnect, resize, focus loss, and clean unload.
2. Test DiRT Rally 2.0 under the current Proton version. Its Windows DirectX
   path is expected to use DXVK/Vulkan, but the active renderer must be verified
   from the layer diagnostics rather than assumed.
3. Test DiRT Rally 1 only when launched through Proton. Its native Linux/Feral
   renderer may be OpenGL, in which case a Vulkan Layer cannot render over it.
4. Probe Assetto Corsa Rally's actual presentation API under Proton, then add a
   per-title compatibility record only after telemetry, route updates, frame
   pacing, suspend/resume, and game shutdown pass on real Steam Deck hardware.
5. Defer EA SPORTS WRC until its base Proton compatibility is stable. A broken
   game launch cannot validate an overlay.

Windows keeps its existing external native overlay. A Vulkan Layer may gain a
Windows build for games that actually render with Vulkan, but it is not a
replacement for the DirectX-independent Windows overlay and does not justify
adding D3D hooks.

## Delivery Phases

### Phase 0: enforce platform boundaries

- Keep the Windows WPF product building and behaviorally unchanged.
- [x] Move Windows registry access behind application-path/configuration services.
- [x] Move WASAPI device enumeration and playback construction behind audio interfaces.
- Separate Windows shared-memory/process helpers from portable game contracts.
- [x] Make `Base` and `Core` target plain `net10.0`.
- [x] Add a `net10.0` portable test project runnable on Windows and Linux.

The application-data boundary now exposes `IApplicationPaths` and
`IConfigurationLocation`. The Windows host supplies the existing registry
location, while platform-neutral code uses `ZTMZ_DATA_HOME` and defaults to
`$XDG_DATA_HOME/ztmz` (or `~/.local/share/ztmz`) on Linux. Existing callers can
continue using `AppLevelVariables` while they are migrated to constructor
injection incrementally.

The audio boundary now exposes `IAudioOutputDeviceEnumerator`,
`IAudioPlaybackEngine`, and `IAudioPlaybackEngineFactory`. Windows resolves
these contracts to the existing WASAPI implementation. The Linux host registers
PortAudio explicitly; unsupported hosts retain the unavailable implementation,
so shared core initialization cannot silently call Windows Core Audio.

Application startup now also has one platform-neutral orchestration path in
`ZtmzRuntime.InitializeAsync()`. Both the WPF and Linux hosts use the same:

- `ResourceBootstrapService` for data directories, localization, and official
  pacenote database provisioning;
- `GameCatalogService` for plugin discovery, ordering, configuration loading,
  and configuration migration;
- `ProfileCatalogService` for profiles and co-driver package discovery;
- `PrerequisiteService` for prerequisite checks and repairs;
- ordered startup status callbacks for resources, profiles, games, co-drivers,
  and audio devices.

The hosts now select platform implementations and present the resulting runtime
state. WPF injects WASAPI and continues to own Windows process watching, DLL
injection, native overlays, WebView2, and replay capture. The Linux host injects
PortAudio and owns Linux/Flatpak command-line diagnostics. This removes the two
independent ZTMZ business bootstraps without pretending that their platform
backends are interchangeable.

### Phase 1: headless Linux proof of concept

- [x] Add a native `net10.0` Linux bootstrap host.
- [x] Verify XDG data-root resolution and write access on Arch Linux.
- [x] Publish and run the bootstrap inside a local Flatpak development sandbox.
- [x] Provision and load the official pacenote database.
- [x] Load portable Codemasters game providers and persist their UDP configuration.
- [x] Verify host-to-Flatpak UDP telemetry with valid simulated Codemasters frames.
- [x] Load profiles and co-driver packages from the XDG data root, including the configured additional co-driver search path.
- [x] Use the same shared runtime bootstrap and startup status sequence as the Windows host.
- [x] Activate game detection, profile/co-driver selection, pacenote core,
  PortAudio, and Web Dashboard through one shared game-session controller.
- [x] Package and provision the default profile and co-driver into a fresh AppImage data root.
- Receive one UDP game's telemetry under Proton.
- Decode, mix, and play a real co-driver package through PortAudio.
- [x] Log co-driver, output-device, playback, and telemetry diagnostics without a desktop UI.

The bootstrap host is intentionally diagnostic-only today. It provisions the
packaged `data.zdb` into the writable user-data root, preserving runtime database
changes while the packaged source is unchanged. It reports the loaded token and
game-provider counts, can bind a selected UDP provider for a bounded diagnostic
interval, lists profiles, PortAudio output devices, and co-driver packages, and
can decode and play a selected audio file through the complete ZTMZ playback pipeline. The
next runtime milestone is rebuilding the Flatpak and audibly verifying a real
co-driver file, followed by receiving telemetry from a real DR1 stage. The
current Arch VMware guest cannot complete that test until Vulkan rendering is
available; the simulated DR1 process lifecycle is not counted as stage telemetry
or audible playback verification.

## Flatpak Development Baseline

`packaging/flatpak/io.github.strawhatboy.ZTMZPacenoteTool.Devel.yml` is a local
development manifest, not the final Flathub submission. It uses Freedesktop
`25.08` plus `org.freedesktop.Sdk.Extension.dotnet10` and packages the native
bootstrap with `--share=network` for Proton telemetry and
`--socket=pulseaudio` for the user's PipeWire/PulseAudio compatibility socket.
It grants scoped access to native Steam data, Flatpak Steam data, and removable
libraries under `/run/media` so prerequisite checks can read and repair Proton
configuration files. It grants no complete home or host filesystem access. The
development build targets `linux-x64` explicitly; the published output contains
the Linux PortAudio library without copying the Windows, macOS, or ARM native
assets into the Flatpak.

The local Arch VM uses an unprivileged `deck` account for source checkout,
builds, tests, Flatpak installation, and application execution. Root SSH is
reserved for system maintenance. Both accounts use key authentication, and
the development account's password remains locked. This keeps normal build
artifacts out of `/root` without adding another password or an unrestricted
passwordless-sudo entry.

When the VM needs the Windows host's development proxy, set `http_proxy` and
`https_proxy` in the invoking shell. Proxy addresses are machine-specific and
must not be committed to the Flatpak manifest or application configuration.

Before a Flathub submission, the packaging work must additionally:

- replace the local directory source with a public source pinned to a commit;
- generate and commit offline NuGet sources with `flatpak-dotnet-generator`;
- settle the permanent application ID and verify the matching domain or source host;
- install the license, desktop file, icon, and complete English AppStream metadata;
- add only the runtime permissions justified by UDP telemetry, audio, dashboards,
  and portals;
- pass `flathub-build` and `flatpak-builder-lint`.

## AppImage Development Baseline

AppImage is now the primary packaging target for the first SteamOS release.
`packaging/appimage/build-appimage.sh` creates a self-contained `linux-x64`
publish and adds the portable Codemasters provider, official database,
localization, desktop metadata, Web Dashboard files, fonts, pacenote icons, and
PortAudio native library. Required payload checks fail the build when a shared
resource is accidentally omitted.

The generated AppDir is approximately 137 MB and the zstd-compressed AppImage
is approximately 60 MB. The first development package was verified on the Arch
VM through both its normal FUSE path and `APPIMAGE_EXTRACT_AND_RUN=1`. AppImage
runs with the current user's normal permissions, so the existing native `/proc`
monitor can detect host Steam/Proton processes without a broad sandbox escape
or a separate lifecycle relay.

The package remains read-only. Configuration, profiles, co-driver packages,
logs, and provisioned data continue to use the XDG data root. A portable archive
can be produced from the same AppDir as a fallback for environments where FUSE
is unavailable. Flatpak files remain in the repository as a tested future store
channel, but Flatpak-specific lifecycle work no longer blocks the first SteamOS
candidate.

### Phase 2: useful Steam Deck MVP

- [x] Extract and host the Web Dashboard on Linux.
- [x] Establish an Avalonia 12 + FluentAvalonia desktop host backed by the
  shared Runtime, game monitor, co-driver controls, and Web Dashboard lifecycle.
- [x] Host the shared Monaco pacenote editor in Avalonia with bidirectional
  JavaScript messaging and loopback-served editor/icon assets.
- Complete the Avalonia views for game, profile, co-driver, audio device,
  dashboard, and diagnostics selection, including localization.
- Bundle or provision WPE WebKit (preferred) or WebKitGTK in the AppImage and
  validate resizing, IME input, clipboard, and navigation under Wayland.
- Publish the Avalonia host as the AppImage desktop entry point and validate it
  under the Arch VM and SteamOS Desktop Mode.
- Support the Tier 1 UDP games.
- Provide SteamOS path discovery and a distributable Linux package.

This phase is the first public Steam Deck candidate. It does not wait for an
in-game Gamescope overlay.

### Phase 3: Proton relays

- Build a versioned relay protocol.
- Add the ACR shared-memory/live-pacenote relay.
- Add RBR relay capabilities incrementally.
- Surface relay version, connection, and capability diagnostics in the UI.

### Phase 4: desktop feature completion

- Add the remaining replay, package management, update, and diagnostics views
  to Avalonia by consuming the shared services rather than porting WPF view
  models or XAML unchanged.
- Keep Monaco browser-hosted unless a reliable Linux embedded-webview solution
  is selected.

### Phase 5: SteamOS overlay and capture research

- Prototype the opt-in Vulkan Layer route and its versioned local IPC protocol.
- Verify actual renderer selection before treating a Proton title as a candidate.
- Measure frame pacing, overlay latency, resize, suspend/resume, focus,
  controller input, and Gaming Mode lifecycle behavior on real Steam Deck
  hardware.
- Keep Gamescope integration limited to normal Vulkan presentation; do not use
  undocumented/private Gamescope protocols as a product dependency.
- Research PipeWire/FFmpeg replay capture independently.

## Acceptance Criteria

### Linux proof of concept

- A self-contained `linux-x64` build starts on SteamOS without Wine.
- A selected UDP game supplies stable telemetry while running under Proton.
- A normal co-driver package plays through the selected Linux audio device.
- Playback does not require WPF, WebView2, Media Foundation, or WASAPI.

### Steam Deck MVP

- The browser control UI and Web Dashboard are usable at the Deck's native
  resolution and from another LAN device.
- Game/profile/co-driver selection persists without Windows registry access.
- At least the Tier 1 integrations report honest capability diagnostics.
- Suspend/resume, game restart, audio-device changes, and application restart
  recover without losing playback permanently.
- Windows builds and their native overlay remain supported by the same shared
  core.

## Explicit Non-Goals For The MVP

- Running the current WPF executable natively on Linux;
- treating WebView2 as a Linux browser host;
- opening Proton's Windows named maps directly from native Linux code;
- promising a click-through in-game overlay merely because Avalonia opens a
  desktop window;
- auto-loading a Vulkan Layer for anti-cheat-protected or unverified games;
- bypassing, concealing, or attempting to work around anti-cheat protections;
- replacing the stable Windows WASAPI backend with PortAudio;
- blocking the first Steam Deck release on full Windows feature parity.
