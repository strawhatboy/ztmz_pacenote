# WRC 7 damage evidence (2026-10-16)

Supported executable SHA-256: `0FEE0ED14AD05D76FD44B2BA1215BACAFC806DD9D7E8B313634A99C105903633`.
All process access is read-only. Offsets below are for this verified build only.

## Source and meaning

The active participant's `+0xb80` points to WrcWearAndTearManager (vtable RVA
`0xdba450`). Its `+0x40` and `+0x288` point to the physics vehicle; `+0x280`
points back to the participant. The named component tree has head at `+0x100`
and count at `+0x108`. Nodes hold a name pointer at `+0x20` and part at `+0x28`.
The part contains the same name pointer, maximum life at `+8`, accumulated impact
loss at `+0xc`, and accumulated wear at `+0x10`.

Native projection `0x3211f1` calculates `min(1, (impact + wear) / maximum)`.
Impact update sites include `0x32022b`, `0x320a7d`; wear is accumulated separately
at `0x32109c`. These are not WRC9's per-part warning/critical thresholds.
ZTMZ uses explicit display/engine-announcement bands at 25% and 75% lost life;
these are application policy, not a claim about WRC7 HUD colours.

The live table has 15 parts: Engine, Gearbox, Brakes, Electronics, Steering,
FL/FR/RL/RR_Wheel, FL/FR/RL/RR_Susp, FrontSplitter and RearWing. Brakes and
Steering are aggregate. Splitter/wing keep their own names rather than implying
full bodywork coverage. Mechanical wheel wear does not establish tyre puncture.
Unavailable radiator, individual brake, bodywork and tyre fields remain unavailable.

## Live verification and regression coverage

The first collision increased engine, gearbox, electronics, steering and
front-left suspension impact counters. The second recording retained the damaged
baseline, restarted, then captured two impacts, including right-front suspension.
The clock reached -1 shortly before the parts cleared; this transitional state
must not generate a damage call. Ordinary wheel/engine/suspension wear increased
independently of impact counters. A production C# capture of the paused car
returned all 15 components and the corresponding normalized magnitudes.

The sanitized fixture `wrc7-damage-restart-20261016.json` contains selected samples
around the baseline, restart and both impacts. It contains no memory pointers,
process dumps or game assets. Regression tests replay it through the production
projection, lifecycle and event tracker. Additional tests cover invalid/cyclic
maps, torn ownership, non-finite values, capped damage, wear-only updates,
multi-part collisions, missing reads, pause, repair, restart and attachment.

A collision announcement requires a new impact increase of at least 1% of one
part's life over consecutive advancing samples no more than 500 ms apart.
Only the largest component increase produces a collision event per sample.
Its severity bands (5%/15%) match the existing WRC9 announcement policy.
Engine calls require a severity crossing. Core sound settings/cooldowns apply.

For further production captures (no audio hook or injection):

```powershell
dotnet run --project tools/Wrc7Probe -- capture <PID> <seconds> <new-output.jsonl>
```

The output must be a new file; duration is bounded to 1–1800 seconds. Diagnostic
exceptions are reported to stderr instead of escaping to a Windows crash dialog.
