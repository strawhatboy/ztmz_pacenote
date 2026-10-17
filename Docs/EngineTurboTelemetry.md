# Optional engine and induction pressure telemetry

`GameData.Engine` and `GameData.Turbo` are nullable immutable snapshots. Both
carry `Source` and `CapturedAtUtc`, following `HybridTelemetrySnapshot`.
They are exposed to CLR Lua and forwarded in both WebDashboardServer wire paths:
legacy `gameData` and browser/native Lua scene telemetry. Null fields and null
snapshots are omitted from JSON. A missing reading is not zero or healthy.

## Contract

| Snapshot | Field | Meaning |
| --- | --- | --- |
| Engine | CoolantTemperatureCelsius | Nullable float, Celsius |
| Engine | OilTemperatureCelsius | Nullable float, Celsius |
| Engine | OilPressureWarning | Nullable bool, source-reported warning only |
| Engine | CoolantTemperatureWarning | Nullable bool, source-reported warning only |
| Engine | EngineWarning | Nullable bool, source-reported warning only |
| Turbo | PressureBar | Nullable float, bar; do not subtract atmosphere implicitly |
| Turbo | PressureReference | String: `unknown` (default), `gauge`, or `absolute` |

`TurboPressureReference` supplies the three string constants. Strings deliberately
have identical semantics in NeoLua, Fengari and JSON, without enum conversion.
Gauge pressure may be labelled boost; absolute pressure needs an absolute unit
label; unknown reference must be labelled pressure, not boost. Zero is a valid
pressure and never proves the absence of a turbo. No turbo presence, spool,
maximum pressure, damage or warning is inferred from pressure alone.

No provider in this change supplies the warning flags: they remain null rather
than using universal temperature thresholds. Component damage remains in the
separate `CarDamage` snapshot.

## RBR

Source `rbr`. The existing race-data mapping reads coolant at offset `+20` and
pressure at `+24`, converting the latter by `/ 100000f` to bar. This change retains
those offsets and units; atmospheric reference has not been independently
established, so the reference is explicitly `unknown`.

`RBRPowertrainTelemetry` reads the contiguous eight-byte block using the existing
checked `TryReadBytes` helper (success and exact byte count). Invalid/null pointers,
failed/partial reads, nonfinite fields and temperatures below absolute zero do
not become valid zero telemetry. The two values are validated independently.
Menus and subsequent unavailable reads replace previous snapshots with null.
Existing game-specific float fields retain their legacy zero fallback.

## BeamNG.drive

Source `beamng`. Official protocol documentation specifies temperatures in Celsius,
pressure in bar, and `OG_TURBO` as gauge availability:
<https://documentation.beamng.com/modding/protocols/>.
`OG_BAR` is a display-unit preference, not the wire unit; no extra conversion is
applied by the provider.

Verified against the installed 0.39.2.1 source under
`L:\SteamLibrary\steamapps\common\BeamNG.drive\lua\vehicle` on 2026-10-17:

- `protocols/outgauge.lua` writes `watertemp`, `oiltemp`, and
  `turboBoost / 14.504`. `OG_TURBO` depends on the presence of `turboBoost`.
- `powertrain/turbocharger.lua` writes `turboBoost` from the turbo's additional
  pressure in PSI; zero represents no additional pressure. This supports the
  `gauge` reference, without subtracting a nominal atmospheric pressure.
- `electrics.lua` initializes both temperatures to zero. OutGauge has no validity
  bits for them. Both zero temperatures are conservatively omitted, including a
  real exactly-zero reading that cannot be distinguished from missing data.
- Although the public protocol calls `DL_OILWARN` an oil-pressure warning, this
  installed writer obtains it from `electrics.oil`, derived from oil temperature.
  It is not mapped to `OilPressureWarning` or any other shared warning. Numeric
  oil pressure is documented as unimplemented and is also not exposed.

SHA-256 of inspected files:

| File | SHA-256 |
| --- | --- |
| protocols/outgauge.lua | 7C455AF8FD0089EEC9CBC296FC49D4C0BDC720804F19CCB10BC4284E248941BD |
| electrics.lua | 743A6DA1D1D5191A5B0445DE9ABF2A72A12AC5A8DB9B1C546390D194E7C62265 |
| powertrain/turbocharger.lua | 3E97237E6F88D9CD616B22DC5A08897B4B625F471A20A2C9557DE793D2E348BE |

Snapshots use raw OutGauge values rather than the legacy game-specific
normalization (which converts NaN to zero). Nonfinite readings are omitted.
Pressure is exposed only with `OG_TURBO`; a zero pressure with that bit is valid.
The capture timestamp is the OutGauge packet's receipt time, not the time of a
later rally-bridge update. Readings expire after two seconds without a parsed
packet, including when accessed through `CurrentGameData`. A bridge-reported
vehicle change and uninitialization invalidate them immediately. The next packet
re-establishes availability. OutGauge itself carries no useful vehicle identity,
so identifying a replacement vehicle before the bridge reports it is not possible.

## Assetto Corsa Rally

`Engine` and `Turbo` intentionally remain null. `ACRPhysicsData` declares
`WaterTemperature` and `TurboBoostPressure`, and static info declares
`TurboBoostMax`, but declarations inherited from the AC ABI do not verify what
ACR writes, in which units, or with which pressure reference. Other existing ACR
fields already required corrections for its actual writer (e.g. brake Kelvin).

`SharedMemoryGameReader.TryReadUnsafe<T>` returns false after disconnection/read
exceptions. `ACRGameDataReader.ReadAll` currently leaves the corresponding struct
at default when that happens and carries no validity bit to the converter. Thus
mapping these fields directly would also risk reporting failed reads as zero.
Adding ACR support needs writer/unit evidence plus per-page validity/freshness
carried through Windows and Proton paths. This change documents and tests the
unavailable state; it does not invent a mapping or expand memory probing.

## Validation

- `EngineTurboTelemetryTests`: raw bar values independent of display preference,
  zero pressure, unavailable turbo, nonfinite readings, ambiguous zero
  temperatures, no invented oil-pressure warnings, packet timestamps, stale data,
  bridge updates, vehicle changes, disconnect, and unverified ACR fields.
- `EngineTurboSerializationTests`: actual WebSockets for both wire paths,
  pressure-reference strings, valid zero and false values, timestamps, and omitted
  null fields/snapshots on transition to unavailable.
- `RBRPowertrainTelemetryTests`: conversion without atmospheric subtraction,
  checked read failures, independent validation, and production projection
  replacing previous snapshots with null.

No live driving verification was performed for these new shared mappings.
