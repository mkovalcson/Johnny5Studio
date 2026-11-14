# Calibration: relaxed wrist roll gates, animated guide, live gauges, attempt log — 2026-11-12

Changes the shared pose library (Shared/Pose) and the VR Player (Johnny5VR). The VR Editor
only carries the new J5Studio.Pose.dll. Scene protocol stays at version 11. Existing
calibrations keep working.

## Why

The wrist "rotate inward / outward" steps could not be passed. Those steps have three gates
and the panel only said which one was amber, not how far away the measurement was:

1. **Moved:** at least 10° of rotation from the straight wrist.
2. **Opposite:** the second endpoint's rotation direction had to be within 60° of exactly
   opposite the first one's (cosine ≤ −0.5).
3. **Like bending:** the learned roll axis had to be under 65% similar (cosine) to the bend
   axis learned in the bend steps.

Gate 2 was tight for a real wrist: inward and outward palm turns rarely rotate about the
same axis, and gate 3 fails when some bending is mixed into either the bend or the roll
steps. The arm-twist steps have a similar gate (9° of sweep).

## What changed

### Relaxed gates (Shared/Pose/CalibratedArmMotion.cs)

Constants, used by the wizard, the alignment display and profile validation:

- `MinimumEndpointRadians` = 0.17 (10°, unchanged).
- `OppositeMinimumDot` = −0.25: endpoint directions need to be at least
  `OppositeMinimumDegrees` ≈ 104° apart (was 120°). The axis is still the difference of the
  two rotation vectors, which is fine for non-opposite endpoints.
- `AxisSimilarityMaximum` = 0.8: a roll axis may be up to 80% like the bend axis (was 65%).
  `CalibratedArmMotion.Wrist` already separates non-orthogonal axes (`denom = max(0.1,
  1 − cos²)`), so at 80% the pitch/roll split still works with about 2.8× noise gain on
  the shared component. `Valid()` accepts saved profiles up to 81%.

The rejection and alignment texts now give the numbers: "directions 92° apart (need 104°)",
"Axes 86% similar (limit 80%)".

### Live gauges (Shared/Pose/BodyCalibrationAlignment.cs, Johnny5VR/CalibrationPoseGuide.cs)

- `BodyCalibrationWizard.Gauge(frame)` returns a `CalibrationGauge` for steps 5–16: the
  motion kind, degrees moved, degrees between the two endpoints' directions (NaN on a first
  endpoint), roll-axis similarity to the bend axis (NaN outside roll steps), and each
  gate's threshold. For arm twist, `Angle` is the sweep signed toward the required
  direction. `Observe` stores it in `LiveGauge`, which the display snapshot carries.
- `CalibrationPoseGuide.DrawGauges` draws one bar per gate under the pose figure: the
  green band is where the measurement needs to be, the triangle is where it is now, and
  the text gives the number and the threshold. Amber until the gate passes.
- `Summary()` gives the same numbers as text for the log.

### Animated guide (Johnny5VR/CalibrationPoseGuide.cs)

`Draw` takes `time` (seconds). The active hand sweeps from the straight position to the
required endpoint over 1.2 s, holds 0.8 s, eases back and rests, on a 3 s loop. The hinge
bend, the palm-roll marker and the arm-twist sweep all animate. The panel repaints every
100 ms, so the motion runs at about 10 frames a second. With `compact` the figure uses the
upper 235 px of its area and the gauges sit under it; previews (`time` negative) still draw
the end pose only.

### Attempt recording and log (Johnny5VR/VrBodyMotion.cs)

- Starting calibration starts a tracking recording, `Diagnostics/tracking-calibration-<date-time>.bin`
  (same format as the Follow recordings, see VR-Follow-Wrist-Pivot.md). It closes when
  calibration completes or is cancelled.
- The follow-problems log (`Diagnostics/follow-problems-<date-time>.txt`) now has one line
  for calibration start, each accepted step (with its feedback, including the wrist-pivot
  note), each new rejection with the gauge numbers, Back, and completion. For example:
  `Calibration step 8 rejected: That rotation is 86% similar to wrist bending (limit 80%)… · moved 31°/10°, opposite 118°/104°, like bending 86%/80%`.

With the recording and the log, a calibration that won't pass can be examined afterwards
without the headset.

## Checks

- Player `--check`: 4,097 pass (11 new): endpoints 110° apart accepted and 80° apart
  rejected; roll axis 75% like bending accepted, 85% rejected; gauge values at the first
  and second roll endpoints, at a straight wrist and after returning to the first endpoint;
  the snapshot carries the gauge; the guide changes with time; calibration start records and
  logs. The guide and gauges are rendered to Diagnostics/calibration-guide-animation.png.
- Editor `--vr-pose-check`: 183 pass. Player/editor smoke test passes.
- Not yet tried in the headset.

## Files

- Shared/Pose: CalibratedArmMotion.cs, BodyTrackingCalibration.cs, BodyCalibrationAlignment.cs
- Johnny5VR: CalibrationPoseGuide.cs, VrBodyMotion.cs, VrBodyMotion.CalibrationDisplay.cs,
  TrackingRecorder.cs (file prefix), BodyMotionChecks.cs
- Backups: Archive/DesktopChanges/vr-calibration-roll-20261112
- Update: Releases/VR/0.9.0/J5Studio-VR-Calibration-Roll-Gauges-update.zip (cumulative on
  top of J5Studio-VR-Follow-Wrist-Pivot-update.zip)

## Tolerances — 2026-11-12, later

From the first real calibration with the attempt log (wrist pivots measured at 16.8 cm left
and 13.5 cm right), two steps were marginal:

- **Wrist bend/rotate drift.** `WristDriftAllowance` assumed an 8 cm grip lever:
  `8 cm + min(8, 2·8·sin(angle/2))`. The left "bend down" step was rejected 25 times at
  11.1 cm against 11.0 allowed. It now assumes `BodyTrackingCalibration.AssumedGripRadius`
  = 17 cm with a 14 cm cap: `8 cm + min(14, 2·17·sin(angle/2))`, so a 29° bend allows
  about 16 cm and a 21° one about 14 cm. (The wrist pivot is not yet known during a side's
  own wrist steps, which is why a fixed assumption is needed.)
- **Straight arms forward.** `FitShoulder` rejected the sphere fit when the centre was
  more than 15 cm from the midpoint of the arms-down and arms-up points. Before the pivot
  is known the three points carry the grip offset, which rotates with the controller, so
  the fit was rejected 12 times with "move forward 19 cm". The limit is now
  `ShoulderFitMaximum` = 22 cm, and the message gives the reach, the error and the limit.
  Once the wrist pivot is learned the shoulder is re-measured from the wrist anyway
  (VR-Follow-Wrist-Pivot.md).

Checks: a 29° bend allows 13 cm of travel and rejects 20 cm; a fit skewed by 15–22 cm is
accepted; an inconsistent fit reports its numbers. 4,101 player checks pass.
Update: Releases/VR/0.9.0/J5Studio-VR-Calibration-Tolerances-update.zip.
Backups: Archive/DesktopChanges/vr-calibration-tolerances-20261112.
