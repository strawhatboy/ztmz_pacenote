# Dashboard color roles

Use consistent meanings across bundled dashboards while retaining each design's
shades, gradients, artwork, and contrast. This is a semantic palette, not one
mandatory RGB value for every design.

| Role | Treatment |
| --- | --- |
| Normal readings | White/light foreground on dark panels; dark foreground on light panels. Secondary text and inactive tracks are gray. |
| Throttle | Green. |
| Service brake | Red. |
| Clutch | Blue. |
| Handbrake | Red applied, gray released. Classic uses separate left/right half-bars for handbrake/brake; pedal history uses dashed red for handbrake versus solid red for brake. |
| Steering | Red locator: original WRC curved dot/trail, Classic angular cursor, Liquid Glass dot. A locator is a control indication, not a fault. |
| Force feedback | White signed fill, amber at normalized magnitude >= 0.99. No FFB text label. This is output saturation proximity, not measured wheel torque or proof of hardware clipping. |
| Hybrid battery charge | Neutral white/gray: stored energy remaining. WRC dash 2 uses a gray fill behind white charge text for contrast. |
| Hybrid activity | Cyan deployment, readiness and regeneration; standby/unknown state gray. Labels and shapes distinguish states independently of hue. |
| RPM/shift | Green through amber to red, with the existing shift-point warning. Liquid Glass fills 18 LEDs individually across the provider's start/end range, falling back to configured green/red RPM thresholds. |
| Component damage | Amber for reported severity 1-2, red for severity >= 3. Unsupported components remain unavailable. |
| Temperature/oil/engine warnings | Normal readings neutral; red for reported warning flags. Do not invent temperature limits from generic numbers. |
| Time delta | Green ahead, red behind, with signed numbers. |

The compact WRC dash 2 hybrid slot has no BOOST text: an upward chevron marks
deployment, a downward chevron regeneration, a ring readiness, and a muted dash
standby/unknown state. Other hybrid dashboards retain their localized state text.
The WRC dash 2 clutch fill uses blue vector segments along its original curved
column instead of the amber raster fill.

Shapes, positions and labels must carry meaning as well as color. Avoid using
amber/red to decorate normal hybrid operation. Existing chroma-key mode retains
its compatibility substitutions for green/blue; those are technical keying
exceptions to the normal visible palette.

Verification: actual browser previews with active pedals/handbrake/hybrid,
browser smoke checks for all 18 dashboard packages, native built-in dashboard
render checks, and CLR-backed Liquid Glass regression tests.

## Turbo pressure gauge

Liquid Glass keeps the exact numeric pressure and adds a white arc in the same
column. Gauge pressure defaults to -1..4 bar, filling away from the marked zero
toward vacuum or positive boost. Absolute pressure defaults to 0..5 bar abs and
is never converted to gauge pressure. The two maxima have separate dashboard
settings (1..10 bar), translated in English and Chinese. No telemetry provider
currently supplies a per-car pressure limit; these are display scales, not
warning thresholds. Values outside the scale clamp the arc while the number
retains the reported value. Unknown pressure reference stays numeric without a
calibrated scale. Missing/nonfinite readings still collapse the entire column.
