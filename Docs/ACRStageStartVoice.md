# ACR stage welcome timing

The previous code emitted `RaceBegin` from the FMOD `EventCountdown` resource.
Core started `system_start_stage` on that transition, while the bridge immediately
played countdown 5. This accounts for the reported overlap; moving audio work to
another thread or delaying the countdown would not correct the trigger.

ACR now sends the welcome as a separate system cue when the verified native race
state first reaches `StartSequence` (3) or `PreSemaphore` (4), while the player is
in the loaded stage before the countdown. These phases count as a single entry.
Its `UseRaceBeginStartSound` policy is false. FMOD remains the
source of countdown and GO; preparation, race states, finish and retirement
mapping are unchanged. Other games retain their existing welcome policy.
The live welcome honors the existing start/end-sound setting and external
pacenote playback control.

## Evidence and reader

The 2026-10-14 capture documented in `ACRRetirementInvestigation.md` observed
`PreSemaphore (4) -> Semaphore (5) -> Running (6)` in this executable:
`42C359B0CD24774A86441710FDC7D7DB3BC06B57B3FF30CB9EA5D56FB71448FB`.
On 2026-10-20, both the diagnostic and production C# readers reported
`StartSequence` (3), RaceId 0, while the user confirmed being on track with the
stage HUD visible. This establishes the earlier welcome point for this build.
The production sampler ports that capture's current-build reflected layout,
local player / controller ownership, non-replay RallyManager vtable, race-state
owner and pointer/phase stability checks. It uses the existing asynchronously
hash-verified read-only process handle, at 10 Hz, independently of vehicle data.
It does not inject another hook or infer a start from shared-memory status zero.

The welcome is handled once per loaded session/track. Session identity uses the
native world, controller and owned race-state objects. Track identity uses the
existing shared-memory `TrackName` and `TrackLayoutConfiguration` fields, so a
different track or layout can welcome even when those native objects are reused.
Repeated waiting samples, pauses,
unavailable reads and restarting the current time trial do not repeat it.
The recorded restart increments RaceId from 0 to 1 while retaining all three
objects; RaceId is an attempt number, not evidence of entering a new stage.
Joining during countdown or driving also marks the welcome handled, so it is
not deferred until the next restart. A changed session identity or reader reset
allows a welcome again. A changed nonempty track identity also allows it, unless
the same native attempt already received a welcome (metadata can arrive after
the session change). Missing metadata preserves the last known track; initial
metadata merely establishes that identity without replaying an existing welcome.
Track changes first seen after countdown are not deferred until a restart.

The user deferred live track-switch testing. Track/layout changes, including
delayed metadata, are covered by synthetic tests, not a captured multi-track run.
If two routes report identical names/layouts and reuse all three native objects,
or metadata arrives only after countdown, this policy cannot identify their
entry in time. The diagnostic now records track/layout alongside native state
so a later live check can verify these assumptions.

When native state is unavailable (including an unverified executable or the
current Proton relay, which does not carry this field), the welcome is skipped.
It is not deferred to countdown. Both Windows WPF and Windows Avalonia use the
same reader; no new native binary is required.

## Verification

`AcrStageEntryTests` covers the captured phase sequence, late joins, repeated
samples, unavailable reads, restart transitions, changed identities, settings,
the unchanged policy for other games, invalid memory, ownership/replay rejection
and scene/phase changes during a read. These are not a new live listening test.

For a read-only production-reader capture while ACR is running:

```powershell
dotnet run --project tools/ACRTelemetryProbe -- --watch-stage-entry tmp/acr-stage-entry.jsonl
```

The file must not already exist. Enter a stage, wait before triggering the
countdown, drive, then restart and wait again. `welcomeToken` should occur once
at initial entry and not after that restart. Then leave and load another stage
to check that its new session welcomes once. This probe opens neither the voice pipe nor an
audio device; end-to-end listening still requires the newly built application.

## Live validation, 2026-10-20

The user confirmed the stage HUD at phase 3, then triggered countdown, drove and
restarted. The original C# production-reader trace contains exactly two welcome
cues: race 0 at phase 3 and race 1 at phase 3 after restart. No welcome was emitted
at phases 4, 5 or 6, nor during restart preparation (0, 1, 2).
The independent native recorder agrees with the phase transitions. Local raw
traces are `tmp/acr-stage-entry-native-20261020.jsonl` and
`tmp/acr-stage-entry-production-final-20261020.jsonl`; both recorders were stopped
after verification. This was trigger verification, not playback through the
still-running installed application.

The user subsequently reported the restart welcome as unwanted. The revised
policy suppresses it. This correction has been tested against the saved sequence,
not yet through a new live listening run.

`TestData/acr-stage-entry-restart-20261020.jsonl` retains the observed phase/race
sequence, including a repeated waiting sample, with timestamps relative to the
first valid sample and no process addresses. `observedWelcome` preserves the
original two cues; `expectedWelcome` records the corrected one-cue behavior.
The portable tests replay it.
