# Rust Native Layer Evaluation

## Decision

Rust is viable for narrowly scoped native capabilities, but it is not a
replacement for the ZTMZ runtime. The current managed Core owns rally-domain
state, pacenote scheduling, profiles, co-driver packages, replay semantics,
configuration, localization, and dashboard contracts. Rewriting that layer
would delay Windows and SteamOS work while creating a second implementation of
well-tested business behavior.

Use Rust only where all of these are true:

1. the work is platform-native or performance-sensitive;
2. the boundary can be expressed as bytes, fixed-layout structs, handles, or
   polling APIs;
3. the component can be validated from recorded fixtures without a running
   game; and
4. it removes a measured reliability, latency, or portability problem.

The first implementation should therefore be a small optional native module,
not a rewrite and not a new mandatory runtime dependency.

## Current Codebase Boundaries

The codebase already has useful boundaries:

| Area | Current implementation | Rust suitability | Decision |
| --- | --- | --- | --- |
| Pacenote core, profiles, replay semantics, i18n | `Base` and `Core` managed services | Low | Keep C# shared runtime |
| WPF and Avalonia UI, Monaco, dialogs, notifications | Host-specific UI projects | Low | Keep in their UI frameworks |
| Web Dashboard server and browser Lua protocol | `WebDashboard` | Low | Keep C# and JavaScript |
| Audio queue, mixer, sequence behavior | Shared C# interfaces and sample providers | Low | Keep shared C# behavior |
| Device stream I/O | `Audio.PortAudio` wraps PortAudioSharp | Medium | Do not replace before a measured audio defect justifies it |
| UDP packet ingestion and binary decoding | `UdpReceiver`, game readers | Medium | Candidate only after profiling allocation/packet-loss pressure |
| Windows shared memory and process-memory reads | `SharedMemoryReader`, `MemoryReader` | Medium | Candidate for isolated readers, but Windows-only today |
| ACR experimental pipe bridge and injection support | `ACRExperimentalBridgeController` and bridge DLL | Medium/high risk | Keep current path until protocol and anti-cheat constraints are stable |
| Windows native overlay | `Platform.Windows/NativeOverlay`, GameOverlay.Net, SharpDX | Medium/high risk | Keep current Direct2D implementation; Rust does not make it portable |
| Proton relay and future Vulkan overlay | Not yet implemented | High potential | Best future Rust candidate, after a supported game path is proven |

Rust cannot turn a Windows named map, DLL injection, Direct2D HWND attachment,
or a Windows game DLL into a SteamOS feature. Proton still needs a deliberate
transport boundary between the Windows game process and the native Linux host.

## Recommended Ownership Model

```text
Avalonia / WPF hosts
        |
Shared C# runtime and game contracts
        |
INativeCapability (optional, polling-oriented contract)
        |
  P/Invoke C ABI                    Native executable protocol
        |                                      |
Rust cdylib                     Rust Proton relay / future overlay helper
```

The C# host retains lifecycle ownership: process discovery, user consent,
configuration, logs, UI status, and recovery. Rust owns only the native handle
and returns explicit status/error codes. No Rust callback may invoke managed
code from a real-time thread.

For a library boundary, expose a stable C ABI from a Rust `cdylib` and call it
from C# through source-generated or handwritten P/Invoke. The ABI must use
fixed-width numbers, opaque handles, caller-owned buffers, and an explicit
`last_error` or error-buffer function. Do not expose Rust collections,
references, strings, or panics across the boundary. Rust's official FFI
guidance and its C interop guide both make the C ABI the correct stable
interoperability choice; C++ does not provide a stable ABI.

`cxx` is appropriate only if a future component must interoperate directly
with an existing C++ library. ZTMZ's primary host is C#, so adding a C++ shim
just to use `cxx` would increase, rather than reduce, maintenance.

## Priority Order

### 1. Instrument before migrating

Add or retain measurements for:

- UDP receive-to-core latency, dropped packets, allocations, and CPU use;
- audio callback underflows, corrected samples, stream latency, and device
  reconfiguration time;
- bridge write/read latency and reconnect counts;
- overlay frame duration, failed attachment count, and CPU/GPU cost.

No Rust component should be introduced solely because it may be faster. The
current PortAudio backend already isolates device I/O behind
`IAudioOutputBackend`; it is not a justified first migration unless the data
shows a managed wrapper problem that cannot be fixed locally.

