# WRC Generations lifecycle evidence

## Countdown

Read-only extraction of the installed `IMMERSIVEVOICES.CFG` identifies
`IMMERSION_START_00` through `04` under `StartCountdown`, and `05` through `09`
under `StartCountdown321`. The suffix is a voice variant, not a countdown number.
These resources are not a verified Go signal.

The installed `WIN32/Sounds/GUIDs.txt` identifies `event:/GUI/54321Go` and
`event:/GUI/321Go`. The live bridge now records successful starts of those events,
plus `event:/GUI/Start` and `event:/MUSIC/Finish`, for timeline correlation only.
It does not assume a generic GUI Start event means the car may start, or that
finish music starts exactly at the finish line. No constant delay is used.

The 2026-10-10 test records GUI/54321Go at 09:36:58.757, the first Block_2648
queued for playback at 09:37:05.971, and telemetry Go at 09:37:06.247 (lap clock
0.049 s). Go was placed behind that entire call in the sequential audio queue.
For games explicitly allowing pre-Go calls, Go now uses immediate playback,
preserving the first call rather than flushing it. The cue may briefly overlap
that call. Other games retain their existing telemetry Go queue behavior.
This removed the queue delay but the user confirmed the lap clock starts at the
start line, not at the countdown's Go. Telemetry Go is now disabled for WRCG;
the bridge supplies countdown system sounds directly and crossing the line does
not repeat Go.

`tools/WrcGenerationsProbe/CountdownTimeline.cpp` loads the installed banks in a
separate, silent FMOD system (no injection, no changes to game assets). It confirms
five repeated countdown sounds and a distinct `Sfx - UI Race countdown 01 Go`
in `54321Go`, and three repeated sounds plus Go in `321Go`. Event lengths are
6995 and 5000 ms respectively; their trailing sound tails are not Go timestamps.
The sounds repeat at one-second intervals. SOUND_PLAYED callbacks are prepared
ahead of audible playback: callback timeline positions differ by roughly 100 ms
between asynchronous and synchronous update modes, so callback query timestamps
must not be mistaken for authored sample onset times.

The bridge worker now follows the actual event timeline at 0, 1, 2, ... seconds,
emitting five/three numbers then Go. It checks playback and pause state; it does
not replace the game's FMOD callbacks or store user data on game objects. Stopped
or invalid instances end observation, a new countdown replaces the previous one,
and reconnect does not replay an earlier countdown. Missed beats older than
500 ms are skipped, not burst-replayed. Precise audible alignment still needs an
in-game short-countdown test, including pause/resume and remaining stationary at
the start line. A new bridge DLL requires restarting WRCG.

## Finish

The 2026-10-09 Harju capture has 25,722 complete 280-byte UDP packets. Float field
62 (zero-based, byte offset 248) is zero before starting, negative during racing
(-27833 in this capture), then becomes 379457 while the lap clock settles at
379.457 seconds. The result remains fixed while the car continues slowing down.
This is evidence from the installed build, not an official protocol guarantee.

Detection requires a finite, positive, integer result in milliseconds agreeing
with the lap clock within 2 ms. Repeated packets do not repeat RaceEnd. Pausing
with a negative result field is not a finish. Resetting the clock/result starts
a new stage normally. Offline replay produces RaceBegin -> Racing -> RaceEnd
once, with no rejected complete packets.

The user's displayed time was 399.457 seconds. Read-only inspection of the saved
Harju `Ghost_001` independently found 399457 at offsets 96 and 148; `Ghost_002`
contains 119803 at both offsets, matching the next session's game log. The first
result therefore differs from the UDP clock by exactly 20 seconds. These ghost
offsets are diagnostic evidence only, not a runtime interface.

## Current session identity and official results (Windows)

The optional `Windows/WrcGenerationsMemorySession.cs` reader uses only
`OpenProcess(QUERY_LIMITED_INFORMATION | VM_READ)` and `ReadProcessMemory`.
It does not inject, write memory, call game functions, or run on a game thread.
The portable snapshot decoder and lifecycle logic are shared by both hosts.
Only executable SHA-256
`C2D711BE210F4E1EA0E97882D070C359E66703CC6C40D39D9FC0E5FAF7824D2D`
is enabled. A loaded-code check also verifies the active-stage getter. Unknown
builds, denied access and non-Windows hosts retain UDP/live audio without guessed
identity. No installer asset or game configuration modification is required.

Static inspection of this executable's own telemetry producer establishes the
race-manager global at image RVA `0x1c61660`. The active special is at manager
`+0xd50`, the rally at `+0xd58`. Player 0 is mapped through byte `+0x1380` into
the bounded profile array (`+0x1374` count, `+0x1378` pointer, stride `0x210`).
The selected car definition is profile `+0x30`; this is not the set of preloaded
car models. Stage Name, rally Name and car Name are LocalesId integers at special
`+0xc`, rally `+0x10`, car `+0x14`. Pointer roots are rechecked after sampling to
reject a scene replacement during a read.

Strings come from the installed `LOCALISATION_EN.PLOC`; it has a four-byte header
followed by null-terminated UTF-16LE strings in LocalesId order. `LOCALISATION.LUA`
and `CARS.CFG` provide car-name IDs and championship categories. They are parsed
as data, never executed, and never redistributed. English names remain stable
for replay identity regardless of interface/game language. Categories retain the
game's own championship labels (for example WRC), rather than assuming every
vehicle's last word is its category.

Live verification on 2026-10-10: the user's Finland / Pihlajakoski reverse /
Ford Puma Rally1 session resolved as `Secto Rally Finland - Pihlajakoski reverse`,
`Ford Puma Rally1`, `WRC`. Initialization measured about 100 ms off-thread;
20 consecutive snapshots averaged 0.065–0.124 ms. Normal sampling is throttled
to once per 200 ms. Metadata is prepared before the first race lifecycle callback;
recovery from a temporarily unavailable snapshot re-prepares the identity.

