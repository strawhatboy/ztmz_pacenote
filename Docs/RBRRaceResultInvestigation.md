# RBR race-result investigation

Investigation began 2026-10-13 with static analysis and a standalone read-only
capture tool. Practice retirement/restart, Hotlap retirement and online retirement
were subsequently captured live. On 2026-10-14 the bounded production detector
was implemented; see the final section for current behavior and verification.
Historical sections describe evidence and limitations at each investigation step.
No game-memory writes or plugin installation were used.

## Conclusion

There is explicit evidence for **unsuccessful stage completion**, but not a
verified, universal, persistent enum equivalent to DR2's TerminalDamage / Retired
/ Disqualified. Native result-page identity and RSF's saved result callback are
promising read-only sources. Neither generic game state 9 nor a zero finish-time
value by itself establishes a new retirement.

Do not infer a specific crash cause from these aggregate results, or turn damage,
pauses, loss of telemetry, or incomplete progress into a retirement event.

## Analysis inputs and reproducibility

- Game: `L:\Richard Burns Rally\RichardBurnsRally_SSE.exe`, SHA-256
  `5BB1B0C8F6719FAECAD63B0AAFD7541089D1D1FF7A4C9BB68ED49BB53DC6F63B`.
- RSF: `L:\Richard Burns Rally\rsfdata\Rallysimfans.hu.dll`, file/product version
  `0.60.5076.0`, SHA-256
  `F9B7D8AA504DF0C6D046BB94B439ED3699756364583D6559578BC3DA29865F97`.
- Public interface: local `NGPCarMenu/src/IPlugin.h`, with memory definitions in
  `RBRAPI.h`. `HandleResults` documents a zero finish time for a failed stage.
- RBR addresses below are **absolute VAs**, image base `0x400000`. RSF addresses
  are **module RVAs**, image base in the static database `0x10000000`; runtime
  module relocation must be accounted for.

The existing RBR IDA database contained runtime-hooked instructions. Work used
separate copies under ignored `tmp/`, first expanding code analysis, then
restoring executable section bytes from the on-disk executable into
`tmp/rbr-static-results.idb`. Invalidate cached Hex-Rays functions with
`mark_cfunc_dirty` before comparing decompilation: otherwise old hooked targets
can remain visible despite corrected disassembly. Originals were not changed.
RSF received a fresh static database at `tmp/rsf-results.idb`.

Local helpers: `tmp/rbr-static-restore.py`, `tmp/rbr-query.py` and
`tmp/rsf-survey.py`. These and generated proprietary decompilation outputs are
not release inputs. The address chains below are sufficient to resume analysis
without retaining decompiled source in the repository.

## Native RBR result chain

1. `0x477920` emits `CSigEndRace`. `0x481530` registers the application callback
   `0x480810` through the connection at application `+0xc40`.
2. `0x480810` examines event member `+0x10`, distinguishing unsuccessful from
   successful endings. For gameflow modes 0/1 it selects result page 18 versus
   page 16. Other modes have other pages; these are not `RBRGameState` enum IDs.
3. `0x47f3a0` sets application state `*(0x7eac48)+0x728` to 9 for both outcomes.
   It also sets race-data `+0x144` to 1. Neither value distinguishes retirement.
4. Result-menu manager is `*(0x165fc48)`. Constructor `0x56f130` installs vtable
   `0x7466ec`; initializer `0x56f390` fills the page table. Getter `0x56f230`
   returns `manager[pageId+32]`, i.e. `manager+0x80+4*pageId`.
5. Selector `0x581850` writes the selected page pointer to manager `+0x08` and
   `+0x0c` when no application transition is pending. During a pending transition
   (`application+0x724 != 0`) it instead queues the pointer at `+0x1c` and sets
   `+0x2c` to 1. Queued pages are not proof that the page is currently displayed.
   Active menu owner is `*(0x7eac44)` and must be checked as well.

Confirmed page constructors:

