# TipToe body control (B_TipToe)

A URDF-only body control for the full-body Johnny 5 model. Available in the Desktop
editor, the VR Editor and the VR Player.

- 2026-11-12: first version (tracks rotate up to 45°, front planted, caster arm swings about the drum).
- 2026-11-13: **CAD caster four-bar** (this document). The caster is rebuilt from the CAD side views:
  one-piece upper brace, hinged lower brace, longer body section, tracks to 61.3°, and the robot
  tips back onto the caster wheel.

## What it does

Both track assemblies rotate about the centre of the pelvis drum (`body_pelvis_drum_link`),
front down and rear up, by up to 61.27 degrees. At that angle the front-top tread is vertical
*to the body*. The caster is a four-bar linkage that swings down with the tracks. The
CAD shows the caster wheel slightly off the floor at full tiptoe; on the model the whole robot instead tips
back on its planted front until the caster wheel touches the floor, so the front tread ends a
little short of vertical to the floor.

| TipToe command | Track rotation (to the body) | Upper / lower brace swing | Robot tips back | Body rise | Body forward | Caster wheel rolls in |
|---|---|---|---|---|---|---|
| 0 (default) | 0° (tracks flat) | 0 / 0 | 0 | 0 | 0 | 0 |
| 50 | 30.6° | 20.9° / about 19° | 4.5° | between | between | between |
| 100 | 61.27° (maximum) | 41.7° / 36.9° | 5.7° (front tread at 84.3° to the floor) | 19.8 cm | 28.8 cm | 34.2 cm |

Negative commands keep the tracks flat. The front rollers do not move over the floor. The
caster wheels roll without slipping. The forward shift is an offset on top of the robot's
placement, so Robot X/Y/heading commands keep their meaning.

## The caster four-bar (from the CAD)

Hinges, fitted to CAD view 1 (rest) and view 3 (tiptoe), 962 px/m with the CAD track hub on the
pelvis drum; both poses fit within 2.4 mm:

| Hinge | Where | Model (x, z) m |
|---|---|---|
| A, upper brace body hinge | grey circle on the hinge housing | (-0.3969, -0.7388) |
| B, upper brace head hinge | centre of the 45° chamfer, over the column axis | (-0.5985, -0.7296) |
| C, lower brace body hinge | lower circle on the hinge housing | (-0.3720, -0.8233) |
| D, lower brace head hinge | collar ring between the bellows | (-0.5945, -0.8101) |

- **Upper brace (A-B, 202 mm)** is one piece with the swivel top housing: `body_caster_head_link`
  now carries `Meshes/Body/caster_upper_brace.stl`, the CAD side profile (nose, deep 45° chamfer
  centred on B over the column, rounded end around A) extruded to the arm's width. The hinge's rear
  leaf moves with it. At full tiptoe the chamfer face lies almost flat on the bellows top, as in the CAD.
- **Lower brace (C-D, 223 mm)**: the collar braces, now children of the root link (they were
  children of the collar), running from the collar pins to C.
- **Column (B-D, 81 mm)** is the coupler: collar, bellows rings, top plate, yoke, fork, axle,
  wheels and hubs. The braces have different lengths, so the column leans forward 5.8° at full
  tiptoe (upright at rest). The caster swivel and wheel roll still turn about the yoke as before.
- **Body section**: the caster arm stays; the hinge housing (lower arm body/edge, hinge, side
  cheeks/panels, hydraulic and cylinder parts, rear housing) moves back 7.2 cm so the hinge pin
  sits at A, and the hydraulic shaft lengthens to match.
- The column moves back 14.4 cm onto the CAD axis; the bellows are lengthened (upper rings x1.98,
  lower rings x3.19) so the collar and top plate sit at the CAD heights with the model's wheel kept.
- Marker links `body_caster_linkage_{upper_body,upper_head,lower_body,lower_head}_link` mark A, B, C, D.

