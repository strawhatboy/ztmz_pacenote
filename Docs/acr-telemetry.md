# ACR telemetry field sources

## Shared-memory conversion

The Windows reader and Proton relay share `ACRGameDataConverter`. The converter
uses the formatted lap clock when the integer millisecond clock is absent,
reports stage length and bounded completion, and reads the player position from
slot zero in ACR's single-player layout (where `activeCars` can remain zero).

Physics `MaximumRPM` is an integer at byte 588, not a float. Brake temperatures
are Kelvin in the native writer and are converted to Celsius. Wheel angular
velocity requires a radius before conversion to km/h. Steering retains its sign:
left negative, right positive. Clutch uses `1 - raw`, matching the game's own
telemetry presentation.

## Windows supplemental reader

`ACRVehicleMemoryReader` supplies fields missing from shared memory using read-only
process access. Both WPF and Windows Avalonia use this same implementation.
Executable verification runs asynchronously; unverified builds retain normal
shared-memory telemetry without enabling version-specific reads.

Verified executable SHA-256:
`42C359B0CD24774A86441710FDC7D7DB3BC06B57B3FF30CB9EA5D56FB71448FB`.

The local player/controller/pawn chain identifies the current vehicle. Ownership,
vtable, pointer stability and value bounds are checked before accepting a sample.
Menus, loading and vehicle replacement can temporarily make the supplement
unavailable; the reader retries without blocking the telemetry timer on discovery.

| Field | Native source |
| --- | --- |
| Handbrake | Simulation control value, including forced-handbrake flag |
| Shift-light thresholds | Vehicle parameters' four RPM thresholds, first and last |
| Maximum forward gears | Engine gear-ratio array, excluding reverse and neutral |
| Idle RPM | Engine parameters' serialized `minIdle` value |
| Wheel radii | Each owned wheel's cylindrical rigid-body radius |

On 2026-10-11, live Citroen Xsara WRC telemetry showed five forward gears,
7500 maximum RPM, 525 minimum idle RPM, 6700–7300 shift thresholds and 0.324 m
wheel radii. Handbrake transitions 0 → 1 → 0 and negative/positive steering
were captured through the production reader. These are observed values, not
defaults for other vehicles.

## Limits and validation

Suspension travel is available; suspension velocity has no verified source and
is not fabricated. The displayed lap clock is not
claimed to include penalties. The supplemental process reader is Windows-only;
Proton currently receives the shared-memory conversion improvements, but not the
additional process-memory fields.

`ACRTelemetryTest` covers raw layout, units, unavailable fields, malformed data,
vehicle ownership and pointer replacement. Together with the existing ACR tests,
180 ACR/KT tests passed including the damage and shift-warning additions. Live control verification does not replace
end-to-end dashboard/replay testing across vehicle changes.

## Component damage

The Windows C# reader samples the current pawn's `UCarDamageComponent` at 5 Hz.
It does not add work to the injected bridge or the game's render thread.
Both actor component arrays (0x280 / 0x290), component vtable (RVA 0x9c11ca8),
owner, part outer, array bounds and pointer stability are checked. Parts are
at component+0x118; each part exposes InstanceId at 0x58, maximum life at 0xb8,
state definitions at 0xc0, state index at 0x130 and remaining life at 0x134.
The verified FName pool is RVA 0xb27ba80.

State index is not severity: New and Normal both have native Type=1 and map to
healthy. Light/Severe/Critical map to severity 2/3/4. Native state definitions
provide vehicle-specific thresholds; no generic damage percentage is guessed.
All parts retain life, maximum life and native state in `CarDamageSnapshot`.
Existing dashboard fields receive engine, radiator, corner brakes/suspension/
wheel geometry, bodywork and differential damage. Other parts retain distinct
`acr.*` component IDs; gearbox failure is not mislabeled as engine failure.
Tyre damage, fuel and tyre pressure are not inferred from these parts.

Live production-reader validation on 2026-10-11 read 27 healthy parts in ACR.
Twenty supplemental reads including damage averaged 1.11 ms, maximum 1.64 ms,
in the ZTMZ process. Actual damaged-state visual testing remains outstanding.