### 2. Build a disposable FFI proof of concept

Create a separate `native/ztmz-native-probe` Cargo workspace, not a production
dependency. It should expose only deterministic operations such as:

- version query;
- byte-buffer checksum or fixed telemetry-frame validation;
- explicit error retrieval;
- create/read/dispose an opaque handle.

The managed test project should compare Rust output against existing C# fixture
vectors. Build it for `win-x64` and `x86_64-unknown-linux-gnu`; package it only
in a developer artifact initially. This proves ABI loading, failure reporting,
logging, and architecture selection before touching a game integration.

### 3. First production candidate: Proton relay

Once a supported Proton game requires a native relay, implement that relay as a
small Rust executable rather than embedding it in the Avalonia process. It
should have one versioned local protocol and no rally business logic:

```text
Game-side source -> Windows/Proton bridge -> versioned local packets
                                      -> Rust relay -> localhost/XDG socket
                                      -> shared C# game transport -> Core
```

The relay may normalize process, socket, and permission behavior on SteamOS,
restart independently, and be integration-tested without the UI. It must not
infer race starts from movement, mutate game memory without an explicit product
decision, or become a second pacenote engine.

### 4. Future candidate: opt-in Vulkan overlay helper

For the documented Proton Vulkan overlay route, Rust is a reasonable language
for the layer/helper because the work is native, concurrent, and Linux-first.
It is still a research task, not a migration target:

- retain the current Windows Direct2D overlay for Windows;
- prove the rendering path in a non-anti-cheat test environment first;
- keep the layer restricted to supported games and explicit user opt-in;
- use the same dashboard/telemetry protocol as the Web Dashboard, not a Rust
  dashboard language or a forked configuration model.

### Not first candidates

Do not start with any of the following:

- replacing C# pacenote timing or audio queue logic;
- replacing PortAudio merely because its underlying library is C;
- translating all shared-memory readers before a real defect is measured;
- rewriting the existing ACR injected bridge while its game-facing protocol is
  still experimental;
- moving dashboard Lua execution, WebView hosting, or UI layout into Rust.

## FFI Contract Rules

1. Version every request and response structure from day one.
2. Use pull/poll APIs for high-frequency data. The C# host controls when data
   crosses the boundary.
3. Copy boundary data; never expose a Rust pointer past the next call unless an
   opaque handle explicitly owns it.
4. Catch every Rust panic at the FFI boundary and convert it to an error code.
5. Provide `create`, `stop`, and `dispose` operations that are idempotent.
6. Treat native libraries as optional capabilities. A load failure degrades one
   integration and must not stop audio, Web Dashboard, or the desktop host.
7. Keep configuration parsing, localization, telemetry-to-`GameData` mapping,
   and diagnostics schemas in C# unless the native protocol itself requires
   them.

## Build and Packaging Rules

- Keep Cargo sources under `native/`; never put Rust build output under `src`.
- Produce one native asset per RID: `win-x64`, `win-arm64` when supported, and
  the Linux target used by the AppImage.
- The Windows installer and AppImage include only their own native artifact;
  do not ship all Rust targets in every package.
- CI or the release build must run ABI fixture tests before packaging.
- A Rust release is blocked if its source maps, debug symbols, or unused target
  artifacts materially inflate the installer/AppImage.
- Pin the Rust toolchain in the workspace and record the minimum supported
  compiler version. Do not depend on a developer's global Cargo installation.

## Release Gates for a Production Migration

A candidate can replace an existing path only after all of the following:

1. fixture parity with the managed implementation;
2. restart, disconnect, malformed-packet, and missing-library tests;
3. no regression in end-to-end pacenote timing or dashboard updates;
4. Windows and Linux packaging contain only the correct RID asset;
5. diagnostics report the native module version, loaded path, status, and last
   failure without exposing raw memory or private game data;
6. a rollback switch retains the managed implementation for one release.

## References

- [Rustonomicon: FFI](https://doc.rust-lang.org/nomicon/ffi.html)
- [Rust C interoperability guide](https://doc.rust-lang.org/stable/embedded-book/interoperability/rust-with-c.html)
- [CXX interoperability guide](https://cxx.rs/)
- [windows-rs book](https://microsoft.github.io/windows-rs/book/)