| Page ID | Slot offset | Constructor | Evidence |
| --- | --- | --- | --- |
| 16 | `+0xc0` | `0x59b7a0` | Selected for successful modes 0/1 |
| 18 | `+0xc8` | `0x59c170` | Selected for unsuccessful modes 0/1; `ENDRACE` page with replay, save, exit and restart |
| 15 | `+0xbc` | `0x59bef0` | Mode-2 unsuccessful branch; `RB_CHALLENGE` result page |
| 23 | `+0xdc` | `0x56b270` | Mode-3 unsuccessful branch; explicit `FAILED` title |

These slots belong to the **in-stage/result menu manager at `0x165fc48`**, not
the public main-menu array at `0x165fa48` (whose array starts at `+0x78`). Do not
mix their IDs or offsets. Page text is corroboration, not a localization-dependent
runtime detector. Validate the outcome branch and the current attempt as well.

## Retirement, restart and plugin behavior

- Retirement confirmation dispatcher `0x56eac0` selects callbacks by gameflow
  mode. For modes 1/2, callback `0x56e8a0` emits the unsuccessful end event.
  Callbacks `0x56e820`, `0x56e9d0` and `0x56ea40` do so for other modes.
- Mode 6 in `0x56e8a0` emits an event with member `+0x10 == 0`; other modes can
  bypass this event. Therefore even the native event argument is not a universal
  retirement enum across every mode.
- Restart button `0x59b6b0` opens confirmation with callback `0x59b690`.
  Confirmation calls restart routine `0x47e190`, rather than the above retire
  callback. Restart must not announce retirement just because the old stage ends.
- The native plugin stage-launch path around `0x626baa` selects gameflow mode 1
  and calls `0x5666e0` with 1. That setter stores the plugin-stage flag at
  `*(gameflow+0xc98)+0x1785c`, where gameflow is `*(0x165fa90)`.
  `0x5403e0(13)` checks mode 1 plus this flag. Mode 7 is not a synonym for plugin
  racing; its original `0x5693f0` flow includes rally-school lesson handling.
- In the unsuccessful plugin branch, `0x480810` obtains the active plugin via
  `0x626770` and calls its vtable `+0x1c`, matching official
  `IPlugin::HandleResults`, with zero checkpoint/finish values. The callback is
  sent to the active plugin, not broadcast to every loaded plugin.
- After emitting the unsuccessful event, `0x56e8a0` checks the plugin-stage flag
  and can immediately schedule leaving the stage via `0x47e0f0`. That routine
  sets application transition `+0x724` to 2. A result page may therefore be a
  short-lived observation; a polling reader must not assume it sees every one.

## RSF saved result

RSF factory RVA `0x186290` delegates to `0x185ed0`, which stores the plugin
instance at RVA `0x3bd5c4`. Constructor `0x19c0f0` installs vtable RVA `0x2bb864`.
The official callback slots identify:

- `StageStarted`: RVA `0x19f130` (vtable `+0x18`).
- `HandleResults`: RVA `0x19f640` (vtable `+0x1c`).
- `DrawResultsUI`: RVA `0x19f870` (vtable `+0x0c`).

`HandleResults` copies its three float arguments into static module storage:

| RVA | Meaning |
| --- | --- |
| `0x3c2094` | Callback checkpoint 1 time |
| `0x3c2098` | Callback checkpoint 2 time |
| `0x3c209c` | Callback finish time; zero indicates a failed stage in this callback |

The result UI actually reads these values, including a nonpositive-finish-time
branch. Thus these are result data, not guessed offsets from the live clock.
However, `StageStarted` does **not** clear these three fields; inspected direct
xrefs show the result callback as their writer. This does not rule out indirect
bulk writes elsewhere. A zero value can be initial/stale data, and identical
back-to-back failures need not change any of the three values.

Stage-start flags at RVAs `0x3bdd83` and `0x3c1db8` also feed other lifecycle/save
paths (`0x1ade30`, `0x1adf00`, `0x1ae1f0`, `0x1ae220`). They are **not established
result-ready flags**. Do not substitute them for evidence of a fresh callback.

Additional rejected freshness candidates:

- RVA `0x3bdd88` is set by Lua binding `0x15ad50` from a Lua argument (values
  0..8); `HandleResults` does not set it. It is not an established result enum.
