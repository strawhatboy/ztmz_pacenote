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

Repeated waiting samples, pause at the start and unavailable reads do not repeat
the welcome. A new race identity or an observed return to the entry phases after
another phase permits it again. Joining during countdown or driving does not
play a late welcome. If a reset happens entirely between samples and reuses all
identities, this observer cannot claim to detect it.

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
countdown, drive, then restart and wait again. `welcomeToken` should occur only
on the waiting-state entries. This probe opens neither the voice pipe nor an
audio device; end-to-end listening still requires the newly built application.

## Live validation, 2026-10-20

The user confirmed the stage HUD at phase 3, then triggered countdown, drove and
restarted. The final C# production-reader trace contains exactly two welcome
cues: race 0 at phase 3 and race 1 at phase 3 after restart. No welcome was emitted
at phases 4, 5 or 6, nor during restart preparation (0, 1, 2).
The independent native recorder agrees with the phase transitions. Local raw
traces are `tmp/acr-stage-entry-native-20261020.jsonl` and
`tmp/acr-stage-entry-production-final-20261020.jsonl`; both recorders were stopped
after verification. This was trigger verification, not playback through the
still-running installed application.

`TestData/acr-stage-entry-restart-20261020.jsonl` retains the observed phase/race
sequence, including a repeated waiting sample, with timestamps relative to the
first valid sample and no process addresses. Its welcome expectations come from
the user-confirmed stage-entry/restart actions. The portable tests replay it.
