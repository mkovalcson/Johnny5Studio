# Webcam Face Tracking

Open Config > Webcam Face Tracking. J5 Animation Editor & Player runs MediaPipe Face Landmarker in a local Python helper. The camera starts only when requested here or by a Meeting using a webcam source.

## Camera and neutral

The **Camera** group contains camera index, tracking rate, Mirror webcam, a single
Start/Stop camera button and Set neutral. Beside the picture, the live status shows
Camera stopped, Looking for face or Tracking face, with the processing FPS below it.
**Performance details** and **Advanced neck calibration** start collapsed.

**Apply** activates the edited configuration for this editor session without writing
`FaceTracking.json`. Applied settings remain available for recording and Meetings after
closing the window. **Save configuration** applies and writes the configuration for
future launches. The footer distinguishes pending edits, applied-but-unsaved settings
and saved settings. Closing does not save pending edits; the last applied configuration
remains active. Camera rate and mirror changes preview immediately on a running camera.

**Test capture only (7 seconds)**, inside Performance details, temporarily pauses
tracking, warms up capture for two seconds, then measures five seconds without
face analysis, preview generation or intentional rate throttling. The result shows
measured delivered/decoded FPS, average camera-read time, resolution, reported FPS,
pixel format and capture backend. It remains visible when normal tracking resumes.
The same selected camera, explicit MJPEG (MJPG) / 640 × 480 request and 30/60 fps
target are used in both normal tracking and the capture-only test. Restart the camera
after updating the tracker. Check the displayed pixel format: drivers can reject the
request, so a reported YUY2 mode means MJPG was not negotiated.
MJPG is applied after resolution and FPS because DirectShow can reset the pixel
format when changing FPS. If the reported format still differs, both readouts show
**MJPG request not accepted**. This verifies reported format selection, while the
capture-only FPS measurement remains the check for actual delivery rate.

A recent normal-tracking baseline is compared only when camera, target and available
capture-mode metadata match. Capture-only throughput below target points toward the
camera/capture path; capture near target with substantially slower normal tracking
points toward the combined tracking pipeline. These short samples do not verify
unique sensor exposures or prove a specific driver defect. **Cancel capture test**
stops it; a stalled test times out. Tracking resumes afterward if it was running
before the test and this window remains open. A stopped camera is released after
the test. Closing the window cancels an active test without restarting tracking.

- Camera index selects the camera (0 is normally the first). Start camera opens it; Stop camera releases it and turns off configuration preview.
- **Tracking rate** selects **30 fps** (the default) or **60 fps**. Changing it restarts a running camera; it does not start a stopped camera. **Apply** retains the choice for this session; **Save configuration** retains it for future launches. Camera capture, processing-loop pacing and the editor's result handling use the selected rate.
- **Mirror webcam** flips the incoming video horizontally before face analysis, so both tracking and the camera preview use the mirrored image. Toggle it beside Tracking rate for a quick comparison. A running camera restarts automatically; a stopped camera stays stopped. Apply or Save configuration as described above. It defaults to off. Mirroring in both the webcam software and editor cancels out; use one or the other. After switching, use **Set neutral** again if needed.
- **Processing: … fps average** reports completed face-tracking frames per elapsed second, averaged over about one second. It counts processing attempts even when no face is found and includes capture/processing overhead. The small camera picture still refreshes independently, about five times per second. The readout shows measuring during warm-up and clears when results stop arriving. A camera or processor that cannot sustain 60 fps will show its lower actual rate; selecting 60 fps does not guarantee that throughput.
- **Capture** shows the resolution of received frames and the camera driver's reported FPS. The driver rate is a reported setting, not independently measured camera throughput. Missing metadata is shown as unavailable. The tracker currently requests 640 × 480 independently of settings in the camera manufacturer's application.
- **Camera wait** averages the time acquiring/decoding each frame. **Face processing** averages color conversion, face analysis and derived-signal calculations. Both use the same approximately one-second window as the FPS average. Face processing excludes camera wait, preview preparation and intentional rate pacing. These timings help identify where time is spent but do not independently prove the camera's delivered FPS. They clear when results stop arriving.
- Set neutral captures your relaxed, forward-facing position. Apply activates it; Save configuration also persists it. Piston pitch and roll neutral offsets are stored separately; changing piston gains or ranges does not erase them.
- Apply validates and activates the mapping set without saving. The **+** button below the final mapping row creates a disabled mapping. The **X** at the far right of a row removes that mapping from the draft. Apply commits additions and removals for this session; Save configuration also persists them.
- Drive URDF preview tests applied mappings without physical output from this configuration window. Close the window to record; closing keeps the camera running.

## Mapping columns

