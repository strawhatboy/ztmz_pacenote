# Shared car damage coverage

The shared snapshot preserves raw source components and projects independently
available parts for WPF, Avalonia, native overlay and Web dashboards.
`*Severity` uses ZTMZ's 0–4 scale. A zero is not evidence of availability:
dashboards must also check the corresponding `*Available` property.

Gearbox and overall bodywork have independent projections. Bodywork aggregates
located body panels without pretending that the game identified a specific door.
The shipped dashboard hides unavailable parts, highlights body edges for bodywork
damage, and uses the powertrain icon for gearbox damage.

| Provider | Current source / scope | Remaining limitation |
| --- | --- | --- |
| BeamNG | damageTracker raw entries; engine, cooling, gearbox, wheels, tyres, brakes, suspension, body panels | Unknown entries remain raw; not every powertrain device is a differential |
| RBR | Native part and HUD-group snapshots; gearbox/actuator/clutch separated from engine | Severity derives from recovered HUD levels |
| ACR | Current car damage-component parts, including Engine, Radiator, Bodywork, GearBox, differential and per-corner suspension/brakes/wheel geometry | Wheel geometry is not tyre condition; unknown parts remain `acr.*` |
| DR2 | Read-only native engine, radiator, gearbox, bodywork; four-corner brakes, damper/spring, wheel geometry and discrete tyre states | Tyres expose puncture/blowout state, not tread life; not every physical part is represented |
| WRC7 / WRCG / DR1 / EA WRC | No shared component-damage reader verified | Do not fabricate healthy component states |

Raw snapshots remain available for diagnostics. Adding an icon or a normalized
mapping does not itself prove a new native memory field; new readers require
source-specific validation. DR2 details are in `dr2-telemetry.md`.
