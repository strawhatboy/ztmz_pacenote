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

Still unverified: actual DX12 rendering/device switching, multiplayer, multi-lap/split-screen,
manual car-reset events, tyre punctures, race position and some directional pacenote conventions.
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
| Scalars | Drive gear +`0x128`, engine angular speed +`0x134`, maximum +`0x448`; controls +`0xe0` (throttle/brake/handbrake/steering/clutch) |
| Motion | Drive rigid body +`0xc0`; verified PhysX vtable/getters, shared WRC7 pose/velocity decoder |

## Expanded vehicle telemetry, 2026-10-16

Live checks used the Ford Fiesta WRC on XION Rally Argentina, El Condor reverse.
Three read-only captures cover full left/right steering, clutch, acceleration,
braking, reverse, collisions, pause and stage restart. The production capture
completed 600 seconds without a missing session sample. No game memory is written.

| GameData fields | Source and units |
| --- | --- |
| Time, LapTime | Native stage clock in seconds; LapTime includes penalties in a verified final result, Time stays raw |
| TrackLength, LapDistance, CompletionRate | Native route length and projected route progress in metres, ratio 0–1; not the vehicle odometer |
| Throttle, Brake, HandBrake/Valid, Steering, Clutch | Drive+`0xe0` channels 0/1/2/4/5; negate steering for left-negative/right-positive; clutch includes auto-clutch |
| Gear, MaxGears | Drive+`0x128` and gear-table count +`0x534`, removing reverse/neutral from the maximum count |
| RPM, MaxRPM, IdleRPM | Drive +`0x134`/`0x448`/`0x44c`, radians/s converted to RPM |
| ShiftLightsFraction, ShiftLightsRPMStart/End/Valid | Car metadata +`0x15c` (`HudRedRpm`) is the flash point; ZTMZ adds its RBR/DR2-style 1000-RPM lead-in for green/yellow/red |
| Speed, PosX/Y/Z | Verified PhysX world velocity and actor pose; speed in km/h |
| G_lat, G_long | Drive local acceleration +`0x29c` and negated +`0x2a0`, divided by 9.80665; positive longitudinal acceleration and negative braking confirmed in captures |
| Four wheel speeds | Radius at `*(drive+0x30)+0x1c0+i*0x30`, angular speed at `*(drive+0x60)+0x10+i*4`, converted to km/h |
| Four suspension positions/speeds | `*(vehicle+0x88)+i*0xa0+0x5c` minus drive+`0x1e0+i*4`, in metres; drive+`0x200+i*4` in m/s |
| Four brake temperatures | Vehicle+`0x118c+i*0x24`, or 20 bytes earlier when the byte 12 bytes after that address is 1; degrees C |
| CarDamage | Native mechanical damage table: body, engine, gearbox, turbo, four brakes, four suspension units and two steering components |
| TimeStamp | Local capture time |
| CarPos, GameSpecificData | Race position is unverified and remains 0; no additional game-specific payload is fabricated |

Wheel order is FL, FR, RL, RR. The debug HUD at `0xf9d835` labels the analog
channels; `0xf9fcab`, `0xf9fdd2`, `0xf9fe77` and `0xf9ff5b` supply the wheel
calculations. Native `0x102a7b5` differentiates velocity and `0x102a8f8` rotates
acceleration into the car frame. Impacts can produce very large instantaneous G;
these are not smoothed into ordinary driving acceleration.

Native route length getter `0xf5cc30` follows race+`0x848`, indexed by timer+`0x70`
(count race+`0x844`, 16-byte entries), then entry+8 → +`0x38` → +`0x178` →
float+`0x8c`. Timer+`0x88` is current route progress. The verified single-lap
case requires race+`0x808` == 1. A paused capture reports 2684.9316 / 7112.941 m.
Pointer ownership is rechecked; unavailable optional data does not discard the clock.

The Fiesta's native `HudRedRpm` is 7500, mechanical maximum is approximately
8021 RPM and `HudMaxRpm` (tachometer scale) is 8500. HUD setup `0x3f966d` passes
the red RPM to `0x40ea00`, which stores red/max as its red-zone threshold;
`0x411a64` compares the displayed RPM fraction against it. ZTMZ uses the same
1000-RPM lead-in as RBR/DR2, with blank below 6500, green from 6500, yellow
from 6833.333…, solid red from 7166.666…, and flashing red from 7500 RPM for
this car. The lead-in is an application display convention, not additional
native LED telemetry. EA WRC instead supplies its own start/end range.
The shared KT helper now supplies a nonzero range for WRC 7/9/Generations: equal
endpoints previously sent the dashboard into its percentage fallback and skipped
solid red. Tests execute the actual shared Lua helper with CLR and Lua values
at every boundary. Native needle smoothing and cockpit animation are not copied.
Captured samples immediately below/above the flash point were 7489.579 / 7500.075 RPM.

Damage owner: participant+`0xc78`, vtable EXE+`0x1401488`, reciprocal vehicle at
+`0x40` and participant at +`0xf8`. Its bounded map uses head +`0xd0`, count
+`0xd8`, node part pointer +`0x28`. A part has name pointer +0, maximum life +8,
**lost life** +12, warning/critical remaining-health thresholds +16/+20.
Native `0x727540` accumulates lost life; `0x7287fc` derives severity from remaining
health. Captured body/engine loss reached 33.22% / 60.39%, then returned to zero
on restart. No tyre puncture was established; unsupported tyre/radiator indicators
remain unavailable, not healthy. Turbo and steering retain their named components
even where dashboard artwork has no dedicated indicator.

Damage announcements use the existing application setting, queue and cooldown:
engine warnings on native severity increases, collision warnings for a body-life
loss of at least 1% between advancing samples at most 500 ms apart. Collision
severity bands (1%/5%/15%) are an application policy, not native severity values.
Attachment, pause, missing data, repair, restart and owner changes establish a
fresh baseline. Existing damage is never announced merely because it was read.
Other mechanical damage is available to dashboards; there are no dedicated shared
voice tokens for gearbox, suspension, steering or turbo failures.

Regression fixtures contain only selected numeric telemetry and named damage
values. They exclude pointers, raw memory, game assets and unrelated process data.
`wrc9-vehicle-damage-20261016.json` preserves raw values for decoder checks;
`wrc9-damage-restart-20261016.json` preserves production damage/lifecycle edges
with adjacent samples and replays five new collision events after restart.

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
