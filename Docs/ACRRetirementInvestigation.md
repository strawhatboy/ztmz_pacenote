# ACR manual retirement investigation

## Status: deferred at the user's request

On 2026-10-14 the user chose to defer further investigation for ACR 0.6.
No production game-state mapping was changed. Existing FMOD disqualification
handling remains; manual retirement is **not** claimed as supported. The
external shared-memory and native-phase capture processes have been stopped.
The development FMOD observer already loaded in ACR remains until the game
exits normally; do not start another FMOD bridge hook in that same process.

The available evidence cannot establish whether the missing signal is a game
limitation, a mode-specific exit path, or a transient state missed by polling.
Do not describe this as a confirmed ACR defect or infer DNF from cleared state.
Resume with mode-labelled result/restart captures on the then-current build.

## Capture prepared on 2026-10-14

The current production bridge maps `EventDisqualified`, `EventDisqualified1`
and `EventDisqualified2` to typed Disqualified and the shared `system_retired`
voice. This does not establish which event, if any, a menu abandonment emits.
Compare menu abandonment with Restart before extending production detection.

The running process is `acr.exe` PID 12872, installed under
`C:\SteamLibrary\steamapps\common\Assetto Corsa Rally`. Its executable and
`fmodstudio.dll` hashes match the tested manifest:

- EXE: `42C359B0CD24774A86441710FDC7D7DB3BC06B57B3FF30CB9EA5D56FB71448FB`
- FMOD: `3132A935C312FB6FFFC8FEB71AF070837341EFF95EF12F105E7E7D6A4D0993B0`

ZTMZ and the packaged live bridge were not running. The existing development
sound-info observer was compiled and loaded into this verified process. It
records all programmer-sound lookup names, leaves the original call/result
unchanged, and does not consume ZTMZ's named pipe. Startup was confirmed by
`pid=12872 tickMs=3164531 | sound=trace-started: FMOD System::getSoundInfo`.
The added timestamp is Windows uptime, matching `Environment.TickCount64` in
the shared-memory probe. This is instrumentation inside the game, not a purely
external read-only process reader. It remains loaded until the game exits.

Read-only shared-memory capture runs separately via:

```powershell
dotnet tmp/wrcg-verification/bin/ACRTelemetryProbe/release/ACRTelemetryProbe.dll --watch-session 12872 tmp/acr-retirement-session-20261014.jsonl
```

It records full decoded graphics fields and selected physics/static values,
immediately flushing changed selected status fields and 250 ms snapshots,
polling every 50 ms. It does not open a pipe or inject a bridge. No short
timeout is used; game exit or Ctrl+C stops the capture. The output must be a
new file. Raw logs remain ignored local artifacts.

Initial observation: track `威尔士 Afon Bidno`, clock `00:00.000`, integer
time zero, status zero, flag zero and penalty type zero while the user reports
being on the track. Do not interpret status zero as proof of a menu/retirement.

## First observed exit (mode subsequently clarified as free practice)

The user initially reported: `okay, retired (exit current championship?)` and
described the destination as a retirement/results screen. Later they clarified
that this was **自由练习 (free practice)** and the menu action was **结束赛季**.
Preserve that exact wording; do not relabel it as a verified championship DNF.
The initial destination description does not establish an internal failure flag.

The observer recorded seven real sound lookups after its startup marker:

| Windows uptime (ms) | Sound lookup (EN_0/PN_ prefix omitted) |
| --- | --- |
| 3374046 | EventCountdown_Any_Any_3 |
| 3379031 | EventCountdownGo_Any_Any_3 |
| 3379484 | Dist70_Any_Start_2 |
| 3380265 | OverCrest_Any_End_3 |
| 3388578 | Into_Any_Start_1 |
| 3389250 | Right5-Long_Any_Mid_2 |
| 3390093 | Tighten3_Any_End_1 |

There was no subsequent `EventDisqualified` variant or `StopAtMarshals` lookup
through the inspection at approximately uptime 3451390. Countdown and actual
pacenotes demonstrate that this observer was receiving events during the attempt;
absence of a result lookup is limited to this observed action/build/voice path.
It does not prove the game has no internal retirement state or that other exit
paths use the same behavior.

