# Race-end voice coverage

Shared by WPF and Avalonia through `ZTMZPacenoteTool` and `RaceEndPlaybackPolicy`.
Related issue: https://gitee.com/ztmz/ztmz_pacenote/issues/I5X2KQ

## Playback contract

- A typed `GameStateRaceEnd.Normal` plays `system_end_stage`.
- `TimeOut`, `Crashed`, `Retired` and `Disqualified` play the existing `system_retired` token.
- Missing, invalid or unknown results play neither ending voice. Damage values, a frozen timer, missing telemetry or returning to a menu do not establish retirement.
- Existing external-audio and start/end-sound settings still gate playback. End events after Racing, Paused or AdHocRaceBegin are handled; repeated RaceEnd -> RaceEnd events do not repeat the voice.
- Saving a completed replay also requires a finite positive official finish time. A normal finish cue without a result time may still play its voice, but cannot create a completed replay.

## Provider audit

| Game | Current evidence and behavior | Remaining limitation |
| --- | --- | --- |
| EA SPORTS WRC | Explicit `stage_result_status` maps to all five shared result values. Result status takes precedence over zeroed clocks and progress, and all emitted values use the shared enum. | Validate normal finish, terminal damage and retirement with live telemetry after the change. |
| Assetto Corsa Rally | Bridge `StopAtMarshals` marks normal finish; `EventDisqualified`, `EventDisqualified1` and `EventDisqualified2` retain disqualification/retired audio. | Other retirement causes have no verified bridge cue. Shared-memory replay status alone has no finish reason and stays silent. Finish audio supplies no official result time. |
| RBR | Hash-gated Windows native reader identifies an active failed result for an observed started RSF stage; emits Retired without a finish time. Practice, Hotlap and online manual-retirement signals were captured. Existing Normal completion remains unchanged. | Waits at most 2 seconds for delayed results; unknown layouts/results, restart and replay stay silent. No specific crash reason inferred. Final audible behavior in a rebuilt app still needs live validation. |
| DiRT Rally | Existing lap-completion event publishes Normal. | No explicit retirement reason in the current reader. |
| DiRT Rally 2.0 | Hash-gated Windows memory reader observes the local player's native Retired, TerminalDamage, Disqualified and TimedOut results; polling continues when UDP stops. Explicit manual retirement was live captured. Normal finish retains UDP timing. | Linux/unknown executable builds have no native result reader. Terminal damage, timeout and disqualification still need live validation. See `dr2-telemetry.md`; damage/silence alone never imply retirement. |
| BeamNG.drive | Bridge 1.1.11 publishes Retired for an explicit matching mission abandon; Rally Loop DNF attempts distinguish abandon from restart. Stage completion still publishes Normal. Restart and other mission stops end silently. | Requires the updated bridge and a game restart. Damage, pauses and missing telemetry are not retirement evidence. Flows without a verified abandon event remain silent. Live validation is still required. |
| WRC 7 | Verified result data publishes Normal. | No verified retirement reason in the current integration. |
| WRC Generations | Validated result time publishes Normal. Hash-gated timer/profile failure flags publish Retired for an observed started attempt, including when UDP stops. Manual abandonment and restart were captured in mode 2/submode 1. | Mode 5 uses unverified player selection and stays excluded. Other modes/causes and final audible behavior still need live validation; unknown results stay silent. |

The shared playback fix covers every provider; **automatic retirement detection is not complete for every game**. Extending those providers requires a verified game result signal and lifecycle capture, not a damage percentage heuristic.

## Regression coverage

`RaceEndPlaybackTests` covers the shared result/audio and replay-time rules. `WrcRaceResultTests` feeds serialized UDP packets through the actual reader, including reset clocks, full progress, duplicate packets and unknown status. `AcrRaceResultTests` exercises the bridge message queue for finish and all existing disqualification cues. Existing DR2, WRC 7 and WRC Generations lifecycle tests retain normal-finish behavior; RBR and BeamNG tests cover incomplete/active stages versus verified completion.

The separate issue about speech continuing after exiting a game is deliberately deferred; playback queue shutdown is outside this change.

## BeamNG 1.1.11 lifecycle evidence and implementation

Inspected the installed game Lua under `lua/ge/extensions` on 2026-10-13:

