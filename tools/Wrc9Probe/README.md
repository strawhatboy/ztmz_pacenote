# WRC 9 integration and verification

Experimental Windows, single-player support for Steam app 1267540, build
6335125. The provider is shared by WPF and Avalonia. Other executable builds
are rejected before fixed-offset reads or production injection.

## Build identity

- `WRC9.exe` SHA256:
  `2A671399D771284502443B28C0B932DEC9189E0A68D40FEEE4C8024B8F5220E6`.
- `PhysX64_s.dll` SHA256 (required for motion telemetry):
  `3775BC47A4BEE80CB752544FEF5C40FD22E8A734CB930FE33F33CCC21C24E1F1`.
- Archives use the plaintext PPKG v2 index and 40-byte trailer, as WRC 7 does.
  The installed tables contain 204 atoms and 4,064 blocks. Definitions are read
  from the installation; extracted game resources are not shipped.

## Live evidence, 2026-10-15

Initial audio/restart/retirement tests used Toyota Yaris WRC on Rallye Monte-Carlo,
Agnières-en-Devoluy. The final timing run used Hyundai i20 R5 on Rally Sweden, Torsby.

- FMOD Studio observation recorded car/UI events but no spoken pacenote
  events. The lower-level `FModPlaySound` driver recorded successful PAF
  playback, including `BLOCK_0321`, `BLOCK_1605`, `BLOCK_1855` and `BLOCK_1854`.
- After restarting the game to remove observers, the production bridge and
  managed receiver exchanged `READY`, emitted 3/2/1/GO, and decoded:
  `BLOCK_1854` → `20/hairpin_left/into/flat_right/tightens_to_4/short`;
  `BLOCK_1799` → `into/4_left/narrows/30`.
- Restart, clock progression, pause and Give up were recorded. Race end was
  emitted only for the verified retirement flags and processed result. The
  sanitized regression fixture keeps all timer changes and transition edges;
  it excludes addresses, game assets and unrelated telemetry.
- The final run progressed directly from countdown to racing, paused/resumed,
  and emitted one normal finish. Raw time 257,588 ms plus penalty 5,000 ms
  equalled the processed result 262,588 ms (4:22.588). Both native result flags
  and stored times agreed. A second sanitized replay fixture covers this run.
- Production native dashboard loaded after game startup, automatically selected
  DX11 from the actual swap chain, connected to the local test server and
  submitted thousands of frames without a render error. The user confirmed the
  panel was visible. A fresh process after explicitly selecting DX11 also passed.
  A further full restart with DX12 explicitly selected still exposed DX11.
  Read-only inspection found the engine's `g_pDX11Device` non-null and
  `g_pDX12Device` null; `d3d11on12.dll` was not loaded. The menu-setting mismatch
  remains unexplained, and does not establish native DX12 support. This used
  a temporary telemetry panel, not a full UI-host acceptance test.
- WPF and Avalonia Debug builds include the provider, image, both translations,
  x64 bridge/helper and MinHook license. The normal build/publish targets,
  legacy release scripts and installer payload guards include WRC 9.
- 200 focused WRC managed tests pass, including both recorded lifecycle fixtures,
  separate global/TLS clock tables, ownership changes, vocabulary/path checks
  and bounded pipe framing. WRC7 and WRC9 native entry-hook smoke tests pass.
- WPF Release application playback was subsequently confirmed by the user.
  Initially telemetry and the dashboard worked without the voice DLL loaded.
  After connecting the live bridge, the app logged READY and played blocks
  0166–0170; the user confirmed hearing the calls. The voice bridge must be
  connected for each new game process, independently of dashboard injection.
  Audible playback through Avalonia has not yet been checked.

Still unverified: actual DX12 rendering/device switching, multiplayer, multi-lap/split-screen, damage/reset events,
steering/clutch, stage distance and some directional pacenote conventions.
The captured GO transition sets `TimerStarted` while lap count is still zero
for roughly 1.4 seconds. The reader now keeps that pending state in countdown;
a regression test covers the transition into the first timed lap.
Original game speech stays enabled. Unmapped blocks are skipped in their entirety.

## Verified memory layout

All reads are external and read-only; no game functions are called.