Shared memory advanced its formatted clock while driving (speed reached about
47 km/h), then stopped at `00:11.596`, packet 96032, speed zero at uptime
3390656. At uptime 3402515 it advanced once to `00:11.601`, packet 96033, then
remained there. Throughout the captured action these fields stayed zero:
`CurrentStatus`, `SessionType`, `CurrentFlag`, `PenaltyType`, `ChequeredFlagGlobal`,
`RedFlagGlobal`, `LapIsValid`, `SetupMenuVisible`, and `LapsCompleted`.
The integer millisecond clock and session clock are not useful lifecycle evidence
in this capture either. No causal meaning is assigned to either stopping point;
they may include the pause/confirmation flow as well as the actual exit.

Current conclusion: the existing disqualification voice event does not cover
this observed free-practice exit. A stopped/frozen shared-memory packet cannot
safely distinguish pause, unload, restart or retirement. Do not map it to Retired
or Normal. The completed restart comparison below also lacks an explicit ending
event. Investigation therefore needs
an independently verified internal stage-result or menu-action signal; the older
ProcessEvent trace offsets must be revalidated before any use on this build.

No production ACR result mapping has been changed. The telemetry probe and
native observer both build successfully. No end-to-end ZTMZ playback test was
performed because the application is not running during this standalone capture.

## Observed restart comparison

The user confirmed the requested drive/restart sequence. At uptime 3652906 the
observer saw `EventCountdown`, then `EventCountdownGo` at 3657906, followed by
normal distance/corner calls through 3664796. No disqualification or finish cue
was emitted. Shared-memory time paused at `00:03.710`, packet 104182; it advanced
once to `00:03.715`, packet 104183, then reset to `00:00.000` at uptime 3665234,
packet 104222. Packets continued advancing at the new start line. Status,
flag and penalty fields remained zero as in the earlier abandonment capture.

Thus the observed restart resets the clock and resumes telemetry, whereas the
observed retirement/results screen retains a stopped packet. The stopped packet
alone still cannot distinguish retirement from pause or a telemetry stall.
No production heuristic was added. The shared-memory recorder was stopped after
this comparison; the sound observer remains loaded until ACR exits.

## Internal-state leads (not yet production validated)

The current verified executable contains `ClientRetireRace` at string RVA
`0x9dcb698`, `bDNF` at `0x9dcb518`, and `bDQ` at `0x9dcb53c`, as well as
`RaceParticipantRallyResult` / `RaceStateData` / `ERacePhase` reflection data.
These are leads for inspecting the native result or lifecycle independently of
the voice event; string presence does not establish a runtime detector.

The current executable's enum registration table at RVA `0x9dc96f0` was also
read directly: 16-byte name/value entries establish `Running=6`, `Ended=7`,
`Resetting=10`, `Retire=11`, `Disqualify=12` (and the intervening lifecycle phases).
These are game-native enum values, not ZTMZ's enum. This confirms the definitions
exist in the supported build, but not which phases a particular menu action
actually traverses or how long a phase remains observable.

Existing generated headers are available under
`C:\Dumper-7\5.4.3-0+UE5-acr\CppSDK\SDK\acr_{classes,structs,parameters}.hpp`.
They were generated on 2026-08-06 and must not be treated as current offsets.
The old SDK describes RaceStateData, RaceManager and a local-player result getter,
plus separate Retire/Disqualify phases and DNF/DQ fields.

`read-memory.py --race` now inspects bounded local-controller component arrays,
checks the current executable hash, and prints class names, ownership and
candidate race fields using read-only access. In the post-restart live process
it found the owned RallyManager (vtable RVA `0x9dd8c98`) but the old SDK's
RaceStateData pointer at `+0x1e8` was null. Current reflected offsets must be
checked before relying on that chain. Do not inject the old ProcessEvent hook
using its August function addresses.
The current RaceStateData class registration at RVA `0x6201200` specifies size
`0x148`, whereas the August SDK describes size `0x130`, providing concrete
evidence that the old layout cannot simply be copied.

## Current native layout and phase recorder

Current executable reflection metadata resolves the layout below. These offsets
are specific to the executable hash above, not guessed from the August SDK.

| Field | Current offset | Reflection property record RVA |
| --- | --- | --- |
| RaceManager.RaceStateData | `+0x200` | `0x9dccaa0` |
| RaceStateData.RaceId | `+0x110` | `0x9dbd758` |
| RaceStateData.RaceTime | `+0x118` | `0x9dbd7a8` |
| RaceStateData.RaceStartTime | `+0x11c` | `0x9dbd7e0` |
| RaceStateData.Phase | `+0x120` | `0x9dbd850` |

