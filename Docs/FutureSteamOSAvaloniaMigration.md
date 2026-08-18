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

This is an infrastructure milestone, not a playable Steam Deck release. The
Linux host now has a PortAudio output implementation and loads co-driver
packages through a cross-platform decoder boundary. WAV, AIFF, MP3, Ogg, FLAC,
and Opus use libsndfile; WMA, AAC, MP4, and M4A fall back to FFmpeg. Audible
playback inside the rebuilt Flatpak remains to be verified on the Arch VM. The
native overlay still reports unavailable rather than invoking Win32 code.

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
| Main UI | WPF, WPF-UI, WinForms, `net10.0-windows` | Browser-based control UI first; optional Avalonia host later |
| Audio output | NAudio WASAPI | WASAPI remains on Windows; PortAudio is implemented for Linux |
| Audio decoding and resampling | NAudio `AudioFileReader` and Media Foundation | `IAudioFileDecoder`, libsndfile, FFmpeg fallback, and managed WDL resampling on Linux |
| Native overlay | GameOverlay.Net, SharpDX, Direct2D, `user32.dll`, HWND attachment | Keep as a Windows feature; research a Gamescope-specific host separately |
| Web Dashboard | ASP.NET Core, WebSocket, browser JavaScript | Extracted into the platform-neutral `WebDashboard` project and hosted by the Linux runtime |
| Script editor | Monaco hosted by WPF WebView2 | Reuse Monaco through the local web host or system browser on Linux |
| Game discovery and IPC | Windows process/window APIs, registry, named maps, named pipes, injection | Introduce platform services and Proton relay processes where required |
| Replay capture | WASAPI loopback and Windows window capture | Add PipeWire/FFmpeg capture only after the core Linux release |
| Packaging and updates | Inno Setup and Windows executable updates | Define a separate SteamOS/Linux package and update channel |

WPF itself only runs on Windows, and the current WebView2 desktop runtime does
not provide a Linux host. Porting only the audio backend therefore cannot make
the current WPF executable a native SteamOS application.

References:

- [Microsoft WPF overview](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/)
- [Microsoft WebView2 platform overview](https://learn.microsoft.com/en-us/microsoft-edge/webview2/)
- [Avalonia supported platforms](https://v11.docs.avaloniaui.net/docs/overview/supported-platforms/)
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
`WebDashboardRuntime` directly and publishes decoded UDP telemetry to browser
Lua dashboards. `--web-dashboard` starts the server, and combining it with
`--listen-game` keeps the telemetry loop alive until `Ctrl+C` unless a bounded
`--listen-seconds` value is supplied.

The next Web Dashboard work is product integration rather than platform
extraction: expose its settings in the future Avalonia UI, populate full
pacenote/replay `GameContext` state from the shared runtime, and validate the
browser renderer against Proton game traffic on SteamOS hardware.

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
co-driver file, followed by receiving telemetry from a game running under
Proton.

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

### Phase 2: useful Steam Deck MVP

- [x] Extract and host the Web Dashboard on Linux.
- Add a browser-based control UI for game, profile, co-driver, audio device,
  dashboard, and diagnostics selection.
- Support the Tier 1 UDP games.
- Provide SteamOS path discovery and a distributable Linux package.

This phase is the first public Steam Deck candidate. It does not wait for an
Avalonia rewrite or an in-game Gamescope overlay.

### Phase 3: Proton relays

- Build a versioned relay protocol.
- Add the ACR shared-memory/live-pacenote relay.
- Add RBR relay capabilities incrementally.
- Surface relay version, connection, and capability diagnostics in the UI.

### Phase 4: optional native desktop shell

- Add Avalonia if a native settings/editor experience provides enough benefit
  beyond the web control UI.
- Reuse view models and platform services, but do not attempt to compile WPF
  XAML unchanged.
- Keep Monaco browser-hosted unless a reliable Linux embedded-webview solution
  is selected.

### Phase 5: SteamOS overlay and capture research

- Prototype a Gamescope-compatible overlay path.
- Measure frame pacing, latency, focus, controller input, and Gaming Mode
  lifecycle behavior on real Steam Deck hardware.
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
- replacing the stable Windows WASAPI backend with PortAudio;
- blocking the first Steam Deck release on full Windows feature parity.
