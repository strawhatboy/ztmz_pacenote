# DiRT Rally 2.0 telemetry

## UDP and vehicle identity

Extra-data 3 packets contain 66 floats (264 bytes). Truncated packets are ignored.
DR2 exports engine speeds in radians/second; public GameData uses RPM, converted
with `60 / (2 * PI)`. The legacy raw-data RPM fingerprint remains unchanged for
compatibility with existing car lookup tables.

On supported Windows builds, a read-only process reader supplements UDP with
handbrake, the vehicle's actual shift warning threshold, and its three-character
car code. This does not inject a DLL or run work on the game's rendering thread.
The executable SHA-256 gate is:

`C119F509FC315D8168AA7AAA5D0A1EE8E3570C7220BD6CBB2D337D6725D73442`

Vehicle wrappers are found through RVA `0x201b7b0`, count `0x201b930`.
The reader requires a unique mode-0 player wrapper, validates its vtable and
checks decoded idle/max RPM against UDP before accepting supplemental values.
Physics-relative fields are handbrake `0x390`, car code `0x4d0`, idle/max engine
limits `0x6d0`/`0x6d4`, and encoded shift warning speed `0x804`.
Pointers are re-read rather than retained across stage reloads.

Unknown executable builds and non-Windows hosts retain UDP and existing car
fingerprint fallback. They must not claim valid handbrake data. The car-code
catalog distinguishes vehicles that share RPM fingerprints. Catalog reference:
[RallyGamer DR2 car list](https://www.rallygamer.com/downloads/dirtrally2/tools/dirt_rally_2.0_car_list.pdf),
cross-checked against installed car archive names.

## Turbo pressure (2026-10-17)

The verified Windows memory reader now supplies `GameData.Turbo` alongside UDP.
The in-game HUD getter at RVA `0x7313f0` reads physics `+0x2538`. Its update at
`0x74e750` smooths physics `+0x1388` minus `1.0` into that field. Publish this
displayed gauge pressure in bar directly; do not subtract atmosphere again or
use the normalized `turbo_pressure` audio parameter.

Turbo availability follows the game's HUD check (`0x1a6720` calling `0x7336c0`):
read the parameter pointer at physics `+0x1378`, decode its `+0x344` float by
multiplying its stored uint32 by `0x16889185` modulo 2^32, and require a finite
positive value. A non-turbo car is unavailable even if a pressure field contains
an old value. The native `-1` initialization sentinel, nonfinite readings and
readings above the decoded pressure limit (allowing 0.01 bar rounding tolerance)
are omitted. Valid negative gauge pressure and zero remain visible.

Read failures or partial reads omit only turbo telemetry, preserving handbrake
and shift RPM. Parameter and player-physics pointers are rechecked after sampling.
The existing executable hash and unique player/UDP RPM checks still apply.
Unsupported builds and non-Windows readers leave turbo unavailable.

A read-only Fiesta R5 capture observed a decoded pressure limit of 3.5 and HUD
pressure from about -0.13 at low load to +2.50 at full boost. A subsequent recording
through the production C# reader, with a user-confirmed stage restart and drive,
reported -0.742 to +2.500 and returned to approximately -0.113 when paused at idle.
The user confirmed the native gauge spans -1 to 4. A non-turbo car has unit-test
coverage but has not been live driven for this change. Tests cover negative/zero/positive pressure,
non-turbo cars, invalid values, reset sentinels, failed/partial reads and pointer
replacement. `tools/Dr2Probe --turbo <idle-rad/s> <max-rad/s> [seconds] [log-path]`
records the production memory reader without changing game UDP configuration.

## Start sequence

The captured time-trial restart began its pre-start stream at simulation time
134.41861, with lap time zero and distance -2.38931 m. The first positive lap
time arrived at simulation time 139.47037, still at that negative distance.
Therefore crossing distance zero is not the Go signal: positive lap time is.

The subsequent capture exposed an important earlier packet: at 14:07:50.998,
DR2 sent a restart packet with simulation time 62.704605 and lap time zero,
then stopped UDP while waiting for the handbrake. At 14:08:21.058 it resumed
at simulation time 84.62696; Go followed at 89.67852 with lap time 0.033332.
The reset packet must NOT emit 5 or anchor the countdown. Loading/waiting time
between these streams must not be subtracted from five seconds.

Countdown numbers require an observed restart followed by two adjacent advancing
simulation frames (at most 250 ms apart). A discontinuity resets the candidate
anchor; it cannot instantly consume the countdown. Pausing does not advance
numbers, and missed numbers are not replayed in a burst. UDP silence during the
handbrake wait preserves the pre-start session rather than changing it to Unknown.
DR2 countdown and Go cues use immediate system playback rather than waiting
behind ordinary pacenote audio. Session state is published before the first
telemetry event, with the current GameData snapshot already available.

This is an inferred five-second start sequence, not a native countdown field.
Without an observed restart (including attaching to an already active stream),
the reader conservatively skips countdown numbers instead of inventing five
remaining seconds. It no longer uses a 500 ms listener wait as proof of a start.
Packet loss and other event modes still require validation. Daily/online events
have not been live verified.

## Component damage

Read-only Windows sampling uses the same hash-gated player-vehicle identification,
at most five times per second. The damage interface at physics + `0x2bd0` points
to itself; values start eight bytes later. Native component getter `0x751260`
uses component 3 for engine (offset 0), 7 for radiator (offset `0x80`), and 6
for gearbox (maximum of offsets `0x0c` and `0x84`). These identities are confirmed
by the game's own engine/radiator/gearbox sound parameter consumers.

The configuration pointer is at damage state + `0x440`. Engine effect onset is
the minimum of configuration offsets `0x1f4`, `0x1fc`, `0x204`; radiator uses
`0x1ec`; gearbox uses the minimum of `0x2a0`, `0x334`. Native `0x743570` maps
raw damage to effect with clamp((damage - onset) / (1 - onset), 0, 1).
ZTMZ retains raw damage and onset, and projects effect into four severity buckets.
Those buckets are ZTMZ's convention, not a native severity enum or measured
remaining component lifespan.

Live Fiesta R5 values were engine 0, radiator 0, gearbox 0.000166667; their
effect onset values were respectively 0.3, 0.7, 0.3, so all remain healthy in
the dashboard projection. Damaged-state UI comparison has not been performed.
Gearbox is retained as its own component, rather than falsely mapping it to
differential damage.

### Four corners and bodywork (2026-10-11)

Offsets below are relative to `owner + 8`, except the discrete tyre states.
The wheel order is rear-left, rear-right, front-left, front-right. The native
puncture voice routing at `0xb2108e` distinguishes these positions; the live
front-right state of 2 was independently confirmed by the user as a front-right
blowout. Physics wheel records (`physics + 0x1480 + index * 0x420`) also carry
the index at `+0x28` and separate front/rear and left/right coordinates.

| Component | Value offset | Configuration onset | Provenance |
| --- | --- | --- | --- |
| Bodywork | max(`0xa4`, `0xa8`) | 0 | Native component 0, `0x751288`; Bodywork/Brakes/etc. component ordering in `0x11bda0` |
| Damper | `0x10 + index*4` | `0x84` | Effect 0; native suspension combiner `0x751a60` |
| Spring | `0x20 + index*4` | `0xb8` | Effect 1; same suspension combiner |
| Brake | `0x30 + index*4` | `0x18c` | Effect 6; `0xb18375` feeds the `damage_brake` parameter |
| Wheel geometry/impact | `0x8c + index*4` | min(`0xc0`, `0xec`, `0xf4`, `0xfc`) | Effects 2–5; effect 8 uses `0x104` as failure threshold |
| Tyre state | **owner** `+0x120 + index` byte | discrete | `0x732190 -> 0x57d550`; voice consumers distinguish 1 (puncture) / 2 (blowout) |

Damper and spring raw entries remain separate; shared suspension severity uses
their maximum. Tyre states are not a remaining-tread percentage: ZTMZ maps 0 to
healthy, 1 to severity 3 and 2 to severity 4, preserving the native integer.
Invalid new component values are omitted rather than reported as healthy.
The four corner groups remain marked incomplete; these fields do not expose
every possible physical part in the game.

The production reader was run against the paused live game: it reported 20
normalized components, front-right tyre state 2, nonzero front-wheel geometry
damage, and bodywork/radiator damage of 1. Healthy and damaged severity branches,
corner isolation, invalid values and raw-value retention are covered offline.
No memory writes, injected probes or render-thread callbacks are used.

## Validation

### Native retirement/result state (2026-10-13)

The 264-byte UDP packet has no verified retirement field. Do not turn silence,
zero speed, a damaged engine, or incomplete stage distance into retirement.
The supplemental **Windows** reader now uses the game's local-player result
record, under the same executable SHA-256 gate above. WPF and Windows Avalonia
share it; Linux and unverified builds retain UDP without invented retirements.

Native evidence (all addresses are RVAs in that verified executable):

- `0x559f10` returns the player manager at `0x16951e8`.
- `0x42abc0` locates the local player in manager `+0x30` (pointer array),
  `+0x40` (count). The entry must have type 3 at `+0x2c` and the local ID at
  `+0xbc`. The local ID comes from `*( *( *(image+0x1695150)+8 ) +
  8 * *(int32*)(image+0x16940c4) ) + 0x2a` (uint16).
- `0x509bd0` searches the race manager at `0x1695240`, array `+0xaca0`,
  uint64 count `+0xacb0`, for a result record whose `+0x40` equals that player.
- `0x4c5110` decodes result-record `+0x18` by XOR with the low 32 bits of
  the process key at `0x15d8510`; key accessor is `0xa0c00`.
- `0x4c6060` maps the decoded enum to names: 0 Finished, 1 TimedOut,
  2 Eliminated, 3 Racing, 4 TerminalDamage, 5 Retired, 6 Disqualified,
  7 Griefing, 8 Disconnected, 9 Spectated, 10 None. The UI consumer at
  `0x280bc0` also compares the decoded value with 5 before selecting `retired`.

The reader bounds pointers/counts, requires an unambiguous matching player/result,
rejects unknown state values, and rechecks ownership during a snapshot. It reads
at most five times/second with UDP, and through the existing one-second timer
when UDP stops. A UDP end/reset packet also triggers a fresh state read so a stale
LastLapTime cannot override an already reported native retirement.

Only a terminal state **after observed Racing and a started stage in the same
record** emits a result. Native Retired/TerminalDamage/Disqualified/TimedOut map
to shared Retired/Crashed/Disqualified/TimeOut and the existing `system_retired`
voice. No finish time is fabricated and no completed replay is saved. Repeated
results are latched; delayed UDP racing packets cannot revive the ended attempt.
Restart/grid/menu transitions clear the old racing observation. Attaching to an
old result screen, changing player records, or losing memory reads does not
fabricate a new result. Other native statuses remain unhandled, and normal
finish timing keeps its existing UDP path.

Live validation: during the user's 2026-10-13 test, the same player result record
reported **Racing at 18:35:00.054** and **Retired at 18:36:12.235 (+08:00)**,
matching the user's manual retirement. Earlier time-trial entry/menu transitions
produced None/Racing/unavailable, not Retired. Raw local evidence is in the ignored
`tmp/dr2-race-state-20261013.log`. This validates the native signal; audible
end-to-end playback still requires running the rebuilt application. Terminal
damage, timeout and disqualification mappings are source-backed and tested
offline, but have not been live reproduced.

`tools/Dr2Probe` supports `--race <seconds> <new-log-path>` to capture only
changes through the same hash-gated production memory reader, independent of
UDP. It never writes to the game. Example:

```powershell
dotnet run --project tools/Dr2Probe -- --race 300 tmp/dr2-race-validation.log
```

`Dr2RaceStateMemoryTest` and `Dr2RetirementTest` cover decoding, player isolation,
ambiguous/changed ownership, unknown values, UDP-stop polling, result deduplication,
stale finish-time precedence, pause, restart, a second attempt, pre-start exit,
normal completion and DR1 isolation. Together with the existing DR2 and shared
ending-voice tests, 73 focused cases pass.

### Previous telemetry validation

Twenty-one focused tests cover memory validation, UDP conversion, startup ordering,
handbrake wait, countdown pause/restart, damage validation, and Go before crossing
the starting line. A live sample
identified Ford Fiesta R5 (`fr5`), R5 class, Fury Lake Depart, 7400 RPM maximum,
7033 RPM shift warning, and valid handbrake input. The user confirmed Fiesta R5,
not Fiesta R5 MKII. Final audible countdown synchronization still needs UI/game
verification; packet timing alone is not an audio end-to-end test.