- Stage history writer RVA `0x1785b0`, called by cleanup `0x1adf00`, decides
  whether to save a finish time from native finish-line/time/distance fields.
  This does not independently establish explicit retirement versus restart.
- Native `0x5c4aa0` really invokes the active plugin's `DrawResultsUI` at vtable
  `+0x0c`. The successful page-16 constructor `0x59b7a0` creates a result widget;
  failed-page-18 constructor `0x59c170` instead builds replay/save/exit/restart
  items. Therefore the failed-page path cannot be assumed to draw plugin results.
  No general fresh-result marker was established by following that draw call.

Correct callback identities matter: `0x579430` calls `StageStarted(..., true)`
for a false start, **not** `HandleResults`. The ordinary start call is in
`0x573d90`; the normal result call at vtable `+0x1c` is in `0x573530`, reached
from the finish-note branch of `0x4783b0`. The unsuccessful callback in
`0x480810` occurs before selecting the native failure page.

## Forced-ending paths

Native update routine `0x579df0` has multiple unsuccessful-event sources:

- Off-stage warning uses callback `0x5797e0` after the `+0x280` countdown.
- The eight-hour limit (`raceTime >= 28800`) emits an unsuccessful event under
  its additional state/audio guard.
- A warning/fade path at race-data `+0x294..+0x2a4` emits an unsuccessful event
  after its positive-warning duration exceeds five seconds, then sets the latch
  at `+0x134`. Initialization/reset `0x5772e0` clears that latch and warning data.
  Drawing routine `0x575460` uses the same warning data. **The underlying warning
  cause is not established**, so this is not named an engine-failure/crash enum.

These paths strengthen the distinction between an explicit failed result and
damage telemetry, but do not prove distinct persistent terminal-damage,
disqualification or manual-retirement states. The diagnostic captures `+0x134`
as a raw warning-end latch only; production does not use it.

## Implementation boundary and remaining validation

A bounded native implementation could classify the explicitly identified failure
pages for their verified gameflow modes, provided it observes a started,
non-replay attempt and a coherent current result page. It must check executable
layout, pointer validity, active menu ownership, queued transitions, and stage
identity. Unknown/custom pages remain unknown; do not fall back to incomplete
distance or a zero timer. Deduplicate the result and reset the latch on a new
attempt. A completed stage must not become retired merely by leaving its results.

RSF needs one more verified piece: **fresh result validity tied to the current
attempt**, not just the saved float. Trace the relevant result-display owner or
a durable callback-generation/result-ready marker. Use an exact verified module
layout/version if reading private RSF fields; unknown RSF versions should disable
that supplement without breaking normal RBR telemetry.

Before production use, validate normal finish, manual retirement, restart,
pre-start exit, attach-on-results, replay, two successive failures and a plugin
stage that returns directly to its menu. Static callback semantics are confirmed;
polling visibility and plugin-modified runtime transitions require runtime
validation. The initial static analysis ran with the game stopped; the later
manual-retirement capture below verifies one installed RSF flow only.

## Read-only capture tool

`tools/RbrResultProbe` is a standalone Windows/.NET 10 console tool. It has no
reference to the application projects and is not wired into product builds.
It opens the game with VM_READ / QUERY_LIMITED_INFORMATION only and checks the
executable SHA-256 above before sampling. RSF private fields are enabled only
for the verified DLL hash; otherwise they are omitted. No injection or hooks.
Native handler entry bytes are logged because plugins can patch in-memory code
even with an unchanged on-disk executable hash.

From the repository root, after starting RBR:

```powershell
dotnet run --project tools/RbrResultProbe -c Release -- --output tmp/rbr-results-manual-retire.jsonl --seconds 300
```

Use a new output filename for each run (existing files are not overwritten).
The `tmp` directory must exist. Optional `--pid` selects a game process;
`--interval-ms` defaults to 20 ms. Ctrl+C stops and closes the file. Module
identity is checked at attachment, so restart the probe if the game/plugin is
reloaded. This tool neither starts the game nor waits for a future process.

