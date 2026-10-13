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
| RBR | Existing detector publishes Normal only after observed stage completion outside replay and without false-start state. Other end states have no verified reason and now stay silent. | `RaceEnded` and end/replay game states alone cannot distinguish completion, retirement and replay transitions. No crash/retire reason is invented from damage or incomplete progress. |
| DiRT Rally | Existing lap-completion event publishes Normal. | No explicit retirement reason in the current reader. |
| DiRT Rally 2.0 | Existing end transition with positive LastLapTime publishes Normal. | No explicit retirement reason in the current UDP reader. This change does not claim to validate every terminal-damage transition or replace its finish detector. |
| BeamNG.drive | The bridge's stage completion flag publishes Normal. Inactive stage alone does not emit a completed result. | Current protocol has no explicit retirement/terminal-damage result. No new Lua fields or bridge-version changes are needed for the shared playback fix. |
| WRC 7 | Verified result data publishes Normal. | No verified retirement reason in the current integration. |
| WRC Generations | Validated result time publishes Normal; pause/reset are not used as finish evidence. | No verified retirement reason in the current integration. |

The shared playback fix covers every provider; **automatic retirement detection is not complete for every game**. Extending those providers requires a verified game result signal and lifecycle capture, not a damage percentage heuristic.

## Regression coverage

`RaceEndPlaybackTests` covers the shared result/audio and replay-time rules. `WrcRaceResultTests` feeds serialized UDP packets through the actual reader, including reset clocks, full progress, duplicate packets and unknown status. `AcrRaceResultTests` exercises the bridge message queue for finish and all existing disqualification cues. Existing DR2, WRC 7 and WRC Generations lifecycle tests retain normal-finish behavior; RBR and BeamNG tests cover incomplete/active stages versus verified completion.

The separate issue about speech continuing after exiting a game is deliberately deferred; playback queue shutdown is outside this change.
