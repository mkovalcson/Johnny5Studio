# Requirement: animate the robot's treads in the URDF view

## 1. Context

The body model now ships pre-baked animation frames for its treads. The tread
pattern repeats every link, so 12 frames, each shifting every link one twelfth
of a link along the belt path, loop seamlessly. The editor only has to show
one frame at a time and turn the front roller hubs to match.

Everything the editor needs is described in:

    Models/Meshes/Body/Treads/tread_animation.json

This feature is display-only. It adds no URDF joints, drives no hardware, and
does not change the servo control list.

## 2. Manifest (what the generator writes)

| Field | Meaning |
| --- | --- |
| `phases` | Number of frames in one loop (12). |
| `phaseStep_m` | Belt travel between consecutive frames (0.00261 m). |
| `linkPitch_m` | One link; `phases x phaseStep_m`. |
| `sides.left` / `sides.right` | Per track. |
| `...layers[]` | Each layer is one URDF visual whose geometry is swapped: `urdfLink`, `urdfVisual`, `material`, and `frames[]`, an ordered list of STL paths relative to the `Models` folder. Frame 0 is identical to the static mesh in the URDF. |
| `...rotators[]` | Parts that spin with the belt: `urdfLinks[]` (the front hub, its cap and five bolts), `axisWorld`, `centreWorld` (base_link frame, metres), `radius_m`, `degreesPerPhase`. |
| `frameSpace` | Frame meshes use the same local frame as the static visual they replace, so swap the visual's geometry and leave the link transform alone. |
| `direction` | Increasing frames move the bottom run forward relative to the body, which drives the robot **backward** over the ground. Decreasing frames drive it **forward**. Rotators turn by `degreesPerPhase` per increasing frame. |

## 3. Requirements

### Loading

1. After the URDF loads, look for the manifest next to the body meshes. If it
   is absent or invalid, do nothing: the treads stay static and no error is shown.
2. Preload every frame mesh once through the existing STL loader, apply the
   layer's `material`, and freeze them. There are 48 frames of about 250
   triangles each.
3. Resolve each layer's `urdfVisual` to the model the loader created for it,
   and each rotator's `urdfLinks` to their visuals. Log a warning and skip any
   that are not found.

### Playback

4. Keep an accumulated belt distance per side, `d_left` and `d_right`, in metres.
5. Each rendered frame, advance each distance by `-speed x dt`, where `speed` is
   that side's belt speed in m/s and positive speed means driving forward.
   The minus sign makes positive speed step the frames in decreasing order, as
   the manifest specifies.
6. Frame index: `floor(d / phaseStep_m) mod phases`, kept non-negative. Using
   distance rather than counting frames keeps any speed smooth and exact.
7. When a side's index changes, swap each of its layers' geometry to that
   frame. Do nothing when it has not changed.
8. Rotate each rotator's links about `axisWorld` through `centreWorld` by
   `(d / phaseStep_m) x degreesPerPhase`, applied on top of their static
   transforms. Use the continuous value, not the rounded index, so the hub turns
   smoothly between tread frames.
9. Pause playback while the view is hidden or minimised, as other per-frame
   work in the view already does.

### Control

10. Provide separate left and right belt speeds, so turning on the spot shows
    the tracks running in opposite directions.
11. First version: a small control in the URDF view with an on/off toggle, a
    speed slider (for example -0.5 to +0.5 m/s, centre detent at 0) and a
    "linked / independent" option for the two sides.
12. Optional, later: two timeline controls (for example `TreadLeft`,
    `TreadRight`) recorded and played back like other controls, so tread
    motion can be animated in a sequence.
13. Speed changes take effect immediately; the belt does not jump, because the
    accumulated distance is continuous.

### Undocked window, Meeting mode

14. The embedded and undocked URDF views animate independently; each keeps its
    own distances.
15. Tread animation honours the Clean Undocked URDF View option (no extra
    on-screen controls in the clean view).

## 4. Acceptance tests

Runnable in the Checks harness with no WPF view and no hardware, by testing the
pure logic:

1. `phases = 12`, `phaseStep = 0.00261`: distance 0 gives index 0;
   `0.00261 x 5.5` gives index 5; `-0.00261 x 0.5` gives index 11 (wraps, non-negative).
2. Forward speed (positive) for one step's worth of time moves the index from
   0 to 11, then 10 (decreasing order).
3. Rotator angle at distance `0.00261 x 207` (one hub circumference) is
   -360 deg to within 0.1 deg.
4. Manifest parsing: all 48 frame paths resolve relative to the Models folder;
   a missing manifest yields "no animation" without an exception.
5. A missing `urdfVisual` produces a warning and animation of the remaining
   layers.

Manual check in the editor: with speed on, the pads march down the front run
and round the front roller while the hub bolts turn with them, and nothing
pops except under the shroud's wrap, where links appear and vanish unseen.

## 5. Notes for the implementer

- The generator (`Models/Body/build_robot.py`) regenerates the frames and the
  manifest every run; frame count is `TREAD_PHASES`.
- The belt band (`tread_band_*`) is uniform, so it has no frames and never
  changes.
- Frame 0 equals the static URDF mesh, so the model looks identical when the
  feature is off.