Capture before starting a stage; first record manual retirement, then separately
restart, normal completion, replay and two consecutive retirements. For a crash,
record whether the game actually ends the stage, rather than merely damaging the
engine. Each sample includes raw game state, transition, gameflow mode,
plugin-stage flag, current/underlying/queued result page IDs, active menu owner,
race clock/start/finish flags, map ID and the verified RSF saved result fields.
Page `null` means a null pointer; `-1` means a page not in the known page table.

Changes to discrete fields are logged, plus a one-second heartbeat with clocks
and distances. Unavailable reads are explicit records, not fabricated zeroes.
Roots/state/menu are rechecked after each read; `Coherent=false` marks observed
tearing. Even `Coherent=true` is **not** an atomic snapshot or proof of fresh
result data. The polling interval is a requested delay, not a timing guarantee;
brief events may still be missed. The file is evidence for analysis, not a new
retirement detector.

Validation: Release build and command-line help/error paths checked. Live
retirement and restart captures successfully read native and RSF fields below.

## Live RSF manual retirement (2026-10-13, 20:13 China time)

User drove briefly and confirmed selecting retirement. Capture:
`tmp/rbr-results-20261013-200134.jsonl`, PID 6008, map ID 505. Executable and RSF
hashes matched the versions above; RSF was loaded at `0x55370000`. Sampling used
a requested 20 ms delay with discrete-change records and one-second heartbeats.
Times below are elapsed capture milliseconds, not stage times.

| Capture ms | Raw state | Transition | Current / underlying page | Important observation |
| --- | --- | --- | --- | --- |
| 684561 | 1 | 0 | 0 / 0 | Countdown passed zero; raceFinished=0; RSF start flag=1 |
| 691375 | 2 | 0 | 1 / 1 | Paused after 6.791608 s of racing |
| 695799 | 9 | 0 | 1 / 1 | End state appeared before the failure page; raceFinished still 0 |
| 695925 | 9 | 2 | 18 / 18 | Failure page selected; raceFinished=1; RSF start flag=0 |
| 696330 | 6 | 0 | 18 / 18 | Leaving stage; failure-page pointer still present |
| 697234 | 12 | 0 | 18 / 18 | Race clock reset to zero and countdown reset to 7 |
| 710704 | 3 | 0 | 18 / 18 | Back at main menu; gameflow/plugin flag=0; active menu owner changed |

Gameflow mode and plugin flag were both 1 throughout the stage and initial
teardown. The result-menu owner was active up to returning to the main menu.
The listed samples passed the probe's coherence recheck. Native finish-line,
false-start and warning-end latch fields remained 0. RSF's three saved result
times were **all zero before, during and after this retirement**; they did not
provide a fresh-result discriminator.

Consequences for implementation:

- A live failure page was observable, consistent with the static unsuccessful
  branch. This establishes the feasibility of a bounded native-menu reader for
  this installation; it does not prove coverage of every plugin/game mode.
- There was a 126 ms gap between the first sampled raw state 9 and the first
  sampled failure page. These are sampled observations, not exact event times.
  Do not finalize an unknown result immediately and then lose the later evidence
  to same-state event suppression. Current `RBRGameStateDetector` clears its
  active-stage latch on the first raw end state, and `SetGameState` suppresses
  duplicate RaceEnd events; both must be considered when adding result handling.
- Requiring transition=0 would miss the observed raw-state-9 failure-page
  interval: the page was already current, while transition=2 scheduled departure.
  Distinguish a current page from a merely queued page; departure alone does not
  invalidate an already selected result.
- Page 18 persisted after race data reset and return to the main menu. Never
  classify from that page pointer alone, including when attaching late. Require
  a current observed attempt, appropriate raw state/mode and menu ownership.
- This was manual retirement only. Restart, normal finish, pre-start exit,
  replay and repeated failures remain distinct validation cases. Do not label
  the aggregate unsuccessful page as a proven terminal-damage reason.

## Live RSF restart (same capture, 2026-10-13, 20:19 China time)

User confirmed restarting after driving briefly, then stopping at the reloaded
start. Same process, map ID 505 and hash-verified modules as above. On entering
this second attempt, the stale failure page 18 survived the loading states while
the result-menu owner was inactive, then changed to page 0 at raw state 10
(capture ms 1035237). The next start crossed zero at ms 1045259.

