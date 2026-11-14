# Webcam Servo Speed enforcement — 2.1.0

Webcam Face Tracking now offers Enforce Servo Speed limits in preview and
recording. Save/apply persists EnforceServoSpeeds in FaceTracking.json; absent
fields default to false, preserving existing mappings.

The option explicitly enables the existing calibrated motion model for
webcam-owned physical channels, even when the general UseInUrdf preference is
off. Hardware continues receiving the requested targets and active speed profile.
The URDF simulates each channel's configured Speed/Acceleration and calibration
timing once; there is no extra target filter. Neck mixing happens before the two
piston channels advance. Eye Pop uses independent extend/retract timings.

Recording samples actual modeled channel positions rather than webcam targets.
All recording sample paths use SampleControllerRecording so controller polling
cannot accidentally sample stale raw webcam values. Neck commands are normalized
to NeckNodUp child units, avoiding the recorder's alternate-parent replacement.
Take processing preserves these physical-channel samples without smoothing,
reduction or gang merging. Other tracks retain their existing processing.
Controller takeover stops sampling the webcam-owned estimate until ownership
returns; resumption starts from the controller's current preview pose.

Enforced webcam capture runs at 1x so timeline seconds equal physical time. The
existing recording-window close handler restores the previous playback rate.
The model estimates movement; it is not physical position feedback. Accurate
speed calibration improves agreement with the actual mechanism.

Validation: Release build 2.1.0 succeeded. Passing checks: new webcam-speed suite
27; existing face tracking 17; controller recording 49; speed calibration 289;
general editor 6697; Help 109. The configuration UI was rendered and inspected.
Tests do not start a camera or initialize physical hardware. No live mappings,
libraries, deployment ZIP or repository release was changed by this feature task.