The URDF change is made by `apply_caster_fourbar.py` from the original
(`Archive/DesktopChanges/tiptoe-fourbar-20261113/johnny5_head.urdf`). Proposal pictures:
`caster-fourbar-proposal.png` (+ `-cad1` / `-cad3` overlays, drawn by `caster_proposal2.py`).
Earlier proposals: `caster-linkage-proposal*.png`, `tiptoe-proposal-render.png`.

## Where it appears

- **Timelines and control picklists** in both editors: `B_TipToe` is a body control, so it
  is in every list built from the control enum or the body catalog, uses the body lane, and
  has its own icon (`Assets/ControlIcons/B_TipToe.png`, source
  `BodyUrdfSource/B_TipToe.svg`, drawn by `render-tiptoe-icon.py`).
- **URDF Configuration** (body section): its own card with Min/Max degrees and Reverse.
  Defaults are 0 to 61.27°. A saved stock 45° maximum (from the first version) is raised to
  61.27° when loaded; a custom maximum is kept. Angles beyond 0..61.27° are clamped by the scene.
- **VR Editor pose handles**: a handle on the left track's front hub, in the left column
  under Waist Lean.
- **VR Player** standalone playback of saved scenes.
- **Editor API** (`get_state` / `insert_commands` / MCP `list_controls`): `B_TipToe`.
- Not sent to physical hardware (URDF only, like all body controls). Not driven by Follow.

## How it is built

- `Shared/Controls/ControlNames.cs`: `B_TipToe` appended at the end of `ServoNames`.
- `Shared/Controls/BodyControlCatalog.cs`: entry after WaistSwivel, joint name `TipToe`,
  0 to `TipToeMaximum` (61.27°); `TipToeFormerMaximum` (45°) for the settings migration in
  `Shared/Configuration/UrdfConfiguration.BodySetting`.
- `Shared/Pose/TipToeGeometry.cs`: reads the hinge markers and the track/tread/shroud and caster
  column visuals (STL meshes and primitives, as (x, z) outlines) in the `body_root_link` frame.
  `PartOf(link)` puts each root child in a group: Track, UpperBrace, LowerBrace, Column or Body.
  `Linkage(swing)` solves the four-bar exactly; the upper brace swing is linear in the track angle.
  Per 1/128 of the range it solves the tip-back (bisection until the lowest column point is back on
  the floor), the lift (treads on the floor), the forward shift (front roller centre planted) and the
  caster roll (axle floor travel minus the column's and robot's own rotation of the wheel). `At(angle)`
  returns a `Pose` (Lift, Forward, Pitch, UpperBrace, LowerBrace, ColumnLean, ColumnShift,
  CasterRoll). `RootChildMotion(link, angle)` = the group's body-frame motion, then the tip-back about
  the drum, then the lift and forward shift. Cached per URDF path and timestamp. A model without the
  markers has no TipToe.
- Editors (`ServoAnimator/ServoAnimator` and `Johnny5VREditor`, identical
  `UrdfScene.TipToe.cs`): "TipToe" is a virtual joint. `InitializeTipToe` runs last in
  `UrdfScene.Load` and wraps every root child joint's node transform with one extra
  `MatrixTransform3D`. Tread frames, roller rotation and caster swivel/wheel roll are part-level
  transforms below it and keep working. `SetTipToe` sets the wheel roll, which
  `UrdfScene.Caster.SetCasterSwivel` adds to the wheels' own travel. The VR Editor's
  `TryVrJointPoint` anchors the handle on `body_track_front_hub_left_link`.
- VR Player (`Johnny5VR/AnimationRig.cs`): root children get `RootChildMotion` in the per-link
  `custom` matrix; the caster roll is added to the `TravelState` caster wheel distances.

## Kinematics in detail (for anyone changing it)

Frames and conventions:

- Everything is in the `body_root_link` frame: metres, +X forward, +Y to the robot's left, +Z up.
  TipToe only ever rotates about axes parallel to +Y, so the solve works on (x, z) points; the
  `Vector2` values in `TipToeGeometry` are (x, z).
- `RotationAbout(pivot, a)` is a row-vector matrix (`v * M`, System.Numerics). **Positive `a`
  moves +X (the front) down.** The private `Rotate(p, centre, a)` is the same rotation on (x, z)
  points: an (x, z) direction's `atan2(z, x)` angle *decreases* by `a`.
