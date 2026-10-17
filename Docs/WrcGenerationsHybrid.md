# WRC Generations hybrid telemetry

The Windows reader exposes `GameData.Hybrid` for the verified Steam executable
SHA-256 `C2D711BE210F4E1EA0E97882D070C359E66703CC6C40D39D9FC0E5FAF7824D2D`.
It only opens the process for query/read access. Unknown builds retain UDP/audio;
no injection, game-function execution or memory writes are involved.

## Contract

`HybridTelemetrySnapshot` is independent of hybrid-system **damage**. A null
snapshot means unavailable or a non-hybrid car. Nullable individual properties
allow other providers to expose only supported readings. Lua dashboards should
check `data.Hybrid ~= nil` before using it. Web/native dashboard JSON omits absent
readings rather than inventing zero charge or a false operational status.

| Property | Meaning |
| --- | --- |
| BatteryChargeFraction | Stored energy / nominal capacity, 0–1 |
| BatteryEnergyKWh | Stored battery energy in kWh |
| BatteryCapacityKWh | Nominal capacity in kWh |
| BoostProgressFraction | Recharge toward the next boost when inactive; remaining boost budget when active, 0–1 |
| BoostReady | Native controller permits the next boost |
| BoostActive | Native boost activation flag |
| RegenerationActive | Native regeneration phase; does not claim measured electrical power |
| BoostMap | Selected map, 1–3 as displayed by the game |

These readings are sampled for every UDP packet, independently of the 200 ms
session/name cache. The shared snapshot reaches WPF/Avalonia Lua and browser/native
dashboard telemetry. Each modern WRC dashboard owns its hybrid drawing code,
colors and placement; only snapshot normalization is shared in `commonfunc.lua`.
The round `modern_wrc` dial uses concentric battery/boost arcs in its lower opening.
`modern_wrc_2` pairs a segmented battery cell with a segmented boost dial in its
horizontal wings. `modern_wrc_2025` puts a lightning/boost ring beside the
handbrake and battery charge/map below Km/h; the live time comparison moves below
the handbrake so both remain visible. The wider `modern_wrc_2025v2` uses thin
circular gauges in its side wings. Progress is encoded by the arcs, battery
percentage and selected map remain numeric, and state labels distinguish boost,
ready, regeneration and standby. There is no shared rectangular hybrid panel.
Hybrid graphics are hidden when readings are unavailable, with a
`Show hybrid telemetry` setting enabled by default. The handbrake indicators
remain separate. Browser/native runtimes use English fallback labels; desktop
labels and the setting also have Chinese translations.

## Evidence and addresses

All offsets below are specific to the hash above. First select the current player
and validate the timer, participant and vehicle as for
[damage telemetry](WrcGenerationsDamage.md). Reject a cached timer belonging to a
different scene or player. Let `P` be the participant, `V` its vehicle,
`D = *(V + 0xa0)` and `H = *(P + 0xa08)`.

- `V+0x1118` is the hybrid-enabled integer. Require 1. Ordinary cars retain default
  capacity/energy values, so those numbers alone do not establish hybrid support.
- `V+0x2178 == P`, `H+0 == P`, `H+8 == V`; ownership is rechecked after sampling.
- Config loader/update `0x8f5d30` references **Hybrid Battery Capacity In kWh**,
  copying the double at `V+0x1120` to float `H+0x30`. `H+0x34` is 80% of that
  nominal capacity, not a unit conversion.
- Battery HUD getter `0x8f5380` reads double `D+0x688`, converts to float and divides
  by `H+0x30`. Reset `0x8f5e70` fills it to `H+0x34`.
- Ready getter `0x8f5340` checks full-usable-battery flag `H+0x66` or recovered
  energy `H+0x3c >= 30`, and requires boost flag `H+0x65 == 0`.
- HUD gauge getter `0x8f53e0` returns recovered energy / 30 while recharging,
  1 when ready, or `1 - ((H+0x40 - energy) * 3600) / (H+0x5c)` during boost.
  A zero starting-energy field is the launch-boost special case (zero budget
  consumed). Display fractions are bounded to 0–1 at transition edges.
- Update `0x8f59d0` sets/clears regeneration phase byte `H+0x67`; this is a
  controller phase and can overlap a boost transition or a full battery.
- Map setter `0x8f5360` accepts integers 0–2 at `H+0x18`. HUD update `0x564607`
  consumes this selected map, readiness and battery/gauge getters. `H+0x28` is
  the latched map of the active boost, not the selected map.

Nonfinite/out-of-range numeric data, invalid flags/maps, missing controllers,
retired timers and changed ownership return unavailable without suppressing UDP.
No electrical power, motor torque, temperature or inferred throttle-based boost
readings are exposed.

## Verification

On 2026-10-17, the initial non-hybrid car had hybrid disabled and no controller,
despite retained battery defaults. Switching to Toyota GR Yaris Rally1 exposed
3.9 kWh nominal capacity, 3.12 kWh initial energy (80%), and selected map 2.
Controlled acceleration/braking captured discharge, regeneration, ready and active
boost through both the raw recorder and production C# reader.
The first production capture contained 1,624 samples, including 54 unavailable
hybrid samples during the countdown: metadata had a valid pending timer but no
lap time yet. The reader now uses that verified pending timer as a fallback,
covered by a countdown regression test; no positive lap time is required.
The race clock reset from 71.270 s to 0.011 s, with a freshly
charged battery already supplying launch boost (79.675%). The subsequent drive
again exercised discharge and regeneration. Map 2 was observed; live switching
between maps was not exercised.

The portable tests cover units, both boost-gauge phases, readiness, launch boost,
non-hybrid cars, invalid/torn memory, stale timers, per-packet freshness, missing
session data and actual WebSocket delivery to native/browser dashboard clients.
Final validation passed 333 focused tests and both Windows WPF/Avalonia Debug
builds, with their staged KT provider DLLs matching the rebuilt provider. An
existing countdown-audio pipe test failed once in an earlier run and passed on
rerun and in the final suite; it was not changed by this work.
The dashboard redesign passed the native Lua runtime/built-in dashboard tests,
the browser smoke suite for all 17 bundled dashboards, and actual Chromium
renders of all four layouts (including active pedals, handbrake and lap delta).
The browser regressions exercise boost/ready/regen/standby, missing fields,
car changes, the visibility setting, zero charge, size extremes and swapped
speed/gear. The focused telemetry/dashboard suite passed 206 tests on rerun;
the first run hit the same pre-existing countdown-pipe timing failure noted above.
The probe's `capture` mode records the production `hybrid` snapshot alongside
session and damage data. Raw process dumps stay in ignored local diagnostics.

EA WRC is unchanged. Its installed documented schema (schema 1/data 2, 77 channels)
does not expose hybrid charge/boost fields. No EA WRC process-memory work was done.