| Data | Verified location / evidence |
| --- | --- |
| Manager | EXE+`0x1a4e820`, vtable `0x13f3398` |
| Race | Manager+`0x48`, also EXE+`0x1b024e8`, vtable `0x13f2510` |
| Profile | Count +`0x1124`, array +`0x1128`, player index +`0x1130`; support one profile/index zero |
| Stage/rally | Manager+`0xbc8` / +`0xbd0`; name IDs +12 / +16 |
| Car | Profile+`0x30`; name ID +8 |
| Timer | Race+`0x4e8`; participant +`0xa0`, reciprocal participant+`0x38` |
| Result | Timer flags +`0x9c`, +`0x9d`, +`0x9f`; profile raw/total/ready/failed +`0x194`/`0x198`/`0x1a0`/`0x1a1` |
| Lap | Count +`0x3c`, array +`0x40`, stride `0x250`; protected start/end +`0x10`/+`0x20` |
| Penalty | Protected timer+`0xc8`; global table at EXE+`0x19e9140` |
| Running clock | Race+`0x898` → protected +`0x2e0`; **main-thread TLS table**, not the global table |
| Clock TLS | TEB+`0x58` → TLS array; index EXE+`0x1b0eb38`; slot+`0x40` → table |
| Vehicle | Participant+`0x70`, vtable `0x13feda8`, reciprocal +`0x60`; drive +`0x80` |
| Scalars | Drive gear +`0x128`, engine angular speed +`0x134`, maximum +`0x448`; controls +`0xe0` (throttle/brake/handbrake) |
| Motion | Drive rigid body +`0xc0`; verified PhysX vtable/getters, shared WRC7 pose/velocity decoder |

Protected integers use the index at field+8 and the mask
`((uint)(field + 8) + index) * 0x159a55e5 + 0x159ae628` (unchecked), replacing
a zero mask with `0xb10be924`. Bounded retries verify the index, table pointer
and stored value. The reader rechecks ownership/result fields before publishing.
Raw time is clock minus lap start; successful results require agreement with
the processed profile total and penalty. Paused/missing clocks cannot prove finish.

## Production bridge

`build-live.bat` builds and runs the fixed-entry smoke test, then builds
`Wrc9Bridge.dll` and `Wrc9BridgeInject.exe`. Only those binaries and the license
are staged into `games/WRC9Bridge`; observation DLLs are development-only.

The driver export is at RVA `0xadcd30`. Its exact 19-byte entry consists of
whole position-independent instructions: `mov rax,rsp; push rbp; push rdi;
lea rbp,[rax-0xa8]; sub rsp,0x198`. The following RIP-relative instruction is
not overwritten. Mismatched entries are rejected. The shared hook suspends
existing threads while installing and rejects instruction pointers inside the
entry. The bridge remains pinned until game exit.

The driver callback calls the original function and queues only successful,
non-paused voice calls. A bounded queue and worker-only pipe I/O prevent the
receiver from blocking the audio callback. Stale calls older than 500 ms are
discarded. Countdown observation uses `fmodstudio.dll`; game audio is preserved.
The receiver uses a current-user-only pipe, bounded lines and a READY handshake.

## Development commands

```powershell
dotnet build tools/Wrc9Probe -p:NuGetAudit=false
dotnet run --project tools/Wrc9Probe --no-build -- session <PID>
dotnet run --project tools/Wrc9Probe --no-build -- capture <PID> 300 tmp/wrc9/new-capture.jsonl
dotnet run --project tools/Wrc9Probe --no-build -- listen <PID> src/ZTMZ.PacenoteTool.Base/data.zdb
dotnet test src/ZTMZ.PacenoteTool.Portable.Tests --filter 'FullyQualifiedName~Wrc'
```

Start the receiver before injection and use the same desktop user as the game.
An agent sandbox account may be unable to accept the game's current-user pipe
connection. A timeout is handled as a diagnostic failure, not an unhandled crash.
The production UI action independently verifies the executable hash and waits
for READY; successful DLL loading alone is not reported as a connected bridge.

`build-trace.bat` and `build-driver-trace.bat` are observers used for investigation.
Never load them alongside the production bridge: they target the same functions.
Restart WRC 9 between observation and production tests. No installed game files
are modified by these tools.
