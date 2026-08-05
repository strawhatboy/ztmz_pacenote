# ZTMZ Pacenote Rally Bridge

This mod is installed by ZTMZ Pacenote Tool into the selected BeamNG.drive user
folder. It does not replace or edit BeamNG Rally Mode files.

1. In BeamNG, enable `OutGauge UDP protocol` and set its target to
   `127.0.0.1:4444`.
2. Restart BeamNG after installing or repairing this mod. In BeamNG's Mods
   Manager, enable `ZTMZ Pacenote Rally Bridge` once. The enabled mod starts
   the local bridge automatically; no Controls binding is required.
3. Start a built-in Rally Mode stage.

The bridge sends Rally state, built-in pacenote metadata, engine RPM limits,
an initial vehicle-damage baseline, and later vehicle damage changes only to
`127.0.0.1:44445`. This default is below Windows' normal dynamic UDP range,
but the desktop tool still checks for an actual local port conflict before it
initializes BeamNG. When BeamNG reports a built-in co-driver call, the desktop
tool plays the corresponding ZTMZ co-driver tokens through its selected output
device. The bridge remains read-only: it does not control the stage, timer,
vehicle, or BeamNG audio.
