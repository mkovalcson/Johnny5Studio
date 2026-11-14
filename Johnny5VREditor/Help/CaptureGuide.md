# Map inputs and record a performance

Stop timeline playback completely before opening Help.

## Map a controller and try it

1. Open the Xbox or Steam mapping window from Config. Select a MUX layer, then an input on the graphic.
2. Choose Control, Pose or Sequence. For a joystick, choose Relative for jogging or Absolute for position.
3. Set dead zone and joystick Expo. Higher Expo gives gentler movement near the center while retaining full-stick response.
4. For Steam gyro/accelerometer input, optionally require a held trigger button.
5. Save the mapping; it takes effect immediately. Save another mapping file to keep alternative layouts.
6. Enable the controller and use the configuration window's URDF test mode to observe motion and raw/mapped values.

Expected result: green feedback identifies active inputs. With MUX enabled shoulders select four independent layouts; Steam No MUX preserves those layouts and exposes separate shoulder mappings.

## Record a controller or webcam performance

1. Save a sequence and put the cursor where the take should begin.
2. Map a controller button to Start/Stop recording (REC). Webcam and mixed webcam/controller sources can also use F9 in the editor.
3. Open Record and choose the source. Select the controls to record; Select child controls toggles the child list.
4. Choose overdub if replacing only the selected controls. Leave Group Recorded Commands checked to keep the take together.
5. Press Ready to Record. The window closes. Press REC/F9 to start, perform, then press again to stop.
6. Review smoothing and reduction settings. Replay Recording auditions the take; Keep Take commits it. Discard / close removes the draft.
7. Save the sequence.

Expected result: editable commands, reduced to useful keyframes, without changing unselected controls.

## Combine webcam and controller input

1. Open Webcam Face Tracking, start the camera, Set neutral, and enable the intended signal mappings.
2. Save/apply mappings. For LPiston/RPiston, avoid conflicting parent neck mappings.
3. In Record choose Webcam + Xbox or Webcam + Steam.
4. Use the recording checkboxes to select what is captured. Webcam icons/highlighted labels identify actuator overlap.
5. In the additional left checkbox column, select controls that may be taken over by the controller.
6. Map Face Tracking Override to a controller button. Hold that button during the performance when the controller should own those selected controls; release it to return control to the webcam.

Expected result: webcam has priority on shared actuators unless the connected, enabled controller's override is on. Recording selection and takeover selection are independent.

## Record speech and convert its voice

1. In Record, check Record Audio and choose a connected microphone.
2. Speak and review the peak meter and clip indicator before arming.
3. Optionally check Automatically Convert Voice when Recording Finishes.
4. Ready the take, start/stop with REC/F9, then Keep Take.
5. Wait for the converted-file-ready status before playing the converted voice. Save the sequence.

Expected result: a numbered mono 44.1 kHz WAV and a Play command at the captured start cursor. Conversion runs after Keep Take; on success the Play reference changes to the _j5 file. Discard removes the draft recording.

For saving and export choices, see [Save, export and reuse your work](Saving.md).
