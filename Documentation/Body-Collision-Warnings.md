# Body collision warnings

November 13, 2026. J5 Studio Desktop and VR Editor.

## What changed

Collision warnings used to check only the eye flaps against the eye tubes, lenses and gimbals.
With the full-body model they now also check:

- each arm, from the upper arm through every finger and the thumb, against the head (everything
  under `base_link`: neck, head shell, eyes, eyebrow flaps, nose, microphone and antenna);
- the left arm against the right arm;
- each arm against the torso: the chest and the neck lift mechanism, including the shoulder laser
  (added later the same day, see "Torso, waist and tracks" below);
- each arm against the waist: the waist column, bellows, swivel plate, panels and pelvis;
- each arm against the tracks: the track frames, treads, shrouds and the caster.

No collision meshes were added to the URDF. The check samples the surface of the links' own
visual geometry (STL meshes and the cylinder, sphere and box primitives, such as the fingertip
caps) every 5 mm. It places the samples with the same numeric link transforms the head collision
check already uses. Two surfaces closer than 5 mm count as touching.

When the Collision Warning toggle is on:

- the touching parts turn red in the URDF view;
- the status text says `COLLISION` and names the regions, for example `left arm ↔ head` or
  `right arm ↔ tracks`;
- playback marks the command times red on the timeline, as for flap collisions;
- the Editor API's `get_collisions` (and the MCP tool) lists the pairs as `body:left arm|head`,
  `body:right arm|head`, `body:left arm|right arm`, `body:left arm|torso`, `body:right arm|waist`,
  `body:left arm|tracks` and so on.

The head-only model has no arm joints, so it shows only the original flap warnings.

## Torso, waist and tracks

How the links are grouped (`GetBodyClearanceModel` in `UrdfScene.BodyClearance.cs`):

| Group | Links |
|---|---|
| head | everything under `base_link` |
| left arm, right arm | everything under the `ArmTwistLeft` / `ArmTwistRight` child link |
| torso | everything under the `ChestTilt` child link, minus the head and shoulders |
| waist | the rest of the body from the root down, except track links |
| tracks | root links whose names contain `_track_`, `_tread_`, `_shroud_` or `_caster_` |
| not checked | the shoulders (everything under `ShoulderHugLeft` / `ShoulderHugRight` above the arm twist), which always touch both the arm and the chest |

The six groups hold about 570,000 samples. They are built once, the first time warnings are on
(about 0.4 s). Each check first places only the links' bounding boxes. A link's samples are moved
only when its box comes within 5 mm of a box in the other group. One check takes about 16 ms in a
heavy arm-over-head contact pose and about 3 ms in a waist contact pose.

What the checks found on the current model:

- The shoulder laser sits 6 mm from the left upper arm at neutral. Swinging the left arm back
  about 50%, hugging it in about 60%, or raising it fully overhead (forearm through the laser
  head) touches it. The right arm has no laser near it.
- The arms reach the waist when they are swung back and down, especially with the waist turned.
- The arms reach the tracks and caster only with the waist turned and leaned back. Bent forward to
  the floor, as in the Sugar Plum Fairy finale, the fingers stay 5 cm or more above the tracks.

## Files

| File | Change |
|---|---|
| `Shared/EditorUI/UrdfScene.BodyClearance.cs` | New, compiled into both editors. Builds the surface samples on first use, finds touching link pairs, and highlights them. Second pass: torso, waist and tracks groups, transforms only the samples a pair needs, `BodyClearanceGroups()` and `BodyGroupClearance()` for checks. |
| `ServoAnimator/ServoAnimator/UrdfScene.CollisionModel.cs` | `CollisionSession.TryGetWorld(link)` exposes a link's world transform for the current pose. |
| `ServoAnimator/ServoAnimator/RobotHeadView.cs` | `UpdateCollisionState` adds body contacts to the active pairs and highlights; the collision baseline rebuild resets the body samples. |
| `Johnny5VREditor/UrdfScene.CollisionModel.cs`, `Johnny5VREditor/RobotHeadView.cs` | Same two changes. |
| `ServoAnimator/ServoAnimator.Checks/BodyCollisionChecks.cs`, `Program.cs` | New `--body-collisions` check group. |

The VR Player's dev editor copy (`Johnny5VR/packaging/editor-0.9.0`) was refreshed with the new
`AnimationEditorPlayer.dll` and `J5Studio.*.dll`.

## Checks

`ServoAnimator.Checks.exe --body-collisions` runs headless (the URDF view is created in memory and
never shown). Run it from the workspace root, because it loads
`Config/Models/Robots/Johnny5FullBody/johnny5_head.urdf`. It drives the view through the
same `SetServo` path as the timeline, then checks:

- the neutral pose has no body contacts;
- the Sugar Plum Fairy dance's first overhead pose is caught as both hands touching and hands
  inside the head;
- fingertip caps meeting over the head are caught;
- the corrected overhead and pirouette poses are clear, the pirouette pose with the neck turned
  -36, 0 and +36;
- raised arms with a 60% neck turn are caught (eyebrow flap against forearm);
- all six groups are present;
- the left upper arm swung back into the laser is caught, and a small backward swing (15 mm away)
  is clear;
- fingertips against the waist column and against the caster are caught;
- the Sugar Plum Fairy finale pose (bent to the floor, arms down) is clear;
- warnings clear when the arms return to neutral;
- one check takes under 25 ms in a heavy contact pose (about 16 ms) and in a waist contact pose
  (about 3 ms).

Results on November 13: `--body-collisions` passed 13 assertions. `--collision-performance`
(the head collision model) still passed 606 assertions. Both editors build. After the editor was
closed, the Desktop Release build was rebuilt in place (previous files in
`desktop-release-before/`), and `--body-collisions` passed again against it.

After the torso, waist and tracks pass: `--body-collisions` passed 21 assertions and
`--collision-performance` passed 606, against the rebuilt Desktop Release build. The VR Editor
also builds, and its DLLs were copied to `Johnny5VR/packaging/editor-0.9.0`.

## Notes

- A deep, enclosed overlap with no surface crossing would not be seen, because only surfaces are
  sampled. Arms reaching into the head always cross its surface, so in practice this is not an
  issue.
- The shoulders, and anything other than an arm (head against chest, for example), are not checked.
- The offline checker used for the Sugar Plum Fairy dance missed the primitive fingertip caps;
  the editor check found them. That checker now samples primitives too, and the dance's intro
  pose was widened (hug 80, twist -90) to leave about 3 cm between the fingertips.

## Backups

`Archive/DesktopChanges/body-collision-warnings-20261113/` holds the previous copies of both
editors' `RobotHeadView.cs` and `UrdfScene.CollisionModel.cs`, the checks `Program.cs`,
`Documentation/README.md`, and the replaced editor-0.9.0 DLLs (`editor-0.9.0-before/`).
`Archive/DesktopChanges/arm-body-collisions-20261113/` holds the first-pass
`UrdfScene.BodyClearance.cs`, `BodyCollisionChecks.cs` and this document, plus the Desktop Release,
VR Editor Release and editor-0.9.0 files that the second pass replaced.