- `gameplay/missions/missionManager.lua`: `taskStopMissionStep` calls `onAnyMissionWillChange("stopped", mission, abandoned)` before mission teardown. Only an explicit true `abandoned` flag establishes retirement. `onMissionAbandoned` alone is **not** used: the function that emits it also serves stops without that flag.
- `gameplay/rallyLoop.lua`: its `onAnyMissionWillChange` saves a DNF for abandonment, excluding `getCurrentMissionId() == "serviceOut"`.
- `gameplay/rally/loop/rallyAttempts.lua`: DNF records contain `dnf = true`, `data.dnfType` (`abandon` or `restart`) and `data.dnfMissionId`. `gameplay/missions/progress.lua` exposes these through `onMissionAttemptAggregated(attempt, mission, progressKey)`.
- `gameplay/rally/rallyManager.lua`: `getActiveStageData().isComplete` / observer `stageComplete` remain the normal-finish evidence. A completed stage remains normal when leaving its results screen.

The bridge observes these hooks without wrapping or modifying game functions. Mission identity must match the current Rally stage, or its Rally Loop parent whose current stage matches; unrelated missions and `serviceOut` are excluded. Loop attempts must additionally identify the current stage in `dnfMissionId`.

Protocol version 1 gains optional `stage.result`: `finished`, `retired`, `restart`, or `interrupted`. Older bridges still work through `isFinished`; older clients ignore the extra field and therefore cannot announce retirement. New clients emit a typed retirement without a finish time; restart/interruption/unknown results emit RaceEnd without a result or finish time, so neither ending audio nor completed replay is generated. Completion takes precedence when already confirmed.

The Lua bridge retains a terminal stage for subsequent periodic packets, suppresses its live pacenote, and resets on manager replacement, the matching mission starting, or a fresh countdown. Repeated results do not emit another RaceEnd. Vehicle telemetry remains independent. No new UI setting is needed; the existing bridge version check offers the update.

### Automated and live validation

`BeamNGRaceEndTest` executes the shipped Lua with mocked game hooks and UDP, and feeds serialized protocol messages to the actual C# reader. It covers single-stage abandon, loop abandon/restart, unrelated missions/stages, service-out exclusion, finished-stage exit, duplicate results, periodic terminal snapshots, countdown restart and manager reload. Existing BeamNG protocol/prerequisite tests also pass (51 BeamNG cases total).

Still test in BeamNG after updating the installed mod to **1.1.11** and restarting the game:

1. In a standalone SS, drive past the start and abandon: one retirement call, no congratulations or completed replay.
2. Repeat in a Rally Loop SS; then restart the loop/stage: no retirement call for restart, and countdown/live calls should resume for the new attempt.
3. Finish normally, then leave the results screen: one normal finish call, no subsequent retirement call.
4. Exit `serviceOut`, pause/resume, recover/reset the car, and damage it without explicitly abandoning: no invented retirement call.

These source-backed hooks are not proof that every game UI path uses them. Flowgraph missions can handle `onRequestAbandon` themselves; if they never publish the verified flag or DNF record, the bridge deliberately leaves the reason unknown. Do not infer a crash/retirement from a disabled engine or stalled vehicle. Capture live events before extending those paths.

## RBR native follow-up (2026-10-13; investigation, not implementation)

Analyzed a workspace copy of the existing `RichardBurnsRally_SSE.exe.idb`;
the original database and game were not modified. Addresses below are absolute
VAs for image base `0x400000`, not RVAs. The local executable's SHA-256 is
`5BB1B0C8F6719FAECAD63B0AAFD7541089D1D1FF7A4C9BB68ED49BB53DC6F63B`.
Some database calls point outside the original image, so this database must not
be treated as proof of an unmodified executable or universal plugin behavior.
The follow-up restored executable code from disk into a separate analysis copy
and invalidated cached decompilations. See [RBRRaceResultInvestigation.md](RBRRaceResultInvestigation.md)
for the corrected native paths, result-menu identity and RSF callback storage.

Confirmed control-flow evidence:

- `0x4e8770` emits `CSigRetireCar`. Gameflow registers receiver `0x5662c0`
  in `0x569270`. The receiver switches on reasons 0..4, but these numeric
  reasons have **not** been assigned production enum names.
- Reasons 3 and 4 call `0x577550` and `0x573950`, respectively. These write
  countdown values at `*(0x165fc68)+0x27c` and `+0x280`; they are not durable
  retirement-result fields. The bystander/off-stage message callbacks
  `0x5693c0` / `0x5797e0` emit `CSigEndRace` with a nonzero member at `+0x10`.
- `0x477920` emits `CSigEndRace`. Application initialization `0x481530`
  registers handler `0x480810`. That handler tests the signal's member `+0x10`
  and chooses different completion versus unsuccessful-end paths. This is an
  event argument, not a global flag that can simply be polled.
