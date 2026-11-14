# Step 4: shared pose evaluation

Completed 2026-11-07.

## Implemented

- Added Shared/Pose/J5Studio.Pose.csproj, referenced by Desktop, VR Editor and VR Player.
- Added the library to the root unified solution and both existing desktop solution files.
- Added Shared/Pose.Checks to the root solution and the outer desktop solution.
- Extracted neck mixing/state/export, physical targets and speed-profile change detection,
  gang/child precedence and movie carry helpers from both editors.
- Connected VR neck mixing and prepared physical channel selection to those shared helpers.
- Connected both editor timeline adapters and VR prepared-track playback to GroundTravelPath.
- Unified ground direction, caster steering/rolling, twin-wheel travel and tread differential.
- Kept rendering, hardware I/O, effects and application transport in their applications.
- Preserved same-time override policies and per-host boundary initialization explicitly.
- Added frozen pre-extraction editor/VR neck baselines and focused shared behavior checks.
- Updated the old NeckChecks full-body model path to Config/Models/Robots/Johnny5FullBody.
  No model geometry or user Config documents were edited.

## Validation

Passed:

- Full Johnny5Studio.slnx Release build, all three applications and all check projects.
- Shared pose checks: 5,619 assertions against frozen prior-build values and focused behavior.
- Desktop hardware timeline: 32 assertions.
- Desktop complementary neck: 70 assertions.
- Desktop rear caster: 35 assertions.
- Desktop piston signals: 118 assertions.
- Desktop audio/movie: 13 assertions.
- VR runtime/importer/playback checks: 405 assertions.
- Shared animation: 329 assertions and established editor document/spline baseline.
- Established Player keypoint, endpoint/stamp, duplicate-time and spline-break baseline.
- Real Player/Editor smoke: toolbar Stop, standalone switch/resume, 30 scene updates,
  27 WPF-rendered editor frames, live pose preview and discard through the named pipe.
  Used Johnny5VR/packaging/editor-check/Config; viewer.json restored byte-for-byte afterward.

Pre-existing unused vent-center field warnings and the checks' unused Green helper remain.
The development computer has no SteamVR runtime registry; physical headset/controller testing
is still needed on the gaming PC. This did not prevent the desktop D3D/bridge smoke test.

## Builds refreshed

- Desktop: ServoAnimator/ServoAnimator/bin/Release/net10.0-windows/AnimationEditorPlayer.exe
  (version preserved at 2.12.2).
- VR Player: Johnny5VR/bin/Release/net10.0-windows/win-x64/Johnny5VR.exe.
- VR Editor source build: Johnny5VREditor/bin/Release/net10.0-windows/win-x64/AnimationEditorPlayer.exe.
- Bundled VR Editor used by normal development launch:
  Johnny5VR/packaging/editor-0.9.0/AnimationEditorPlayer.exe.
- Retained self-contained Player publish:
  Johnny5VR/packaging/publish-0.9.0/Johnny5VR.exe.

No release ZIP or archive was created or verified. Existing shortcuts remain valid.
No remaining implementation work is required for this extraction. Next practical check:
play an existing movie in the headset, seek forward/backward, move/spin Johnny, and verify
neck motion, tread movement and rear caster rolling while switching editor/player modes.
