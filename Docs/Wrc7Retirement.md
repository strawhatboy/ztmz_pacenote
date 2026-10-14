# WRC 7 native retirement detection

## Verified build and evidence (2026-10-14)

Executable: `N:\SteamLibrary\steamapps\common\WRC 7\WRC7.exe`.
SHA-256: `0FEE0ED14AD05D76FD44B2BA1215BACAFC806DD9D7E8B313634A99C105903633`.
All addresses below are RVAs or object-relative offsets, not fixed process addresses.
The existing production hash gate remains required.

- Manager global `0x1091b80`, expected vtable `0xdb1b60`.
- Single-player profile vector `manager+0x17798..0x177a0`, stride `0x170`.
  Multiple profiles remain unsupported. Participant vector at `+0xb0..0xb8`;
  selected vehicle owns RaceStats at `vehicle+0x38`, with reciprocal pointer
  `stats+0x908`. Production rereads roots and result records to reject torn reads.
- The Giveup path around `0x14fc00` obtains RaceStats through `0x291cf0`
  (or the alternate player getter `0x291da0`), then writes `stats+0x8ca=1`
  at `0x14fe16` and `stats+0x8c8=1` at `0x14fe1d`.
- `ProcessRaceStats`, `0x2900b0`, marks profile `+0x160=1` at `0x2901c4`
  and copies `stats+0x8ca` to profile `+0x161` at `0x2901cb..0x2901d2`.
- The existing normal-finish path, `CrossEndLine` at `0x2ca9c0`, sets the
  ended flag and clears the failed flag. Normal results still require matching
  positive raw time, penalty and processed profile totals.

The user reported a manual abandonment in the running game. Subsequent read-only
samples found stats flags `[1,0,1]`, profile flags `[1,1]`, and zero raw, penalty
and result times. The compiled production decoder independently read the same
failure on `Luceram - Col St Roch`, Hyundai i20 Coupe WRC.

**Capture limitation:** the first 900-second capture expired before the user's
action. It contains pre-start time `-1` and clear result flags only. The follow-up
capture contains the post-abandonment state, not the full start-to-abandonment
transition. Do not describe this as an end-to-end retirement capture. Files are
ignored local diagnostics: `tmp/wrc7-retirement-20261014.jsonl` and
`tmp/wrc7-retirement-followup.jsonl`.

The user subsequently reported restarting. The 1800-second follow-up had also
expired before that action: all 1790 available samples still showed the old
failed result. A fresh read after restart found time `-1`, stats `[0,0,0]` and
profile `[0,0]`, **with the same manager, profile, vehicle and stats addresses**.
Thus cleared flags and the clock reset, not pointer replacement, are required
to recognize a new attempt. This verifies the post-restart state only, not the
absence of a transient failure during restart. The existing restart regression
test covers address/identity reuse and reset of a latched retirement.

The subsequent continuous capture **did record both complete comparisons**.
`tmp/wrc7-restart-continuous.jsonl` contains two in-race restarts and two
manual abandonments, with the same profile/vehicle/stats addresses throughout:

| Capture elapsed (ms) | Observed transition | Raw time (ms) | Stats flags | Profile flags |
| --- | --- | --- | --- | --- |
| 211297 | First observed running sample | 975 | `[0,0,0]` | `[0,0]` |
| 214312 | Restart after time 1336 | -1 | `[0,0,0]` | `[0,0]` |
| 227406 | Running again | 218 | `[0,0,0]` | `[0,0]` |
| 231094 | Abandon after time 1242 | 0 | `[1,0,1]` | `[1,1]` |
| 264297 | New attempt after the failed result | -1 | `[0,0,0]` | `[0,0]` |
| 276344 | Running again | 434 | `[0,0,0]` | `[0,0]` |
| 280359 | Restart after time 1895 | -1 | `[0,0,0]` | `[0,0]` |
| 288406 | Running again | 38 | `[0,0,0]` | `[0,0]` |
| 293812 | Abandon after time 3332 | 0 | `[1,0,1]` | `[1,1]` |

No failed-result flags were observed during either restart at the probe's
20 ms sampling cadence. Changed flags/owners are logged immediately; unchanged
flags retain one-second clock snapshots. This is sampled evidence, not proof
that arbitrarily short transitions can never occur in any game mode.

All 92 original samples from elapsed 210281 through 299844 are preserved in
`src/ZTMZ.PacenoteTool.Portable.Tests/TestData/wrc7-restart-retire-20261014.jsonl`.
The regression test feeds them through the production flag decoder and session
state machine: exactly two retirement transitions, no normal finish, no retirement
on either restart, no duplicate result announcement and no completed replay.
It also verifies that a fresh attempt after a retired result clears the latch.

The probe was stopped after capturing the completed comparison; the game and
running Pacenote Tool were left running. For future interactive tests,
`--seconds 0` captures until the game exits or the probe is interrupted.

## Production behavior

`Wrc7MemorySession` supplies typed native flags alongside telemetry to
`Wrc7RaceSession`; it does not inject anything or depend on incoming UDP.
`Wrc7GameDataReader` polls every 50 ms. WPF and Windows Avalonia share this code.

- Arm retirement only after observing positive time with all four result flags
  clear. Profile, vehicle, stats, level and layer identify the observed attempt.
- Require both ended/failed stats flags and ready/failed profile flags. Handle
  this before the backwards-clock restart check: retirement can clear time to zero.
- A partially published result can wait up to two seconds in Paused state for
  an observed started attempt. This preserves the shared end-voice transition.
  Incomplete/unknown results expire silently. Missing reads clear the observation.
- Publish typed `Retired` without finish time or penalty; shared playback chooses
  `system_retired`, and no completed replay is saved. The current outcome stays
  latched so repeated result samples do not repeat the event. Confirmed normal
  completion cannot later be changed to retirement.
- Clear the outcome/arming when the owner changes or a fresh pending attempt
  resets the clock. Late attachment to an old result, pre-start abandonment,
  a frozen timer, missing telemetry and a restart do not prove retirement.
- No specific crash, terminal-damage, timeout or disqualification cause is inferred
  from these generic native failure flags.

## Validation and remaining checks

Automated tests cover zero-time retirement, repeated results, delayed flags,
bounded waiting, normal results/penalty, shared sound token and replay policy,
pre-start exit, late attachment, owner changes, restart and unavailable reads.
The standalone Python reader additionally tests profile count, ownership and
torn result rejection. Existing WRC 7 tests are included in the verification run.
Verification: 80 WRC 7/shared playback tests and four Python tests passed;
the isolated probe build and `git diff --check` also passed. Existing unrelated
compiler warnings remain.

```powershell
dotnet test src/ZTMZ.PacenoteTool.Portable.Tests/ZTMZ.PacenoteTool.Portable.Tests.csproj -c Release --no-restore --artifacts-path tmp/wrcg-verification -m:1 -p:UseSharedCompilation=false -p:_NativeDashboardEnabled=false --filter 'FullyQualifiedName~Wrc7|FullyQualifiedName~RaceEndPlaybackTests'
python -m unittest discover -s tools/Wrc7Probe -p test_capture_results.py
```

The reader lifecycle test deliberately avoids attaching when WRC7 is already
running, so its passing result is not live lifecycle verification. A separate
read-only `Wrc7Probe session <pid>` verified production field decoding. The
running Pacenote Tool has not been replaced. Still needed: audible verification
after rebuilding the app;
normal finish and other game modes also remain live-validation coverage gaps.