| Capture ms | Raw state | Transition | Current / underlying page | Important observation |
| --- | --- | --- | --- | --- |
| 1049650 | 2 | 0 | 1 / 1 | Paused at race time 4.3902454 s; raceFinished=0 |
| 1054127 | 2 | 3 | 1 / 1 | Restart transition; no failure result page |
| 1054625 | 2 | 0 | 1 / 1 | Race time reset to 0, countdown to 7, raceFinished=1 |
| 1054969 | 5 | 0 | 1 / 1 | Loading the restarted stage |
| 1061514 | 13 | 0 | 1 / 1 | Intermediate loading state |
| 1061792 | 10 | 0 | 0 / 0 | New stage/start UI |
| 1068765 | 1 | 0 | 0 / 0 | At start; raceFinished=1, countdown=6.991672 |

No raw-state-9 / page-18 failure sequence was observed during this restart.
The result-menu owner remained active during restart and reload, so ownership
alone also cannot establish an outcome. RSF's raw stage-start flag stayed 1 even
after the new stage loaded and before its countdown completed. Its saved result
times stayed zero throughout. These fields cannot identify the current attempt
without observing lifecycle resets. All listed samples passed the coherence
recheck; finite-rate polling is not proof of absence of every transient state.

The observed manual-retirement and restart paths are distinct and consistent
with the statically inspected callbacks. A bounded native failure-page detector
must reset the attempt on loading/restart/new countdown, accept only a failure
result belonging to an observed started non-replay attempt, and retain the
pending attempt across the initial raw end state until the result becomes
available or the attempt is invalidated. Never classify from a stale failure
page during loading, raceFinished=1, RSF's start flag, or zero result time alone.
Normal finish, pre-start exit, replay and repeated retirements remain untested
live; production retirement detection is still not implemented.

## RSF Practice, Hotlap and online competition coverage

The user identified the 2026-10-13 live tests above as **RSF Practice**. Hotlap
and online competition were subsequently captured separately below.
RSF's user-facing modes and the
native RBR gameflow mode are different domains; the observed native value 1 does
not mean "Practice" and must not be named as such in code.

The native unsuccessful-result handler `0x480810` chooses page 18 for native
gameflow 0/1, page 15 for 2 and page 23 for 3. It does not itself switch on RSF's
Practice/Hotlap/online labels. The standard plugin launch selects native mode 1
and the plugin-stage flag. RSF stores its `IRBRGame` pointer at module RVA
`0x3bd140` in factory `0x185ed0`; the indirect call at RVA `0x1eec40` invokes
that interface's first virtual method with five arguments, matching the public
`IRBRGame::StartGame(map, car, weather, tyre, setup)` signature. That signature
contains no Practice/Hotlap/online mode parameter.

This provides a reason that different RSF modes **could share** native result
page 18; the later captures below confirm one manual retirement in each of the
three modes for the installed version. RSF also dispatches
its common result callback into Lua, and mode-specific control flow or runtime
hooks may affect the visible result sequence. This inspection has not mapped
every Hotlap/online launch and retirement branch. The private mode-like field
at RVA `0x3bdd88` still has no verified mapping to all user-facing mode names.

Before broadening coverage beyond the observed manual-retirement paths,
establish the other launch/end paths statically or capture them directly.
Record native mode, plugin flag, active page/owner and attempt lifecycle; compare
the whole sequence rather than just page 18. Online-event retirement is a real
user action affecting its result and should only be tested in an event the user
chooses for that purpose. Unknown paths remain unknown, without congratulations
or an invented failure cause.

The initial 30-minute probe ended normally at 20:31:35 China time: 57,722 read
attempts, 1,783 recorded samples, zero read errors and zero detected coherence
changes. A new capture was started for Hotlap on 2026-10-14.

## Live RSF Hotlap retirement (2026-10-14, 13:53 China time)

