# BeamNG.drive Rally Mode Integration

## Status

The BeamNG.drive provider combines standard OutGauge dashboard telemetry with a
required, read-only Rally Mode companion bridge. It has been exercised against
the shipped Rally Mode stage `SS 洛克伍德`
(`small_island_rallyStage_001-mixed2`), including its complete 61-call stage
export and the default ZTMZ co-driver vocabulary. Rally Mode's Lua model is not
a documented stable external API, so this integration must still be checked
after BeamNG updates and against community-authored stages.

No BeamNG game installation is overwritten. The bridge is installed
as a ZIP mod in the selected BeamNG user folder.

## Implemented Scope

- OutGauge is received on `127.0.0.1:4444` by default. It supplies speed,
  current RPM, gear, throttle, brake, clutch, fuel, temperatures, turbo, and
  dashboard light data. The companion bridge supplements it with the active
  vehicle's maximum and idle RPM for correct dashboard scaling.
- The companion bridge listens on `127.0.0.1:44445`. That default is below
  Windows' normal dynamic UDP range, avoiding ordinary ephemeral-port
  allocation; the tool still reports a real local bind conflict. It sends
  versioned JSON only from the local computer and is ignored unless it has the
  `ZTMZ_BNG_RALLY` protocol marker.
- While built-in Rally Mode is active, the bridge exposes stage ID, stage name,
  countdown/active/finished state, time, route distance, total distance,
  completion, the complete current-stage pacenote list, and the current
  built-in pacenote.
- The bridge requests a baseline snapshot from BeamNG's vehicle damage tracker
  when it attaches, then receives later state changes. The provider maps new
  punctures, meaningful body damage, and engine/radiator faults into the
  existing damage-event contract, and keeps a compact current damage summary
  in BeamNG-specific telemetry for diagnostics and dashboard work. The
  baseline makes the Car Damage dashboard visible for an undamaged vehicle
  without producing a false damage alert.
- Meaningful bodywork changes from one bridge update are condensed into one
  collision call at the highest severity, so a single impact does not produce
  one call per body panel. Radiator, water-pump, and engine faults use
  dedicated system tokens and are edge-triggered with repeat protection. They
  follow the existing collision-sound setting. Voice packages can provide
  `radiator_damage`, `water_pump_damage`, and `engine_damage` recordings;
  packages without them fall back to the established `caution` recording.
- The first complete pacenote list received for a stage is saved for inspection
  under `games\\beamng\\rally-stages` in Pacenote Tool's data folder. The
  exported JSON keeps BeamNG's original IDs, source text/language, structured
  projection data, visual data, and distance call values.
- The stage export is an inspection, recording, and coverage-testing artifact.
  It is not read to schedule audio playback.
- During racing, BeamNG remains the timing authority: the bridge sends the
  current native pacenote, and Pacenote Tool translates that event immediately
  into selected ZTMZ co-driver tokens and plays it through the configured audio
  output.
- Structured pacenotes are translated from BeamNG's language-neutral projection
  data. Legacy English freeform notes use a conservative text fallback.
  Legacy freeform notes with a non-English source language are exported and
  logged, but are not partially translated into potentially incorrect calls.
- If the bridge port cannot be opened or the mod is not installed, the provider
  reports the missing setup and exposes an install/repair action.

MotionSim is not consumed by this initial provider. It can be added later if
world position or motion is needed beyond OutGauge.

## Player Setup

1. Select `BeamNG.drive Rally Mode` in Pacenote Tool and open its game settings.
2. Select the BeamNG.drive user folder. The standard default is
   `%LOCALAPPDATA%\\BeamNG\\BeamNG.drive\\current`; Pacenote Tool reads
   `%LOCALAPPDATA%\\BeamNG\\BeamNG.drive.ini` first so it also follows a custom
   user folder selected in the BeamNG launcher.
3. Choose **Install / repair Rally bridge**. This writes
   `mods\\ztmz_pacenote_rally_bridge.zip` below that user folder.
4. In BeamNG, enable OutGauge and point it to `127.0.0.1:4444` for dashboard
   telemetry.
5. Restart BeamNG after installation, then enable **ZTMZ Pacenote Rally
   Bridge** once in BeamNG's Mods Manager. The enabled bridge starts
   automatically and sends local Rally Mode traffic on `127.0.0.1:44445`; no
   BeamNG Protocol entry or Controls binding is expected.

The bridge is intended for BeamNG's built-in Rally Mode, with older community
rally mods disabled during validation.

## Pacenote Coverage And Validation

BeamNG's documented protocols do not include Rally stage state or pacenotes;
those fields are read defensively from the game-engine Rally Manager. Before
shipping this provider, validate a real special stage for the following:

- OutGauge values update correctly in the dashboard.
- Bridge start, stage load, countdown, active driving, finish, restart, and
  return-to-menu transitions do not leave stale state in Pacenote Tool.
- Stage time and route progress match BeamNG's Rally HUD.
- Current pacenote and exported complete list match the built-in Rally UI.
- The countdown and live co-driver calls play once at the same points as the
  built-in Rally Mode calls.
- The bridge reports unavailable state rather than causing game Lua errors
  outside Rally Mode or after a BeamNG update.

The exported official `SS 洛克伍德` record contains 61 pacenotes, all of which
map to the current default ZTMZ co-driver vocabulary. The translator also has
coverage tests for BeamNG's current standard structured modifier taxonomy.
This does not guarantee support for arbitrary community freeform phrases,
custom structured fields, future BeamNG schema changes, or every installed
ZTMZ voice package. Unknown data is retained in the export and logged for
follow-up mapping rather than discarded silently.

## References

- BeamNG protocol documentation: <https://documentation.beamng.com/modding/protocols/>
- BeamNG extension documentation: <https://documentation.beamng.com/modding/programming/extensions/>
- BeamNG user-folder documentation: <https://documentation.beamng.com/support/version/>
- BeamNG.drive v0.38 Rally Mode release notes: <https://www.beamng.com/game/news/patch/beamng-drive-v0-38/>
