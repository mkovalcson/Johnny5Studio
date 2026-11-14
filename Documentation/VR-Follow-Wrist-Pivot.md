# Follow wrist pivot and tracking recorder — 2026-11-12

Changes the shared pose library (Shared/Pose, linked into the VR Player and the VR Editor)
and the VR Player (Johnny5VR). The VR Editor's behaviour is unchanged; it only carries the
new J5Studio.Pose.dll. The scene protocol stays at version 11.

## The problem

The player used the controller's own origin as the hand. On an Index controller that origin
is in the handle, several centimetres from the wrist. So:

- Bending or rolling the wrist swung the "hand" point a few centimetres, which the solver
  read as elbow and shoulder movement. Waving the hand with a still arm moved the robot's
  arm.
- Arm lengths and shoulder positions were measured to the controller, not the wrist (the
  forearm included the grip offset).
- The wrist calibration steps had to allow 8–16 cm of controller drift to cope.

## What changed

### Calibration learns the wrist pivot (Shared/Pose/BodyTrackingCalibration.cs)

- `BodyTrackingCalibration.LeftWristPivot` / `RightWristPivot`: controller-local offset
  (metres) from the controller origin to the wrist pivot. `LeftWristPivotFit` /
  `RightWristPivotFit`: fit residual (RMS metres). Zero pivot means "use the controller
  origin", which is what older profiles get. **No profile version bump:** existing
  profiles load unchanged; recalibrate to get a pivot.
- `Wrist(controllerPose, left)` returns the hand point. `HasWristPivot(left)` says whether
  one was learned.
- `FitWristPivot(poses)`: least squares for the controller-local point that moves least
  across a set of poses in one common frame. Row-vector convention: `c·A = b`,
  `A = n(I − R̄R̄ᵀ)`, `b = −Σ(pᵢ − p̄)Rᵢᵀ`. Rejects pose sets that rotate about a single
  axis (the pivot along that axis is unobservable).
- The wizard calls `ApplyWristPivot` when the second roll endpoint of a side is accepted
  (step 8 for the left, 12 for the right). The poses are the neutral pose plus that side's
  four wrist endpoints, in the body-local frame (`LocalPose`). If the pivot is within 25 cm
  and the residual within 3 cm, the profile keeps it and **re-measures that side from the
  wrist**: shoulder fit (steps 0–2), arm lengths (step 3), neutral orientation and twist
  neutral (step 4) and the pitch axis (steps 5–6 / 9–10); the roll axis is then learned as
  before with the corrected geometry. If anything fails, the side keeps the grip-origin
  measurements and the step feedback says so. The arm-twist steps (13–16) that follow use
  the corrected hand automatically.
- Every hand position in the wizard and in the alignment display now goes through
  `Hand(frame, left)`, which is the controller origin until the pivot is known.
- The step feedback reports "Left wrist pivot 9.1 cm from grip origin (fit 0.6 cm)", and
  the calibration panel's arm-lengths line shows both pivots.

### Solver uses the pivot (Shared/Pose/BodyTrackingSolver.cs)

`Solve` takes the hand as `profile.Wrist(handPose, left)`. Wrist pitch, roll and the finger
mapping are unchanged. With a pivot, bending only the wrist leaves the estimated elbow and
shoulder within 1° (checked).

### Tracking recorder and replay (Johnny5VR/TrackingRecorder.cs)

- While Follow runs, raw frames (time step, validity, headset and both controller poses,
  finger curls, finger source) are written to `Diagnostics/tracking-<date-time>.bin`.
  About 190 bytes per frame, under 1 MB per minute. One file per Follow session.
- Replay: `Johnny5VR.exe --replay-tracking <file.bin> [--calibration <profile.json>]
  [--out <file.csv>] [--config <folder>]`. Default calibration is
  `<Config>/VR/BodyTrackingCalibration.json`; default output is the recording's name with
  `.csv`. One row per frame with every solver target; prints frame count, duration,
  invalid frames and dropouts. The solver is re-centred on the first valid frame, so robot
  placement starts at zero.
- This is how solver changes can be tested against real movements without the headset:
  record once, replay with any calibration or code change.

## Checks

- Player `--check`: 4,086 pass (32 new):
  - `FitWristPivot` recovers a known pivot from rotations about a fixed wrist, and rejects
    single-axis pose sets.
  - The whole 21-step calibration run with the controller origin displaced 9 cm from the
    wrist accepts every step, learns both pivots within 5 mm, and re-measures arm lengths and
    shoulders to match a grip-at-wrist calibration within 1 cm.
  - Bending only the wrist moves the estimated elbow and shoulder under 1° with the pivot,
    and over 3° without it.
  - Pivots round-trip through the saved profile; a profile without the fields loads and
    uses the controller origin.
  - Recorder round trip and replay CSV.
- Editor `--vr-pose-check`: 183 pass (the editor only carries the library).
- Player/editor smoke test passes.
- Not yet tried in the headset. The first real calibration will show the pivot distance;
  expect roughly 5–15 cm on an Index controller. If the fit residual is over 3 cm the step
  feedback says the pivot wasn't resolved, which means the forearm moved during the wrist
  steps.

## Files

- Shared/Pose: BodyTrackingCalibration.cs, BodyCalibrationAlignment.cs, BodyTrackingSolver.cs
- Johnny5VR: TrackingRecorder.cs (new), VrBodyMotion.cs, VrBodyMotion.CalibrationDisplay.cs,
  Program.cs, BodyMotionChecks.cs
- Backups: Archive/DesktopChanges/vr-wrist-pivot-20261112
- Update: Releases/VR/0.9.0/J5Studio-VR-Follow-Wrist-Pivot-update.zip (cumulative on top of
  J5Studio-VR-Follow-Scene-Rate-Treads-update.zip; replaces Johnny5VR.dll, J5Studio.Pose.dll
  and Editor/J5Studio.Pose.dll)

## Related ideas not done

From the body-tracking review of 2026-11-12: body-yaw estimate robust to one-arm reaches,
elbow hint from controller orientation, 1:1 roll and arm-twist gains with a fade instead of
a 15° cliff, calibrated neck pivot. See Future-Features-List.md.
