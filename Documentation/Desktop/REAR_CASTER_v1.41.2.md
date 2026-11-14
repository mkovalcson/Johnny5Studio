# Rear caster detail (body v35) — 1.41.2

The full-body model's rear caster was rebuilt from the user's side and rear photographs, in `FullBodyModel/Body/build_robot.py`. The change was regenerated and spliced into `FullBodyModel/johnny5_head.urdf`; only `body_caster_*` links changed.

- **Offset swivel.** The axle trails 95 mm (`CASTER_TRAIL`) behind the swivel axis. The axis has not moved: it is still the origin of `body_caster_yoke_link`, which the editor uses as the swivel pivot.
- **Swivel stack.** Two bellows sections split by a grey collar, sitting on a dark swivel block.
- **Trailing plate.** `caster_fork_arm`, a generated STL in `Meshes/Body`, is a single plate in the wheel gap. It is shaped as an arm running into a band around the tyre, with a web and a boss carrying the axle. It replaces the three straight forks.
- **Wheels.** The tyres have 2 × 16 staggered tread lugs, and their outer edge is still on the floor.
- **Hub covers.** Each has a grey outer band, a lighter face, a black boss, a black accent bar from the centre to the band, and a small dark plug.
- **Swivel animation.** The new parts keep the name prefixes that `UrdfScene.InitializeCaster` turns (`yoke`, `fork`, `axle`, `wheel`, `hub`), so the whole trailing assembly swivels with no code change. The bellows and collar are symmetric and stay fixed.
- **Fall-apart clip.** Re-baked: 773 links, 105 frames.
- **Generator.** The v34 generator was archived as `legacy/build_robot_v34.py`. The generator also rewrote the yoke hood STLs and the tread manifest with different bytes; those files were left untouched.

Follow-up: the caster arm and head are now one triangle in plan view (`plan_solid` meshes). The arm is full width back to the rear housing tail's axis, then tapers to a 75 mm rounded nose over the swivel. Its underside angles from the head's front edge down to the bottom of the pelvis drum. The arm, rib, head and top plate keep their v35 link frames (checked against the URDF joint origins), so the fall-apart clip was deliberately not re-baked.

Body v36: the rear housing body, its slope, tail, handle and handle posts were removed. The caster arm's flat top was raised to the point where the waist column's rear face meets the pelvis drum (computed from the column pose). The housing's top panel, side panels and panel lines were moved onto the arm, keeping their link frames. The six removed links were pruned from `fall_apart.json`, renumbering piece indices, with no re-bake. `--fall-apart` (82) and `--caster` (8) pass.

Validation:
- The new `--caster` suite passed 8 assertions:
  - 95 mm trail;
  - lugs on the floor;
  - plate in the wheel gap;
  - at 90° the axle is beside the axis, still 95 mm from it;
  - all trailing parts swivel, while the bellows, collar and arm head stay fixed;
  - returning to 0° restores exactly.
- `--fall-apart` (82), `--beam` (37), `--tv` (43), `--neck` (70), `--new-features` (139) and `--recent-features` (466) passed.
- `--urdf-model` and `--urdf-presentation` stop at their pre-existing first failures.

Not changed: `CasterSwivel` still limits the fork to ±90° and does not flip it when reversing. With the wheel now trailing, a real castor would flip 180° in sustained reverse.

## Trailing caster motion

`CasterSwivel.Roll` replaces target-chasing for casters with a trail: `UrdfScene.CasterTrail` is measured from the swivel (`body_caster_yoke_link`) to `body_caster_axle_link`, and is 95 mm on body v36. The wheel can only roll along its plane, so in the room the fork turns at `|v|·sin(travel − angle)/trail`, where `v` is the swivel's velocity from forward travel plus its sideways motion from turning. Relative to the robot, the robot's own turn is subtracted.

The motion is integrated in steps of at most trail/10 of travel and 2° of turn, so it doesn't depend on frame rate. Both the live view and `TrackForwardPath` (timeline, seeking) use it.

Behaviour:
- Forward: the wheels trail.
- Reverse: the fork swings through 180° until the wheels sit under the arm (about 134° after 0.4 m; 179.4° after 0.8 m).
- Spin: the fork settles at −90° − asin(trail/radius).
- Stopped: the fork stays still.

A pushed wheel whose swing cancels out, such as straight reverse or a reversed spin, is an unstable equilibrium. It is given 20% of its full swing rate so it always swings round.

Casters without a trail keep `Target`/`Advance` (±90°).

`--caster` now has 22 assertions.