- For gameflow modes 0/1, the unsuccessful path selects result UI 18; normal
  completion selects UI 16. Other modes select different UIs. The original
  mode-7 call is `0x5693f0`; the external target in the older database was a
  runtime patch, not the original call. Mode 7 must not be assumed to mean
  plugin racing. `0x47f3a0` changes
  the application state to **9 in both cases** and sets the public
  `raceFinished` flag. UI IDs passed to that function are **not** the
  `RBRGameState` values read at `*(0x7eac48)+0x728`.
- The public API describes `raceFinished` at race-data `+0x144` as shared by
  finished, retired and not-yet-started states. It cannot establish retirement.

Rejected lead: getter `0x47ca00` returns application `+0x70`. Gameflow's
`0x5663e0` saves its first member when a race ends, but consumers and setter
`0x487680` establish that this is **camera state**, not a race result. Do not
interpret its values 1/3/4/5/7 as retirement causes.

Result-menu pointers and RSF's saved `HandleResults` values have now been traced.
Neither is an unconditional retirement flag: menu transitions can be short, and
RSF's finish-time value needs proof of a fresh result for this attempt. The
linked investigation specifies the required guards and unresolved validation.
RBR's unknown-end behavior remains silent. The subsequent bounded production
implementation is documented at the end of the linked investigation.
The standalone `tools/RbrResultProbe` now captures native result-page ownership,
transitions and hash-verified RSF saved results without injection or writes.
Its build, one live RSF manual retirement and a restart were checked: failure page 18
appeared about 126 ms after the first sampled end state and remained as a stale
pointer after returning to the main menu. RSF result times stayed zero throughout.
The restart instead used transition 3 and loading states, without the observed
failure-page sequence; RSF's stage-start flag stayed set through the new start.
On 2026-10-14, a separate Hotlap manual retirement also used native mode 1 and
failure page 18, appearing 94 ms after the first sampled end state before
automatically returning to the Hotlap menu. A subsequent online retirement in
the same process also used native mode 1 and failure page 18, appearing 155 ms
after the first sampled end state. All three modes now have one observed manual
retirement; this does not establish crash-specific causes or every plugin flow.
The detailed document includes these sequences and remaining normal-finish,
pre-start-exit, replay and repeated-failure validation. The production detector
now uses native mode/page/ownership plus the observed attempt, with bounded
waiting for a delayed result and one typed retirement event. It does not rely
on RSF's saved result floats. 33 focused regression tests pass; final live audio
verification requires a rebuilt app. WPF and Windows Avalonia share this module.

## WRC 7 / Generations follow-up prerequisites

Follow-up scope agreed on 2026-10-14: do not add a separate RBR terminal-damage
detector. The user's RBR flow still requires manually choosing Retire after
terminal damage; use the existing verified manual-retirement path. WRC
Generations retirement is the next investigation target. Its timer `+0x9f`
flag is not yet a verified retirement reason: trace its writers and compare
manual retirement, restart and normal finish before changing production behavior.

The current workspace readers provide investigation entry points, not verified
retirement enums:

- WRC 7 reads three bytes at RaceStats `+0x8c8` and 14 bytes at profile
  `+0x154`. Normal-result validation requires `+0x8c8 == 1`, `+0x8ca == 0`,
  profile `+0x160 == 1` and `+0x161 == 0`. Inspect writers/consumers of the
  rejected flags before interpreting them as retirement. Known routines are
  CrossEndLine RVA `0x2ca9c0` and ProcessRaceStats RVA `0x2900b0`.
- WRC Generations rejects timer `+0x9f != 0` for timing reads. Inspect that
  flag's writers and ProcessRaceStats RVA `0x814680`, together with profile
  result fields `+0x19c/+0x1a0/+0x1a4`. Rejected timing alone does not justify
  a retirement event. Native result polling must also work when UDP stops.

During the earlier search, the previous `L:\SteamLibrary\steamapps\common\WRC 7`
directory no longer contains `WRC7.exe`; no current WRC Generations executable
was located in the checked game-library paths. Obtain their current install
paths and verify executable hashes before continuing native analysis. On
2026-10-14, WRC Generations was located in the N: Steam library and its hash
matched the supported build. The Giveup-to-timer-to-profile flag chain and live
manual-abandon/restart captures are documented in `WrcGenerationsLifecycle.md`.
The bounded production reader now uses those failure flags for the observed
attempt; no specific damage cause is inferred. Normal-result handling additionally
rejects a profile explicitly marked failed. Mode 5 and unknown builds are excluded.
