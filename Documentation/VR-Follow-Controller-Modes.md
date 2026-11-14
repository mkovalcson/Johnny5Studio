# Follow me controller modes — 2026-11-08

Implemented on top of the Point Gesture update, preserving its timing diagnostics, hidden-panel capture optimization, elbow estimate fix, finger mapping and crouch neck lift.

## Behavior
- While following (including recording/overdub), A/B are separated per hand. Bound per-hand released states are authoritative over stale legacy presses.
- Left A toggles Mirrored / Unmirrored; defaults Mirrored. No automatic viewpoint/orientation switching.
- Left B toggles Move Me / Move Robot; defaults Move Robot. Choices persist in this Player session.
- Move Me: left XY walk/strafe, right X smooth viewer yaw (90 degrees/s maximum).
- Move Robot: left Y forward/reverse (80 cm/s maximum), right X smooth robot yaw (90 degrees/s maximum); dead zones prevent drift. Robot drive ignores mirror selection.
- Physical walking and body turning never change robot base position/heading. Coordinated head/hand turning drives constrained Waist Swivel, reversing in mirror mode. Head-only turn still drives neck.
- Torso recording owns Waist Swivel; travel recording owns joystick base movement. Neck capture remains separate and OFF by default.
- Calibration now has 21 steps, including separate left/right pitch, roll and arm twist, ending with forward bend. No walking/turning calibration. Existing profiles remain usable.
- Controller rays hide while following. Four small cached brass instrument plates follow tracked controllers, with A plates near the grip and B plates farther forward. Labels face the headset. Tracking loss/focus loss or stopping hides plates.
- Right A menu and right B stop/back behavior retained. Opening a menu still stops following; resume deliberately via Follow / re-center. Outside following, existing controls unchanged.

## Main source
Johnny5VR/ControllerInput.cs: per-hand restricted SteamVR action reads and legacy fallback.
Johnny5VR/VrSession.cs: mode-specific button routing, locomotion, pointer visibility and labels.
Johnny5VR/VrBodyMotion.cs: manual toggles and joystick drive before batched motion send.
Johnny5VR/FollowControllerLabels.cs: runtime brass label plates and tracked placements.
Johnny5VR/Locomotion.cs: optional smooth yaw; normal snap mode preserved.
Shared/Pose/BodyTrackingCalibration.cs: removed three travel/turn steps and obsolete validity requirements.
Shared/Pose/BodyTrackingSolver.cs: joystick-only root drive, coordinated constrained waist swivel.
Shared/VR/PoseWire.cs: waist swivel belongs to torso recording.

## Validation
Published Player and matching VR Editor. Player checks: 3914 PASS. Editor pose/recording checks: 138 PASS. Actual Player/Editor smoke: 30 scene frames, 31 WPF frames, pose batch + discard, recording start/sample/stop/discard PASS using isolated Config. Viewer settings restored after smoke. Label bitmap inspected for readable, unclipped text.
Headset/Index usability and physical button routing still require gaming-PC testing; this workstation has no SteamVR hardware.

## Package
Releases/VR/0.9.0/J5Studio-VR-Follow-Controller-Modes-update.zip
Install over the November 8 cumulative 0.9.0 installation. Includes all intervening Point Gesture update changes. Close Player and VR Editor, extract over the folder containing Johnny5VR.exe and keep the Editor subfolder. No Config, Settings, models or desktop editor binaries are replaced.