User identified the starting menu as Hotlap, drove, selected retirement, and
reported that the game returned directly to the Hotlap menu. Capture:
`tmp/rbr-hotlap-20261014-134928.jsonl`, PID 30452, map ID 505, same verified
executable and RSF hashes. Native mode=1 and plugin-stage flag=1 during the stage.

| Capture ms | Raw state | Transition | Current / underlying page | Observation |
| --- | --- | --- | --- | --- |
| 159595 | 1 | 0 | 0 / 0 | Countdown passed zero; raceFinished=0, RSF start flag=1 |
| 204178 | 2 | 0 | 1 / 1 | Paused after 44.57721 s of racing |
| 211993 | 9 | 0 | 1 / 1 | End state appeared while the old pause page remained current |
| 212056 | 9 | 0 | 1 / 1 | RSF start flag cleared before failure-page selection |
| 212087 | 9 | 2 | 18 / 18 | Native failure page selected, active menu owned; raceFinished=1 |
| 212490 | 6 | 0 | 18 / 18 | Leaving stage |
| 212863 | 12 | 0 | 18 / 18 | Race time reset to zero and countdown to 7 |
| 218870 | 3 | 0 | 18 / 18 | Hotlap menu: native mode/plugin flag=0, active menu owner changed |

The first sampled failure page arrived 94 ms after the first sampled raw state
9; the first sampled state 6 followed 403 ms later. These are sampled timings,
not exact event durations. The user need not see a persistent result screen:
the native failure page was readable during automatic departure to the Hotlap
menu. It later persisted as stale data, just as in Practice. RSF saved checkpoint
and finish times stayed zero; finish-line/false-start/warning-end flags stayed
zero. All listed samples passed the coherence recheck.

Practice and Hotlap therefore have matching observed manual-retirement paths
for this installed version, while Practice restart used a different path. The
online-competition follow-up is recorded below. No production detector has been
added; the observations do not establish every failure cause.

## Live RSF online retirement (2026-10-14, 13:57 China time)

User confirmed retiring an online game with ZTMZ running. The same active
capture `tmp/rbr-hotlap-20261014-134928.jsonl` recorded this new attempt on map
ID 151, PID 30452, with the same executable and RSF versions. The filename
reflects where collection began, not the mode of every subsequent attempt.

| Capture ms | Raw state | Transition | Current / underlying page | Observation |
| --- | --- | --- | --- | --- |
| 433282 | 3 | 1 | 18 / 18 | Starting online stage; old Hotlap failure page remains, owner inactive |
| 443342 | 10 | 0 | 0 / 0 | New stage/start UI; result-menu owner active |
| 460863 | 1 | 0 | 0 / 0 | Countdown passed zero; raceFinished=0, RSF start flag=1 |
| 465498 | 2 | 0 | 1 / 1 | Paused after 4.6359334 s of racing |
| 469797 | 9 | 0 | 1 / 1 | End state before native failure-page selection |
| 469921 | 9 | 0 | 1 / 1 | RSF start flag cleared |
| 469952 | 9 | 2 | 18 / 18 | Failure page selected; raceFinished=1, owner active |
| 470330 | 9 | 0 | 18 / 18 | Departure transition cleared, result page still current |
| 470362 | 6 | 0 | 18 / 18 | Leaving stage |
| 471048 | 12 | 0 | 18 / 18 | Race clock reset to zero and countdown reset to 7 |
| 474327 | 3 | 0 | 18 / 18 | Initial menu-state sample still has native mode/plugin flag=1 and owner active |
| 474358 | 3 | 0 | 18 / 18 | Native mode/plugin flag=0 and owner inactive |

Native gameflow mode and plugin-stage flag were both 1 during the attempt.
The first sampled failure page followed the first sampled end state by 155 ms;
state 6 followed the first failure-page observation by 410 ms. All listed
samples passed the coherence recheck. Finish-line, false-start and warning-end
latch fields remained zero; RSF checkpoint and finish times stayed zero.

The first raw-state-3 sample still had an active result-menu owner, which is
another reason to require the appropriate raw end state and attempt lifecycle
rather than treating page identity plus ownership as sufficient on their own.
Loading this attempt also retained the previous Hotlap failure page until the
new start UI was selected. This is direct evidence of stale-result carry-over
between two user-confirmed retirements in different modes.

