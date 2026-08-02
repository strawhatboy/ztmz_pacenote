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

- OutGauge is received on `127.0.0.1:44444` by default. It supplies speed,
  RPM, gear, throttle, brake, clutch, fuel, temperatures, turbo, and dashboard
  light data for the existing telemetry UI.
- The companion bridge listens on `127.0.0.1:44445`. That default is below
  Windows' normal dynamic UDP range, avoiding ordinary ephemeral-port
  allocation; the tool still reports a real local bind conflict. It sends
  versioned JSON only from the local computer and is ignored unless it has the
  `ZTMZ_BNG_RALLY` protocol marker.
- While built-in Rally Mode is active, the bridge attempts to expose stage ID,
  stage name, countdown/active/finished state, time, route distance, total
  distance, completion, and the current built-in pacenote.
- The first complete pacenote list received for a stage is saved for inspection
  under `games\\beamng\\rally-stages` in Pacenote Tool's data folder. The
  exported JSON keeps BeamNG's original IDs, text, visual data, and distance
  call values.
- Pacenote Tool plays BeamNG Rally Mode pacenotes through the companion bridge.
  The bridge is required for every BeamNG provider initialization.
- If the bridge port cannot be opened or the mod is not installed, the provider
  reports the missing setup and exposes an install/repair action.

MotionSim is not consumed by this initial provider. It can be added later if
world position or motion is needed beyond OutGauge.

## Player Setup

1. Select `BeamNG.drive Rally Mode` in Pacenote Tool and open its game settings.
2. Select the BeamNG.drive user folder. The usual default is under
   `%LOCALAPPDATA%\\BeamNG.drive\\current`, but a custom user folder selected in
   the BeamNG launcher must be selected here instead.
3. Choose **Install / repair Rally bridge**. This writes
   `mods\\ztmz_pacenote_rally_bridge.zip` below that user folder.
4. In BeamNG, enable OutGauge and point it to `127.0.0.1:44444` for dashboard
   telemetry.
5. Bind and use `Toggle ZTMZ Pacenote Rally Bridge` in BeamNG Controls before
   loading a built-in Rally Mode stage. The action explicitly loads the Lua
   extension and starts or stops local bridge traffic.

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
- BeamNG user-folder documentation: <https://documentation.beamng.com/support/userfolder/>
- BeamNG.drive v0.38 Rally Mode release notes: <https://www.beamng.com/game/news/patch/beamng-drive-v0-38/>