- Matrices compose left to right: `PartMotion * Placement`, i.e. the part moves in the body
  frame first, then the whole robot is tipped back and lifted.

Per TipToe angle θ (clamped 0..61.27°), with t = θ / 61.27°:

1. **Tracks**: `RotationAbout(drum, θ)` (front down).
2. **Upper brace**: swing ψ = t · 41.7°; matrix `RotationAbout(A, -ψ)`. The head end lies behind A,
   so a negative angle moves it down. B moves to `Rotate(B0, A, -ψ)`.
3. **Four-bar** (`Linkage`): D is where the circle about C (radius |CD|) meets the circle about the
   new B (radius |BD|), choosing the intersection nearer `D0 + (B - B0)`.
   - Lower brace swing φ = wrap(angle(D - C) - angle(D0 - C)), positive with the head end down.
     The brace points backward, so its angle *grows* as it drops. Matrix `RotationAbout(C, -φ)`.
   - Column lean λ = wrap(angle(B0 - D0) - angle(B - D)), positive with the top forward. Matrix
     `RotationAbout(D0, +λ)`, then a translation by (D - D0).
   - Wrap every angle difference with IEEERemainder. The lower brace points near ±180°, so a raw
     difference comes out as 323° instead of -37°. That bug, and the sign of φ and λ, were the
     first failures while building this.
4. **Tip-back** β (positive = front up), found by bisection over -30..30°:
   - Rotate everything by `-β` about the drum.
   - lift = floor - lowest track point.
   - forward = front-roller x at rest - its rotated x.
   - gap = lowest column point + lift - floor - the column's rest gap.
   - Gap > 0 means the wheel is still in the air: tip back more.
   - Placement matrix: `RotationAbout(drum, -β) * Translation(forward, 0, lift)`.
5. **Caster roll** (extra wheel travel, metres): roll = axle floor travel - (λ - β) · r. The wheel
   already turns by (λ - β) through its parent transforms (column lean, then tip-back), and rolling
   needs travel / r of turn in total. Both editors and the player add it to the caster wheel
   travel; the editors scale it to `CasterWheelVisualRadius`.
6. Lift, forward, β and roll are tabled at 128 steps and interpolated linearly. The brace angles
   and the four-bar are solved exactly on every call. Floor contact uses only the convex hulls of
   the (x, z) outlines of the track and column visuals, so building the table is fast.

What moves with what (`TipToeGeometry.PartOf`, applied to each direct child of the root link;
links below a root child, such as the collar pins under the collar, inherit its motion):

- Track: `body_track_*`, `body_tread_*`, `body_shroud_*`.
- UpperBrace: `body_caster_head_link`, `body_caster_detail_hinge_rear_leaf_link`, the B marker.
- LowerBrace: `body_caster_detail_collar_brace_*`.
- Column: collar, springs (the bellows), yoke, fork, axle, wheels, hubs, top plate, collar pins and
  the D marker.
- Body: everything else, including the caster arm, the hinge housing and the A and C markers.

To retune: the CAD hinge pixels and the profile live in `apply_caster_fourbar.py`. Re-run it on the
backup URDF. `TipToeGeometry.MaximumRadians` (61.27°) must match
`BodyControlCatalog.TipToeMaximum`; `UpperBraceMaximumRadians` is 41.7°. The Python twin
`caster_proposal2.py` uses the same maths and draws pictures to compare against.

## Files

