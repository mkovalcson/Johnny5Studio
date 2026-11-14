# Record from Controller / Webcam

Click Record in the Sequence controls. This window configures a take at the current cursor; it does not begin recording immediately.

## Input and control selection

Choose Xbox Controller, Steam Controller, Webcam, Webcam + Xbox or Webcam + Steam. Connect and enable the chosen controller. For webcam sources, first start the camera and apply enabled mappings in Webcam Face Tracking. Load the URDF, enable URDF Drive and leave Pose editing.

Overdub selected controls limits recording to the selected parents or children. Select all and Clear selection affect the control selection. Select child controls toggles child visibility; only multi-child parents expand. Checking a child unchecks its parent, and checking a parent clears its children. Turning overdub off records all offered controls.

In mixed modes, the text above the labels explains webcam precedence. Gold labels with a webcam icon identify any control sharing a physical actuator with an enabled webcam mapping, whether through a parent or child. The selection chooses what to record, not which source wins. Change enabled webcam mappings to change ownership.

Maximum take length is 1–300 seconds. Group Recorded Commands defaults checked and creates a new numbered group when the take is kept. Smoothing is 0–300 ms. Keyframe reduction tolerance is a percentage of each control’s full range; larger values generally retain fewer points. These processing settings can be adjusted while reviewing. Existing spline settings are preserved; individual child commands use held values.

## Ready, record and review

Ready to Record saves configuration and hides the window. Use a controller button mapped to the red REC action to start/stop. Webcam sources also support F9 while the editor has focus. Escape while armed returns to configuration. The Sequence Record button is dark red normally and bright red during a take.

Stopping reopens the window. Replay Recording plays only this take; Stop Replay ends it. Existing Drive HW and focus permissions apply to recording and movement replay. Keep Take merges the processed commands into the selected controls and time range as one undoable edit. Unselected controls remain intact. Discard / close discards the draft after confirmation when needed.

Recording settings save when Ready to Record or Discard / close is pressed (also when closing the window): source, overdub, parent/child selections, child visibility, duration, grouping, smoothing, reduction, Record Audio, and microphone preference. They restore from ControllerRecordingSettings.json in Config. A missing remembered microphone must be selected again.

See Recording Audio for microphone capture and Meeting Mode for live URDF puppeteering without recording.

## Recording at servo speed

For webcam motion that follows the hardware's configured pace, enable **Enforce Servo Speed limits in preview and recording** in **Config > Webcam Face Tracking** and Save / apply. Such takes use 1× timing and retain modeled actuator samples rather than raw targets. Smoothing and reduction remain available for other controller tracks. See [Webcam Face Tracking](WebcamFaceTracking.md).

## Face Tracking Override

For Webcam + Xbox and Webcam + Steam, each parent and child row has a left checkbox for controller takeover and a right checkbox for recording. Select the left boxes for the actuators the controller should own when its Face Tracking Override action is on. Unchecked actuators retain webcam priority. These choices are independent of the recording selection and are saved with Ready to Record or Discard / close. Parent selections apply to all their children.

Map Face Tracking Override to a controller button under Control / Target action. Hold the button for takeover; release it to return control to the webcam. A red label on white appears above the controller mapping title while enabled. Takeover ends on release or when controller input is stopped or reset; the selected recording takeover controls persist. Meeting and webcam preview allow takeover of all face-mapped controls while enabled. Disconnected or disabled controllers do not suppress webcam output.
