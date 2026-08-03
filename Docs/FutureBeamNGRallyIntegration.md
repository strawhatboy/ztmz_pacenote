# BeamNG.drive Rally Mode Integration

## Status

An initial BeamNG.drive provider is implemented in source. It combines standard
OutGauge dashboard telemetry with a required, read-only Rally Mode companion
bridge. The bridge still needs in-game validation against the BeamNG.drive
version used for the next release because Rally Mode's Lua model is not a
documented stable external API.

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
- While built-in Rally Mode is active, the bridge attempts to expose stage ID,
  stage name, countdown/active/finished state, time, route distance, total
  distance, completion, and the current built-in pacenote.
- BeamNG's vehicle damage tracker sends only state changes. The provider maps
  new punctures, meaningful body damage, and engine/radiator faults into the
  existing damage-event contract, and keeps a compact current damage summary
  in BeamNG-specific telemetry for diagnostics and future dashboard work.
- The first complete pacenote list received for a stage is saved for inspection
  under `games\\beamng\\rally-stages` in Pacenote Tool's data folder. The
  exported JSON keeps BeamNG's original IDs, text, visual data, and distance
  call values.
- The bridge supplies stage distance and built-in pacenote metadata. Pacenote
  Tool's own normal replay/script audio remains enabled by default when a
  matching pacenote recording is selected.
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

## Compatibility And Validation

BeamNG's documented protocols do not include Rally stage state or pacenotes;
those fields are read defensively from the game-engine Rally Manager. Before
shipping this provider, validate a real special stage for the following:

- OutGauge values update correctly in the dashboard.
- Bridge start, stage load, countdown, active driving, finish, restart, and
  return-to-menu transitions do not leave stale state in Pacenote Tool.
- Stage time and route progress match BeamNG's Rally HUD.
- Current pacenote and exported complete list match the built-in Rally UI.
- The bridge reports unavailable state rather than causing game Lua errors
  outside Rally Mode or after a BeamNG update.

The exported pacenote data is currently an inspection and recording source.
It is not yet a complete mapping from BeamNG pacenote semantics to every
Pacenote Tool voice package or UI language.

## References

- BeamNG protocol documentation: <https://documentation.beamng.com/modding/protocols/>
- BeamNG extension documentation: <https://documentation.beamng.com/modding/programming/extensions/>
- BeamNG user-folder documentation: <https://documentation.beamng.com/support/version/>
- BeamNG.drive v0.38 Rally Mode release notes: <https://www.beamng.com/game/news/patch/beamng-drive-v0-38/>
