# WRC Generations damage (2026-10-17)

Verified Windows executable SHA-256:
`C2D711BE210F4E1EA0E97882D070C359E66703CC6C40D39D9FC0E5FAF7824D2D`.
Only QUERY_LIMITED_INFORMATION and VM_READ are used. Unknown builds retain the
existing UDP/audio fallback without fabricated damage values.

## Verified source

Follow the existing active-player timer selection: manager RVA `0x1c61660`,
manager+0x48 race matching global `0x1d61938`, timer at race+0x4e8+index*8.
Mode 7 except submode 5 uses manager+0x1380 as index; otherwise index is zero.
Timer+0xa0 participant and participant+0x38 timer must agree. Participant+0x70
vehicle has vtable RVA `0x1744518` and vehicle+0x80 points back to participant.

Participant+0x9b0 is WrcWearAndTearManager, as assigned by native `0x92150d`.
Its vtable is RVA `0x1747968`; owner+0x40 vehicle and owner+0xf8 participant
must agree. The component tree uses head+0xd0/count+0xd8, node name+0x20 and
part+0x28. Part name is +0, maximum life +8, lost life +0xc, remaining-health
warning threshold +0x10 and critical threshold +0x14. Native `0x96cc52`
compares remaining health with these thresholds; `0x96cbf0` caps loss/life at 1.
The layout and meaning were verified separately from WRC7 and WRC9.

WRCG and WRC9 share a bounded mechanical table decoder, projection and event
tracker, with explicit build-specific owner offsets/vtables and source IDs.
WRC7 keeps its distinct impact/wear decoder and event policy.

## Coverage and live evidence

The recorded hybrid car exposes 15 parts: Body, Engine, Gearbox, Turbo, Hybrid,
four Brake_* parts, four Susp_* parts, and Steering_FL/FR. All are retained in
GameData.CarDamage, including independently named hybrid/turbo/steering parts.
The dashboard uses the available body, engine, gearbox, brake and suspension
indicators. No dedicated hybrid/turbo/steering artwork is introduced.
Tyre punctures, wheel condition and radiator damage remain unavailable: none
is inferred from the mechanical table or shown as healthy without evidence.

The initial read-only recording started with all losses at zero. Driving and
collisions increased body, engine, gearbox, turbo, hybrid, right-side brakes,
steering and suspension values. The production C# capture attached with 5.613%
body loss, observed the restart's unavailable interval, then zero body loss in
the new attempt and a fresh collision raising body loss to 2.519%.

The sanitized fixture `wrcg-damage-restart-20261017.json` preserves selected
production readings around these transitions without addresses or game assets.
Tests replay the captured conditions/times through the actual UDP reader using
synthetic telemetry packets. This verifies reader event dispatch, not audible
speaker output or live dashboard rendering. Tests also cover fresh per-packet
reads, native engine severity crossings, pause, repair, restart, attachment,
finish, changed player/timer, invalid data, cyclic trees and torn ownership.

## Runtime behaviour

Damage is sampled for each UDP telemetry packet, not retained in the 200 ms
metadata cache. The current timer must match the metadata timer; invalid reads
clear damage while leaving other UDP telemetry available. A valid advancing
race, stable identity and consecutive reads within 500 ms are required for
announcements. Pause, restart, finish and missing data reset the baseline.
A >=1% body-loss jump produces a collision event; 5%/15% severity boundaries
are ZTMZ announcement policy. Engine announcements use native severity crossings.
The shared core applies sound settings and cooldowns.

## Diagnostic capture

```powershell
dotnet run --project tools/WrcGenerationsProbe/Session -- capture <PID> <seconds> <new-output.jsonl>
```

The capture uses the production memory reader without injecting or attaching
an audio hook. Output must be a new file; duration is 1–1800 seconds. Creating
`<new-output.jsonl>.stop` ends it early. Exceptions print to stderr instead of
escaping to a Windows crash dialog. Raw local diagnostic captures stay ignored.
