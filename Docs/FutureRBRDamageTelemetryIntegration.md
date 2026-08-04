# RBR Damage Telemetry Research

## Status

Static analysis has identified a live damage block in the RBR build used by
`RBRHUD.dll` dated 2025-04-21. Pacenote Tool now reads it automatically and
read-only during an active race or replay. Every known component keeps its raw
values, while a second layer exposes the seven compact RBRHUD-compatible
damage groups. This does not enable damage announcements, dashboard warnings,
or any game modification.

The public RBR API exposes the physics root but not the granular damage
layout. RBRHUD appears to poll the game memory directly and converts fields
into its own five-level display values. The existing public API reference is
[NGPCarMenu RBRAPI.h](https://raw.githubusercontent.com/mika-n/NGPCarMenu/master/src/RBRAPI.h).

## Confirmed Pointer Chain

RBRHUD obtains the same physics pointer that Pacenote Tool already reads:

```text
physics = ReadInt32(0x008EF660)
damage  = ReadInt32(physics + 0x620)
```

Its code resolves `0x008EF660` as `RBR module base + 0x4EF660`, which confirms
that this is the familiar RBR physics root rather than an RBRHUD-specific
allocation. Each concrete damage telemetry object then reads from
`damage + offset` and converts the raw value to RBRHUD's display level.

RBRHUD imports `ReadProcessMemory`, but that belongs to a generic pointer or
buffer utility. The per-part refresh functions read in-process memory through
the chain above. Pacenote Tool can mirror the same reads through its existing
read-only process handle; it does not need RBRHUD to be installed and must not
inject or modify the game.

## Candidate Damage Layout

The following offsets were recovered from the concrete RBRHUD telemetry
classes. `Converter` describes RBRHUD's raw-value interpretation and is now
mirrored only as a compact `HudLevel` alongside the unmodified raw values.

| Part | Damage offset | Converter |
| --- | ---: | --- |
| Battery | `0x08C` | float thresholds |
| Bodywork | `0x488` | pointer to aggregate bodywork data |
| Bonnet | `0x388` | integer flag |
| Bonnet lock | `0x384` | integer flag |
| Brake caliper LF | `0x01C` | integer flag |
| Brake caliper LR | `0x034` | integer flag |
| Brake caliper RF | `0x028` | integer flag |
| Brake caliper RR | `0x040` | integer flag |
| Brake circuit | `0x080` | integer flag |
| Center differential | `0x008` | float plus adjacent status field |
| Clutch | `0x084` | float thresholds |
| Control arm LF | `0x1F8` | float thresholds |
| Control arm LR | `0x268` | float thresholds |
| Control arm RF | `0x230` | float thresholds |
| Control arm RR | `0x2A0` | float thresholds |
| Damper LF | `0x2D0` | float thresholds |
| Damper LR | `0x2E0` | float thresholds |
| Damper RF | `0x2D8` | float thresholds |
| Damper RR | `0x2E8` | float thresholds |
| Damper mount LF | `0x1EC` | two-float aggregate |
| Damper mount LR | `0x25C` | two-float aggregate |
| Damper mount RF | `0x224` | two-float aggregate |
| Damper mount RR | `0x294` | two-float aggregate |
| Drive shaft LF | `0x018` | integer flag |
| Drive shaft LR | `0x030` | integer flag |
| Drive shaft RF | `0x024` | integer flag |
| Drive shaft RR | `0x03C` | integer flag |
| Electrical | `0x1E8` | integer flag |
| Exhaust | `0x394` | integer flag |
| Exhaust mount | `0x398` | integer flag |
| Front bumper | `0x39C` | integer enum |
| Front differential | `0x000` | float plus adjacent status field |
| Gear actuator | `0x078` | integer flag |
| Gearbox | `0x048` | multi-float aggregate |
| Head gasket | `0x098` | multi-float aggregate |
| Hub LF | `0x1FC` | absolute float thresholds |
| Hub LR | `0x26C` | absolute float thresholds |
| Hub RF | `0x234` | absolute float thresholds |
| Hub RR | `0x2A4` | absolute float thresholds |
| Hydraulics | `0x090` | integer flag |
| Intercooler | `0x0F8` | inverse float thresholds |
| Left door | `0x370` | integer flag |
| Left door lock | `0x36C` | integer flag |
| Oil cooler | `0x0F4` | integer flag |
| Oil pump | `0x0F0` | float zero test |
| Radiator | `0x0E8` | inverse float thresholds |
| Rear bumper | `0x3A0` | integer enum |
| Rear differential | `0x010` | float plus adjacent status field |
| Right door | `0x37C` | integer flag |
| Right door lock | `0x378` | integer flag |
| Starter | `0x07C` | integer flag |
| Steering rod LF | `0x1F4` | float thresholds |
| Steering rod RF | `0x22C` | float thresholds |
| Suspension arm LR | `0x264` | float thresholds |
| Suspension arm RR | `0x29C` | float thresholds |
| Tyre LF | `0x2EC` | 128-byte float aggregate |
| Tyre LR | `0x32C` | 128-byte float aggregate |
| Tyre RF | `0x30C` | 128-byte float aggregate |
| Tyre RR | `0x34C` | 128-byte float aggregate |
| Water pump | `0x0DC` | integer flag |
| Wheel LF | `0x200` | 128-byte float aggregate |
| Wheel LR | `0x270` | 128-byte float aggregate |
| Wheel RF | `0x238` | 128-byte float aggregate |
| Wheel RR | `0x2A8` | 128-byte float aggregate |
| Windshield | `0x3AC` | multi-field state |
| Wing | `0x3A4` | integer enum |

`CTelemetryDamageOilPressure` has a no-op refresh slot in this RBRHUD build.
It should be investigated through the separate float telemetry path rather
than assumed to be a member of this damage block.

## Conversion Behavior

The RBRHUD UI stores a discrete level from 1 to 5 after every refresh. Its
converters establish these broad categories:

- Integer flags and enums cover brakes, shafts, doors, exhaust, bumpers, and
  several engine accessories.
- Single float fields cover battery, clutch, radiator, intercooler, oil pump,
  suspension parts, and hubs. Some are health values where lower is worse;
  others are damage values where higher is worse.
- Differential fields combine a float with a neighboring integer status.
- Tyres and wheels aggregate 32 floats (128 bytes) before assigning a level.
- Gearbox, head gasket, damper mounts, and bodywork combine multiple values;
  bodywork first follows another pointer from `damage + 0x488`.

RBRHUD's level numbering is presentation behavior, not a stable public
contract. A Pacenote Tool implementation should retain raw values, derive
named semantic states locally, and make its own thresholds configurable if
needed.

## RBRHUD Converter Reference

The following observations are recorded to make the next investigation
repeatable. They describe the converter shapes recovered from this RBRHUD
build, not thresholds that Pacenote Tool should expose directly.

| RBRHUD converter | Observed behavior |
| --- | --- |
| Integer flag converters | Map a zero/nonzero raw value to a healthy or failed display state. Two variants invert the interpretation. |
| Integer enum converter | Treats raw values `1` and `2` as progressively more severe states; other values use the normal state. |
| General float converters | Use threshold families near `0.30 / 0.50 / 0.70 / 0.95` and `0.50 / 0.65 / 0.80 / 0.90`. |
| Inverse health converters | Treat lower raw values as worse, with families near `0.20 / 0.03 / 0.005`, `0.70 / 0.40 / 0.10 / 0.02`, and `0.30 / 0.10 / 0.02`. |
| Absolute-value converter | Uses `abs(raw)` with thresholds near `0.10 / 0.05 / 0.01`. |
| Zero-test converter | Treats a non-positive raw value as the failed condition. |
| Differential converter | Combines a float with a neighboring status field. |
| Tyre and wheel converter | Uses the first eight floats of the 128-byte raw window before assigning a display state. |
| Head-gasket and gearbox converters | Select the weakest value from multiple separate float fields. |
| Damper-mount converter | Combines two health values before assigning a state. |
| Bodywork converter | Follows the pointer at `damage + 0x488` and aggregates multiple entries. |

RBRHUD internally maps those results to display levels `1` through `5`; level
`1` is hidden in its Honeycomb dashboard and level `5` maps to the blinking red
state. Pacenote Tool exposes the observed `HudLevel` and a derived `Severity`
from `0` through `4`, but retains the original raw scalar, integer, and array
data as the source of truth. No automatic voice or visual behavior is attached
to the new data yet.

## Current Implementation

Pacenote Tool samples at most five times per second while RBR is in an active
race or replay. It validates the physics and damage pointers, then reads the
bounded `0x48C`-byte damage block through the existing read-only process
handle. It makes no calls that alter RBR and does not require RBRHUD to be
installed.

Each snapshot contains all currently recovered parts, including their raw
scalar or array data. The bodywork pointer is retained as raw evidence, but it
is not yet followed into a derived bodywork severity. Gearbox uses the complete
17-float window required by RBRHUD's converter. Tyre and wheel entries retain
their full 32-float raw windows; the derived level intentionally uses the first
eight floats, matching the analyzed RBRHUD converter.

The latest snapshot is assigned to `RBRMemData.DamageTelemetry` and included as
`Game.RuntimeDiagnostics` in `diagnostics.json` when a support bundle is
created. The normal log records a baseline, pointer change, or a change to a
derived part/group level. It deliberately does not log every raw fluctuation;
the latest snapshot still retains those values. Unreadable pointers or blocks
are reported as unavailable rather than treated as healthy data.

## RBRHUD-Compatible Groups

The seven group definitions reproduce the `Damage_Honeycomb.element` layout.
Their `HudLevel` is the highest known component level. A group is marked
partial if any required component could not be read.

| Group | Raw parts |
| --- | --- |
| Radiator | Radiator, WaterPump, Intercooler |
| Engine | Battery, Clutch, GearActuator, Gearbox, HeadGasket, OilPump, OilCooler, Starter |
| Diff | CenterDiff, FrontDiff, RearDiff |
| FrontRight | BrakeCaliperRF, ControlArmRF, DamperMountRF, DamperRF, DriveShaftRF, HubRF, SteeringRodRF, WheelRF |
| RearRight | BrakeCaliperRR, ControlArmRR, DamperMountRR, DamperRR, DriveShaftRR, HubRR, SuspensionArmRR, WheelRR |
| RearLeft | BrakeCaliperLR, ControlArmLR, DamperLR, DamperMountLR, DriveShaftLR, HubLR, SuspensionArmLR, WheelLR |
| FrontLeft | BrakeCaliperLF, ControlArmLF, DamperLF, DamperMountLF, DriveShaftLF, HubLF, SteeringRodLF, WheelLF |

## Warning Lights

RBRHUD's warning lamps are not direct aliases for those seven damage groups.
The analyzed engine lamp is `EngineOff`: it reads the physics field at offset
`0x2B8` (`696`) and is on when that field is zero. Its coolant-temperature lamp
is on only when `physics + 0x14` is greater than `120.0`. A damaged radiator or
water pump may eventually cause those warnings, but neither warning by itself
proves a particular raw damage component failed.

## Required Validation

The offsets are specific to the analyzed RBRHUD/RBR combination and must be
validated before release:

1. Start RBR with RBRHUD enabled and open its F4 Telemetry Viewer.
2. Record baseline values for an undamaged car, then create controlled damage
   to a tyre, radiator, wheel, gearbox, and bodywork.
3. Compare the raw external reads and derived state transitions with RBRHUD's
   telemetry display for each controlled change.
4. Repeat after car changes, restarts, retirement, replay, and return to menu
   to ensure stale pointers cannot produce false events.
5. Test the RBR and NGP versions supported by Pacenote Tool. If the physics
   root or `+0x620` chain changes, fail closed and retain current collision-only
   behavior.

## Reproducing the Static Analysis

The reproducible probe is `build/RBRHUDIdaProbe.py`. Run the x86 `idat.exe`
against a disposable copy of the matching `RBRHUD.dll.idb`; do not open the
live game or modify the original DLL or IDA database. Set
`RBRHUD_IDA_REPORT` to the desired report path before running the script.

The script records imports, strings, call relationships, telemetry vtables,
and the relevant decompiler output. Its hard-coded addresses apply only to the
RBRHUD build analyzed here. A different RBRHUD or RBR build must be analyzed
again and independently validated before any offsets are reused.