| Area | Files |
|---|---|
| Control identity, catalog, icon | `Shared/Controls/ControlNames.cs`, `BodyControlCatalog.cs`, `ControlIconFiles.cs`; `Assets/ControlIcons/B_TipToe.png` in both editors; `BodyUrdfSource/B_TipToe.svg`, `render-tiptoe-icon.py` |
| Settings migration (45° to 61.27°) | `Shared/Configuration/UrdfConfiguration.cs` (`BodySetting`) |
| Geometry and kinematics | `Shared/Pose/TipToeGeometry.cs` |
| Model | `Config/Models/Robots/Johnny5FullBody/johnny5_head.urdf`, `Meshes/Body/caster_upper_brace.stl` (also `Johnny5VR/packaging/editor-check/Config/...`); generator `Documentation/TipToe/apply_caster_fourbar.py` |
| Editors (identical pairs) | `UrdfScene.TipToe.cs`, `UrdfScene.Caster.cs` (wheel roll), `RobotHeadView.cs` (`InitializeTipToe`, the "TipToe" joint in `HasJoint`, `SetConfiguredBodyJoint` and `SetJoint`) in `ServoAnimator/ServoAnimator` and `Johnny5VREditor`; `Help/AnimationEditor_API_For_AI.md` |
| VR Editor pose handle | `Johnny5VREditor/MainWindow.VrPose.cs` (`TryVrJointPoint`) |
| VR Player | `Johnny5VR/AnimationRig.cs` (root-child `custom` matrices, caster roll), `RobotPoseInteraction.cs` (handle order) |
| Checks | `Johnny5VR/TipToeChecks.cs` (from `Diagnostics.cs`), `Johnny5VR/RobotPoseChecks.cs` (left column order), `Johnny5VR/Diagnostics/pose-control-catalog.bin` (fixture), `Johnny5VREditor/VrTipToeChecks.cs` (from `VrPoseChecks`), `Shared/Controls.Checks/Program.cs` (B_TipToe is additive) |
| Pictures and scripts | `Documentation/TipToe/*.png`, `caster_proposal*.py`, `tiptoe_render*.py`, `render_dump.py` |

Release: `Releases/VR/0.9.0/J5Studio-VR-TipToe-Caster-FourBar-update.zip` (cumulative; replaces the
full-body URDF).

## Known limits

- The collision model (head/flap safeguards) does not include TipToe. Arm-to-track
  collisions are not checked, as before.
- The robot is not re-grounded by other code: the rise is relative to the floor found when
  the model loaded.
- The model's caster wheel stays its own size (radius 75 mm; the CAD's is about 96 mm), so the
  bellows are stretched to reach the CAD hinge heights.
- At rest the 45° chamfer meets the bellows top at an angle (as in the CAD); the part of the
  bellows top under the forward half of the chamfer sits inside the brace.

## Checks

- Player `--check`: 4,208 pass. TipToe: catalog (61.27°), mapping, the 45° settings migration,
  four-bar lengths from the markers (202 / 223 mm), swings (41.7° / 36.9°), column lean, tip-back
  (5.7°) and rise (19.8 cm), forward shift, clamping, link grouping, and standalone playback (body
  rises and moves forward, front rollers planted within 2 mm, caster rolls in and stays on the floor,
  all four hinges stay joined, caster wheel turns exactly as far as it rolls, exact return to rest).
  The left centre pose column check now expects TipToe under Waist Lean (the stored catalog fixture
  `Johnny5VR/Diagnostics/pose-control-catalog.bin` predated TipToe and was refreshed).
- VR Editor `--vr-pose-check`: 197 pass through the real pose handle: 61.3° at 100, 19.8 cm rise,
  braces 41.7° / 36.9°, 5.7° tip-back, front rollers planted, caster wheel rolls in 34.2 cm and stays
  on the floor (0 mm), upper brace and column swing down, shroud strip lines rotate with the tracks
  and the tip-back, 30.6° at 50, flat for negatives, exact return to rest. The check writes
  `tiptoe-scene-rest.bin` / `tiptoe-scene-full.bin`, rendered by `render_dump.py` into
  `tiptoe-editor-scene.png` (`tiptoe-editor-scene-caster.png`: the lower half).
- Desktop `ServoAnimator.Checks --caster`: 35 pass on the new URDF.
- Controls compatibility check: both editors pass.

## Backups

`Archive/DesktopChanges/tiptoe-20261112` (first version), `tiptoe-planted-20261112` (front
planted), `tiptoe-fourbar-20261113` (this change: original URDFs, every edited source, the
previous dev-editor and Desktop DLLs, the old catalog fixture and pictures).
