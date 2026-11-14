# First-class paired commands in both editors - 2026-11-07

Completed: the five VR virtual arm pairs have been promoted to shared ServoNames:
B_ShoulderRotate, B_ShoulderHug, B_Elbow, B_WristPitch, B_WristRoll.
Appended enum identities 96-100 preserve the existing 0-95 identities.

Shared body catalog definitions make the commands available to timeline commands,
controller mapping targets (Xbox and Steam), URDF body configuration, body pose
panels, spline/recording targets and the existing body-command views. Both editors
use the same definitions and paired motion implementation. URDF pair preview cards
explain that calibration comes from the two member-joint cards, including Reverse.
Icons are composed from the two existing arm-control icons, with a purple link.

Body pose evaluation expands each pair through shared calibrated symmetric-angle
mapping. Unauthored pairs do not reset individual poses. Authored pairs and individual
commands resolve chronologically, with individual commands winning an exact-time tie.
Captured/library poses and individual exports expand to the existing member controls.
Movie carry in the editors captures resolved individual body joints. VR movie carry
preserves relative body-command ages so older member commands cannot retake a newer
pair when advancing to an empty next sequence. VR Player supports paired commands
loaded directly from saved movies/sequences, with equivalent render transforms.

This retains the existing URDF body scope. Physical Maestro-channel setup and face
tracking intentionally exclude all body commands, including the existing independent
arm and hand commands. No new hardware channel definitions were invented.

Validation:
- All applications/check projects build successfully; existing unused-field warnings remain.
- Desktop integration: 57 assertions (picker, icons, all controller kinds, URDF cards,
  pose expansion/refinement, save/load and individual export).
- VR Editor integration: 89 assertions, including the same shared UI checks,
  paired previews, ordinary member insertion, Undo, and carry into an empty next sequence.
- Shared pose: 5,724 assertions (including reversed and unequal paired calibrations).
- VR runtime: 3,742 assertions, including direct paired playback transform equivalence,
  individual refinement and carried-command precedence.
- Controls compatibility: original 96 IDs/ranges, 192 serialized commands, defaults,
  aliases and file formats still match the frozen baseline. Five explicit new commands
  are checked separately; the original baseline was not regenerated.
- Animation: 329 shared assertions and existing document/spline baseline passed.
- Desktop body/audio/voice feature checks: 176 assertions passed.

Builds: Desktop normal development output refreshed; both VR source builds and retained
self-contained Player/Editor publishes refreshed. Portable update:
Releases/VR/0.9.0/J5Studio-VR-Shared-Paired-Commands-update.zip.
Updates root Player + Editor application files; preserves Config/Settings/data.
Physical headset testing remains for the gaming PC.
