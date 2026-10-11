# KT shift warning sources

Both providers report the game's HUD warning point, not a calculated optimum
shift RPM. Their HUDs do not supply ACR-style progressive LED thresholds.
`ShiftLightsRPMStart == ShiftLightsRPMEnd` therefore intentionally describes
one threshold; `ShiftLightsFraction` is zero below it and one at/above it.
The shared dashboard's existing range comparisons support this degenerate range
without division. Unavailable metadata clears validity rather than retaining
another car's threshold.

## WRC Generations

For the verified executable already supported by the memory reader, the CARS.CFG
loader stores `HudMaxRpm` and `HudRedRpm` as integers at car-definition offsets
0x1f8 and 0x1fc (RVA 0x51d6a5 / 0x51d6dd). The active profile already supplies
this car definition for names/class. Read both together and require
100 <= red <= max <= 30000.

HUD initialization at 0x57de00 computes red/max; update at 0x57fe55 compares the
current gauge ratio to that value and triggers the warning animation. This is
independent of the UDP engine maximum RPM. For example, installed Rally1
definitions specify HUD max 8500 and red 7500.

## WRC 7

HUD update at 0x1313b7 subtracts the float constant 1.0 from physics omegaMax,
then computes integer `omega * 100 / (omegaMax - 1)` and animates the gauge at
100. The provider reports the onset of this warning in RPM; it keeps warning
active above that point, without reproducing the HUD's integer animation band.
Neither the torque-peak field nor a guessed percentage is used.

Live read on 2026-10-11: Hyundai i20 Coupé WRC, Luceram - Col St Roch,
maximum RPM 7161.972, warning RPM 7152.423, validity true. This confirms the
production reader; the visual warning at high RPM still needs end-to-end checking.

132 KT tests passed, including threshold boundaries, selected-car metadata,
metadata disappearance and existing lifecycle/telemetry tests.
