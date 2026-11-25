# Johnny 5 Studio documentation

Desktop/ contains development history, design notes, measurements, and workflow reference. User Help remains with each application so it can be deployed. Build version metadata is in ServoAnimator/Build/PROJECT_VERSION.json, separate from these documents.

Open **Help → VR Guides** in J5 Studio Desktop or VR Editor for the searchable user guides. The shared topics live in `Shared/UserHelp`. The notes below cover implementation history.

[Changelog](../CHANGELOG.md) tracks implemented changes for the next release.

[Prioritized usability review](Usability-Review-2026-11-18.md) records open behavior mismatches and follow-up suggestions.

## Start here: VR Player, VR Editor and body work (November 2026)

Each document says what changed and why, the files involved, the checks that cover it, and
where the backups are (`Archive/DesktopChanges/<topic>-<date>`). Update ZIPs are in
`Releases/VR/0.9.0`, each with an `update-manifest.json`; the newest is cumulative.

Follow mode (body tracking driving the robot):
- `VR-Body-Calibration-And-Following.md`: the overall design, including the waist swivel
  tracking switch (off by default).
- `VR-Follow-Smooth-Drive.md`: per-frame blending between editor scenes, one motion step per
  scene, the editor's catch-up headroom and the high-resolution timer.
- `VR-Follow-Scene-Rate-And-Treads.md`: adaptive scene rate (30 to 75 Hz), tread belts drawn by
  the player (protocol 11), Follow waiting for the editor, and the test fixtures and dev editor copy.
- `VR-Follow-Wrist-Pivot.md`: wrist pivot learned in calibration, the solver change, and the
  tracking recorder and replay.
- Earlier: `VR-Follow-*.md`, `VR-Faster-Hands-And-Timing.md`, `VR-Bridge-Pace-And-Memory.md`.

Calibration:
- `VR-Calibration-Wrist-Roll-Gauges.md`: relaxed wrist roll and arm-twist gates, the animated pose
  guide, live gauges, the attempt log, and the loosened tolerances (17 cm grip lever, 22 cm
  shoulder fit).
- `VR-Independent-Wrist-Arm-Calibration.md`, `VR-Calibration-Alignment-Help.md`.

Body controls and the model:
- `TipToe/README.md`: the B_TipToe control and the CAD caster four-bar. It covers the model
  changes, the kinematics and sign conventions, every file, the checks and the release.
- `Shared-Paired-Body-Commands.md`, `VR-Pose-Layout-And-Pairs.md`.

Sequence editing (Desktop and VR Editor):
- `Insert-Audio-Library-Copy.md`: Insert Audio copies a file from outside Config into
  `Config/Library/Audio` (never overwriting, unique names) and references the copy.
- `Body-Collision-Warnings.md`: collision warnings now cover the arms against the head, each
  other, the torso (including the shoulder laser), the waist and the tracks on the full-body
  model, sampled from the URDF's own meshes (no collision meshes needed).

Plans: `Future-Features-List.md`.

Conventions used across this work:
- Settings come from config files, not command-line options.
- Back up before overwriting.
- Nothing goes to physical hardware from URDF-only body controls.
- Player checks: `Johnny5VR.exe --check`.
- VR Editor checks: `AnimationEditorPlayer.exe --vr-pose-check <Config> <report>`, run with
  `Johnny5VR/packaging/editor-check/Config`.
- Desktop checks: `ServoAnimator.Checks.exe --caster` and the other switches, run from the
  workspace root.
- Controls compatibility: `J5Studio.Controls.Checks.exe <editor dll> Shared/Controls.Checks/baseline.json --require-shared`.