`WrcPlayerProfile::ProcessRaceStats` (RVA `0x814680`) calls the raw-time getter
`0x1160ba0` and penalty-inclusive getter `0x1160c70`, writes milliseconds to
profile `+0x19c` and `+0x1a0`, then sets readiness byte `+0x1a4`. Its game log
explicitly distinguishes `Race time` and `Result time ( including penalties )`.
The reader requires observed non-ready state in the current run, matching current
identity, a total at least equal to raw time, and agreement with the UDP finish
clock within 2 ms. It publishes one RaceEnd with total time and penalty seconds.
If statistics lag UDP, a cancellable-by-generation asynchronous wait polls every
50 ms for at most 2 seconds; restart, identity change or disposal invalidates it.
Unavailable official results use the accepted live cumulative penalty when
available; otherwise they retain raw UDP time and explicitly log unknown penalties.

### Live cumulative penalties

WRCG stops UDP transmission while paused. The shared UDP watchdog now exposes
a lifecycle-policy hook: after two silent watchdog ticks WRCG checks that the
same session still has a valid frozen timer before marking it Paused, preserving
identity, replay and penalties. It does not apply the
default roughly ten-second transition to Unknown, which previously caused a new
AdHocRaceBegin and reset the session on resume. Actual transport availability is
still reported separately. A different session identity clears active state;
unavailable or nonmatching timing must persist for ten watchdog checks to clear
it, tolerating transient read failures. Neither case publishes a normal finish.
Without verified memory timing, silence cannot establish pause and eventually
falls back to Unknown. Other providers retain their existing watchdog policy;
confirmed finish, real restart and process teardown remain separate transitions.

The verified build's active-timer getter (`0x8176d0`) selects from race `+0x4e8`.
Race must agree between manager `+0x48` and image RVA `0x1d61938`. Normally timer
index is zero; mode 7 (manager `+0x12d0`) except submode 5 (`+0x12e0`) uses the
player mapping byte `+0x1380`, bounded below 50. Timer `+0xc8` holds protected
cumulative penalty milliseconds. We reproduce only the read portion of getter
`0x415310`: object `+8` indexes the uint table at image RVA `0x1bbf030`; XOR mask
is `((uint(object + 8) + index) * 0x159a55e5 + 0x159ae628)` modulo 2^32,
substituting `0xb10be924` for zero. Slot/table stability is checked with at most
three attempts. No game function is called and no memory is modified.

UDP's raw lap getter `0x11607f0` uses the last interval in timer `+0x40`, count
`+0x3c`, stride `0x250`. Protected start/end are interval `+0x10`/`+0x20`;
an unfinished interval uses the protected clock at `*(race + 0x898) + 0x2f0`.
Ownership/start are rechecked, retired/invalid timers and out-of-range values
rejected. A timing snapshot must agree with raw UDP time within 500 ms (normal
metadata sampling is every 200 ms). Transient read failures retain the accepted
penalty; restart/identity changes clear it.

GameData.LapTime is raw UDP lap time plus the cumulative penalty, never an
incremental addition to the previous packet. Lifecycle decisions retain raw time.
Replay samples/checkpoints consequently include penalties; sampling compares the
last recorded time bucket, not the number of samples, so a penalty jump does not
cause packet-rate recording. Official finish totals replace the adjusted time
without adding penalties again. On 2026-10-10 the live read-only probe decoded
15,791 ms raw time and 10,000 ms penalty, averaging 0.170 ms per snapshot.

The 20-second result path, stale equal-time results, delayed statistics, restart,
player-index bounds and scene replacement are covered by portable tests. A new
in-game penalty-inclusive finish has not yet been observed by this implementation;
the old ghost/log evidence and synthetic lifecycle tests do not replace that E2E
check. No additional full-stage run was required during development.

### Telemetry completeness (2026-10-11)

The verified game's UDP producer at RVA `0x911800` writes float index 3 from
the active stage's length getter `0x529fc0` (`stage+0x98`, falling back to
`stage+0x29c`). This is kilometres, not an unused field. The recorded Harju
session reports 2.2662833; the provider converts it to 2266.2833 metres and
computes bounded 0..1 completion from UDP index 2 (travelled metres). Index 61
remains unused; it is not the length source. Invalid/absent lengths do not
produce a fabricated completion value. UDP steering is negated to match the
shared direction convention, following the user's in-game confirmation.

Handbrake is absent from UDP. The read-only, executable-hash-guarded source
uses the active timer selection described above, then `timer+0xa0` to its
participant, requiring `participant+0x38 == timer`. The UDP producer establishes
`participant+0x70 -> vehicle`, `vehicle+0xa0 -> drive`. The game's debug HUD
at `0x1199dba` labels drive control index 2 as Handbrake; getter `0x11dce40`
reads that float directly from `drive+0x170+2*4`. Roots, participant ownership,
vehicle, drive, and player selection are rechecked after the read. Scene teardown,
invalid pointers and non-finite/out-of-range values produce an unavailable
handbrake, not a retained previous value. Sampling occurs per UDP packet, separate
from the 200 ms metadata cache. Live attachment to Harju / Toyota GR Yaris Rally1
verified the pointer chain and released value 0. The user subsequently confirmed
that handbrake operation works in the deployed application.

Metadata discovery no longer permanently excludes a PID after a failed attach.
Discovery runs off-thread and retries after five seconds, with one attempt in
flight and lifecycle-generation checks to discard stale completions. Tests cover
retry recovery, fresh handbrake input, reciprocal ownership, torn scene reads,
invalid input, steering sign, and metre/kilometre progress conversion.