### Validated scope and implementation direction

| User-confirmed RSF mode/action | Native mode | Observed outcome |
| --- | --- | --- |
| Practice manual retirement | 1 | End state, then failure page 18, then menu |
| Hotlap manual retirement | 1 | Same failure page and departure path |
| Online manual retirement | 1 | Same failure page and departure path |
| Practice restart | 1 | Restart transition 3, loading, new start; no observed failure-page sequence |

For this version, three modes now support a common **bounded native-result
reader**, rather than a different page-ID guess for each RSF mode. Require an
observed started non-replay attempt, a verified native layout, matching stage
identity, appropriate raw end state, current failure page and active menu owner.
Handle delayed result selection without dropping the pending attempt, and clear
that attempt on restart/loading/new countdown. Deduplicate terminal results.
Do not use the observed 94/126/155 ms delays as a universal hard-coded timeout.

Normal finish must retain precedence once confirmed; missing/unrecognized
evidence remains unknown and silent. No crash/disqualification-specific enum
has been verified. The two consecutive Hotlap/online retirements in one process
exercise stale result carry-over, but same-stage repeated retirements, normal
finish, pre-start exit and replay remain separate regression cases. This is
validation evidence and an implementation plan; the production RBR detector
has not yet been changed.

## Production implementation (2026-10-14)

`RBRNativeResultReader` now reads the native layout after verifying the executable
SHA-256 once per process attachment. Unknown binaries disable this supplement,
not the existing telemetry. Native reads check complete byte counts, pointer
bounds and repeated roots/state/page identity; unavailable or visibly changing
snapshots are discarded. No RSF-private offsets or stale saved result times are
needed in production. The reader polls end/menu states as well as racing, with
the existing RBR timer, and is released on detach.

`RBRGameStateDetector` arms the result check only after observing a non-replay
native mode-1/plugin stage actually start (finite nonpositive countdown and
raceFinished=0). It requires the same race-data pointer and map ID, raw end state
9 or 6, active result-menu ownership, current **and underlying** page 18,
raceFinished=1 and finishLinePassed=0. Departure transition 2 is allowed; a
queued page alone is not sufficient. This covers the three observed RSF manual
retirement paths without assuming that RSF mode names equal native mode IDs.

An eligible unfinished attempt may wait up to **2 seconds** for its native
result after the first raw end state. While waiting it reports Paused, so it
does not keep processing the stage as Racing. The 2-second bound limits delayed
cleanup; it is not a guarantee of result delivery. The observed 94/126/155 ms
delays are regression inputs, not a hard-coded game timing rule. Confirmed
normal completion ends immediately via the existing policy. Missing evidence
at the bound, leaving the stage before a result, unsupported layouts, or a
changed stage identity end silently with no invented result. Loading, restart
transition 3 and a new countdown invalidate the prior attempt. Replay and
attach-on-results cannot arm a failure announcement.

One typed `GameStateRaceEnd.Retired` is sent through `RBRGameDataReader` after a
verified failure. It has no normal finish time, uses the existing shared
`system_retired` policy, and cannot create a completed replay. Subsequent end
samples are deduplicated. The shared Core playback contract and other game
providers were not changed. WPF and Windows Avalonia load this same RBR module;
this change does not add Linux RBR support or a more specific crash-reason enum.

Validation: 33 focused tests cover native decoding, unsupported executable,
unavailable/torn reads, queued-versus-current pages, the three captured delays,
restart and consecutive attempts, stale results, identity changes, pre-start
exit, replay, normal-finish precedence, timeout, and actual reader event payloads
and deduplication. Existing state-detector and process-close tests pass too.
The Windows test graph (including WPF) was built to an isolated artifacts path
because the current app held its normal output DLLs open; unrelated native
dashboard build targets were disabled for that isolated test run.

The new production code has **not yet been exercised through a rebuilt running
ZTMZ instance**. Earlier live captures validated the game signals using the
diagnostic tool. Restart/rebuild ZTMZ to verify the final audible retirement
call; the existing running instance cannot use the newly built DLL automatically.