The everyday table shows On, Face movement, Robot control, Speed, Reverse and remove.
**Advanced mapping settings** is checked by default and shows input/output ranges, dead zone,
smoothing and neutral. Uncheck it for the simpler table. The window initially opens at 1292 × 944.
On enables the row. Face movement selects an expression or derived measurement. Robot
control selects a logical servo or individual child. The dark dropdowns include all
supported signals and numeric controls. Use the table's horizontal scrollbar for advanced columns.

**Input minimum** and **Input maximum** describe the expected input after neutral subtraction. Head angles are degrees; expression scores generally use 0–1; LPiston/RPiston use −1–+1. **Output minimum** and **Output maximum** are native editor units, not degrees. Output is limited to that range. Reverse swaps the output direction. **Dead zone** is a fraction of normalized input span around neutral. **Smoothing (ms)** controls the live low-pass response (0 disables it). **Neutral** is the per-row input offset. Its display uses up to five decimal places; the saved value retains its full precision, which is also available while editing.

The readout inside Performance details shows raw signal → mapped value; subsequent smoothing and motion limits can make actual movement lag that value. Changing a target does not automatically set suitable input ranges: adjust them for the selected signal.

Default enabled signals are headYaw, headPitch, headRoll and squint. Other signals can be assigned to eyes, brows/flaps, irises or any available numeric target. Approximate gazeX/gazeY output holds during detected blinks.

**Speed** chooses Default, Slow, Fast or Crawl from Servo Configuration for that target, independently of controller speed actions. New rows use Default. **N/C · Keep current** retains the last profile on each actuator; the URDF uses Default when none is known. Existing files without per-row Speed keep N/C until you choose a profile. Speed selection applies whenever the webcam drives an output; the enforcement checkbox controls calibrated URDF movement and recording. Eye Pop uses its calibrated extension/retraction times rather than Maestro speed profiles.

## Servo speeds in preview and recording

Enable **Enforce Servo Speed limits in preview and recording**, then **Apply** to try it or **Save configuration** to retain it. It uses each physical actuator's configured Speed and Acceleration for the **Speed** profile selected on each mapping row, plus the timing estimates or measurements in **Config > Servo Speed Calibration**. The option also applies to webcam Meeting preview and is persisted by Save configuration. Existing mapping files keep this option off.

The URDF models the resulting actuator motion, including separately limited neck pistons after nod/tilt mixing and calibrated Eye Pop travel times. This works even when the general **Use calibrated motion in URDF** setting is off; other controls retain that setting. Hardware receives the requested targets and each mapping's selected speed profile through its existing output permissions.

Webcam takes record the modeled actuator positions rather than the requested face targets. Capture uses **1× playback** so recorded seconds match physical time; the previous rate is restored when the recording window closes. These samples are retained without take smoothing or reduction, so a speed-limited take may contain more commands. Other controller tracks still use the recording window's processing choices. Controller takeover retains its normal behavior.

The result is a timing estimate, not physical position feedback. Accurate calibration improves agreement with the real mechanism. Mapping smoothing still shapes the requested motion before these limits are applied, and later timeline edits can change its timing.

## Shared neck and input ownership

The head pitch → Neck Nod Up and head roll → Neck Tilt Right pair drive nod and tilt together. These rows must select the same Speed profile because they share the two physical actuators; validation explains a mismatch. Left piston / Right piston mappings allow separate profiles. Like the timeline, they resolve into the two neck pistons, with nod keeping priority at the travel limits (see Spline Editor › Neck nod and tilt). Alternatively use LPiston/RPiston (targets `Child:NeckNodUp:NeckTiltLeft` / `…:NeckTiltRight`) for explicit gain and clipping control. Do not enable both paths together. Enabled mappings generally must not share an actuator; validation identifies conflicts.

For Webcam + Xbox or Webcam + Steam recording, enabled webcam mappings reserve their physical actuators. Controller commands for overlapping children are ignored. A partially overlapping controller parent can still drive its unreserved children. The recording window highlights overlapping labels and shows a webcam icon, even when the overlap is through a different parent alias.

## Recording and tracking loss

Choose Record, select a Webcam source, arm controls, and press Ready to Record. F9 in the editor starts/stops an armed webcam take; a matching controller REC binding can also do so. Replay, Keep Take and Discard / close work as for controller recording. Old frames are ignored after about 500 ms; sustained loss stops a webcam take after about 1.5 seconds for review.

Ordinary webcam preview/recording requires an active editor, recording, tracking or robot window. Meeting mode deliberately continues while Teams has focus. Head angles are camera-relative, not torso-relative. Gaze is an expression-based estimate, not calibrated screen gaze. Video and raw landmarks are not saved as animation data.

The installed prebuilt MediaPipe runtime includes usage telemetry. Its maintainers state that camera input is not uploaded; local inference should nevertheless not be described as network-free. Setup instructions are in the face-tracking solution README.