The owned local RallyManager's vtable is RVA `0x9dd8c98`; RaceStateData's
vtable is `0x9dd5da0`. The controller's `+0x2b0` pointer equals the state's
owner at `+0xa0` in the live capture (PlayerState ownership). The manager owner
at `+0xa0` equals the local controller. RallyManagerReplay has a different
vtable (`0x9dd94f8`) and is excluded. At the post-restart start line, RaceId
is 2, RaceTime/StartTime are zero, and Phase is 4 (`PreSemaphore`).

`tools/ACRTelemetryProbe/capture-race.py` verifies the executable hash, resolves
the local-player chain afresh on every sample, selects only an owned non-replay
RallyManager, and checks state ownership/type. It rechecks pointers, arrays,
RaceId and Phase after reading to reject a sample spanning scene replacement.
No pawn is required: a result may outlive the vehicle. The recorder polls every
20 ms, flushes phase/identity/availability changes immediately and emits a
one-second heartbeat. It records native values without mapping them to a ZTMZ
result. Lifetime is game exit or Ctrl+C, with an exclusive new output file.

```powershell
python tools/ACRTelemetryProbe/capture-race.py 12872 --output tmp/acr-native-race-20261014.jsonl
python -m unittest discover -s tools/ACRTelemetryProbe -p test_capture_race.py
```

The four diagnostic tests cover raw phases, foreign owners/replay exclusion,
invalid arrays/player counts/phases/clocks, and world/phase changes during a
read. Live recorder readiness was confirmed at uptime 5233390, phase 4.
Retirement/restart native transitions still need capture before production use.

## Native capture of free-practice exit

The user performed another drive/exit after the native recorder was ready and
confirmed completion. The stable, owner-checked samples show:

| Uptime ms | RaceId | Native Phase | RaceTime seconds |
| --- | --- | --- | --- |
| 5233390 | 2 | 4 PreSemaphore | 0 |
| 5268937 | 2 | 5 Semaphore | 0 |
| 5274062 | 2 | 6 Running | 0.010714 |
| 5281062 | -1 | 0 None | 3.355953 |

No `Retire=11` or `Disqualify=12` was captured at 20 ms polling. This does not
exclude a shorter transient phase. It does establish that polling this phase
alone has not proven a reliable retirement detector for the user's operation.
Do not equate Running→None or RaceId=-1 with DNF. The user subsequently clarified
the mode/action as free practice / 结束赛季. They navigated to other scenes
after this capture; subsequent phase/world changes are not annotated restart
or retirement comparisons and must not be used as such.

Current-build result getter inspection provides a further read-only path:

- `GetLocalPlayerParticipantRallyResults` native exec RVA `0x6366d50`:
  local controller `+0x2b0` is PlayerState; getter `0x61e0ae0` supplies its
  participant FName at `+0x368`.
- `GetParticipantCurrentRallyResult` exec `0x63693d0` reads results with stride
  **0x30**, DNF at `+0x28`, DQ at `+0x29`. The old SDK's 0x80 result size is
  obsolete; do not copy it.
- `0x634f680` resolves world GameState (`+0x1b0`), finds
  `RaceEventManagerComponent`, and reads its event actor pointer at `+0xd0`.
- The event actor's `RaceEventRallyResultsComponent` supplies participant entries
  at `+0xe8`, stride `0x18`; helper `0x6228a00` matches the participant FName and
  returns the result array at entry `+8`.
- Native `GetComponentByClass` (`0x39e8a60`) walks the owned sparse component set
  at actor `+0x230` (16-byte elements with allocation bits). Instance/blueprint
  arrays alone omit native components, including RaceEventManagerComponent.
- Main-menu RaceEventManagerComponent has vtable RVA `0x9ddd0e0`; its event
  pointer was null, as expected for no current event. This is not a result sample.

The diagnostic inspector now walks the bounded allocated owned-component set
for this investigation. Result fields still need a live owned event/result and
comparison capture before a production mapping can be added.

An experimental `sample_results` path was added to `capture-race.py` after the
phase-only recorder started. That running process did **not** reload the script,
so `tmp/acr-native-race-20261014.jsonl` contains phase samples only, not DNF/DQ
samples. The result path follows the statically identified getters and vtables
but has not yet been validated against a live non-null event/result object.
The existing four tests validate the phase sampler only. Keep this distinction
when resuming; do not treat the new result reader as completed production support.
