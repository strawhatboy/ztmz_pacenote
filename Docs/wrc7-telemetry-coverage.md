# WRC 7 telemetry coverage

This is an implementation checklist, not a claim of complete game support.
Scope: the verified WRC7 executable SHA256
`0FEE0ED14AD05D76FD44B2BA1215BACAFC806DD9D7E8B313634A99C105903633`.

## Shared GameData contract

Every public field in `Base/Game/GameData.cs` is covered below. A default zero
does **not** establish support or mean that a measured input is released.

| Fields | Current WRC7 status / remaining work |
| --- | --- |
| `TimeStamp` | Host sample timestamp, populated. |
| `LapTime` | Raw stage clock, populated; final result including penalties still needs lifecycle validation. |
| `Speed`, `Gear`, `RPM`, `MaxRPM`, `IdleRPM` | Implemented from verified physics object; live read and conversion tests passed. Moving-game/dashboard comparison remains necessary. |
| `Time` | Not implemented; establish source clock semantics independently of stage time. |
| `LapDistance`, `TrackLength`, `CompletionRate` | Route length +0x290 minus RaceStats remaining distance +0x790 gives travelled distance. Forward progression verified live; reverse-stage comparison remains pending. |
| `Throttle`, `Brake`, `Clutch`, `Steering` | Implemented from drive analog channels. Steering channel 4 is negated to match the shared convention; user verified left/right after correction. Clutch includes automatic disengagement. |
| `HandBrake`, `HandBrakeValid` | Independent channel 2 observed at 0 and 1. Valid is set only for finite in-range input. |
| `MaxGears` | Physics gear count minus reverse and neutral; current Fiesta R2 reports 5. |
| `ShiftLightsFraction`, `ShiftLightsRPMStart`, `ShiftLightsRPMEnd`, `ShiftLightsRPMValid` | Native HUD warning point: `(omegaMax - 1 rad/s) * 60 / (2π)`. Equal start/end denote a single warning, with fraction 0/1; no invented progressive LED range. |
| `G_long`, `G_lat` | Not implemented; acceleration candidates exist, but axes, signs and units require confirmation. |
| `SpeedRearLeft`, `SpeedRearRight`, `SpeedFrontLeft`, `SpeedFrontRight` | Implemented from individual wheel radius times angular speed, converted to km/h; live dashboard comparison pending. |
| `SuspensionRearLeft`, `SuspensionRearRight`, `SuspensionFrontLeft`, `SuspensionFrontRight` | Not implemented; physics writer exposes candidates, but travel meaning, units and wheel order are unverified. |
| `SuspensionSpeedRearLeft`, `SuspensionSpeedRearRight`, `SuspensionSpeedFrontLeft`, `SuspensionSpeedFrontRight` | Not implemented; confirm velocity separately from travel. |
| `BrakeTempRearLeft`, `BrakeTempRearRight`, `BrakeTempFrontLeft`, `BrakeTempFrontRight` | Not implemented; no verified source yet. |
| `PosX`, `PosY`, `PosZ` | Implemented from PhysX body world pose minus transformed local COM offset. Guarded by physics DLL hash and body vtable; live comparison pending. |
| `CarPos` | Not implemented; this is race position, not world position or stage distance. Confirm whether meaningful for this mode. |
| `CarDamage` | Not implemented; requires verified component data and mapping to the shared damage contract. |
| `GameSpecificData` | No WRC7 extension currently. Use for verified game-specific information that has no shared field, not to bypass shared fields. |

Unknown sources above are **unverified**, not proven unavailable in the game.
The supplied telemetry patch's packet is not the upper bound of this work.
WRCG's UDP decoder also explicitly sets `HandBrakeValid = false`; it cannot
serve as evidence for a WRC7 handbrake offset.

## Lifecycle is separate from GameData

Car/stage identity, countdown, immediate system “go”, race finish, penalties,
pause, restart, abandonment and process exit must also be completed through the
existing reader events and shared state contracts. Do not infer finish from
frozen time or lost data, or infer countdown solely from a zero stage clock.

## Acceptance

The 2026-10-10 build replaces the earlier unverified speed cache with the
verified PhysX linear-velocity getter's fields. Countdown follows the FMOD
GUI/54321Go or GUI/321Go event timeline on the bridge worker; synchronization
still needs a fresh game launch and live test. Finish requires CrossEndLine
flags plus a processed profile result matching raw time and penalty exactly.
Pause, missing samples and process exit cannot establish a finish. A real
finish including penalty remains to be tested.

Prioritize controls (including handbrake), stage progress, and lifecycle, then
the remaining physics/damage fields. For each field group, record the source,
unit conversion, validity conditions and live observation before claiming it
supported. Unavailable optional data must remain explicitly distinguished from
a measured zero. Preserve WRCG/ACR behavior and share the KT reader between WPF
and Avalonia.
