# Meeting Mode

Meeting, immediately to the right of Record, drives the URDF without writing animation commands or recording audio files. It is intended for capturing the robot window through OBS for Teams or other meeting software.

## Meeting Configuration

The window groups Scene, Robot movement, and Voice and mouth lights on the left, with a live scene preview and readiness checks on the right. Cancel, Save settings and Start Meeting stay visible in the footer while you scroll.

Turn **Face tracking** on to use the saved Webcam Face Tracking mappings. Choose **None**, **Xbox**, or **Steam** under **Controller**. You can combine face tracking with either controller; at least one movement input is required. Existing combined input preferences reopen as these separate choices. The selected controller must be connected. Meeting enables it for the session and restores its previous enable preference on Stop.

A microphone is always required to animate the mouth lights. Select one and speak while watching the live meter and preview; **Refresh** beside the selector finds newly connected devices. If no preference has been saved and only one microphone is available, it is selected automatically. A missing saved microphone is reported so you can choose its replacement. The live readout shows peak dBFS and a clipping indication. Re-selecting the microphone resets its clip indication. Select your speech microphone separately in Teams; these settings do not change its meeting volume.

Expand **Adjust mouth response** for sliders and exact numeric fields. **Mouth sensitivity** scales the visual response. **Ignore quiet sounds** suppresses levels at or below its threshold. **Light fade time (ms)** determines how quickly the lights decay after a louder sound; attack follows incoming peaks. **Restore defaults** resets these values to 4, 0.02 and 180 ms. Save settings or Start Meeting keeps those changes. Existing URDF Audio LED Gain also affects the final mouth lights.

## Meeting scene

- **Use current editor setup** is the default. Inherit the active robot, background (including image motion or 3D scenery), room placement and camera. Use Apply in Scene Setup first to keep edits without saving a scene.
- **Use saved scene** loads a complete scene for this meeting. Select it from the list or use **Browse…** to choose a scene file. Missing or invalid scenes produce an error; they are never silently replaced.

The preview loads the chosen robot, background and camera before starting. Microphone mouth response and face motion appear in this independent view without replacing the editor scene. Robot/background names and the camera source appear below the picture; in a compact window, hover over the scene name for those details. Edit the appearance in URDF Scene Setup and Apply, or save a reusable scene.

**Save settings** stores the chosen mode, scene reference and input/audio preferences in Config/MeetingSettings.json and leaves the window open. **Start Meeting** also saves those preferences. **Cancel** closes without saving pending changes and releases preview devices started for this window.

The selected robot, background, placement and camera are temporary for the meeting. Saved scene files and editor defaults are unchanged. Stopping, a microphone failure or a failed scene load restores the editor's previous robot, background, placement and camera. Camera framing is preserved when entering the clean meeting view for every background type.

## Start and stop

Start Meeting undocks the robot if needed, enables URDF Drive, and hides pose/editor controls, status, FPS and resize overlays for a clean view. It uses the current setup or selected saved scene without replacing editor defaults. The Meeting button becomes bright red with white Stop meeting text.

Input continues while another application has focus. In combined webcam/controller modes, enabled face mappings take precedence for shared actuators. Meeting controller input handles numeric motion; library and transport actions are not used here. Mapped Stop/Disable actions stop the meeting. Collision safeguards and calibrated motion remain in use.

Stop meeting stops microphone capture and meeting input, clears mouth lighting and restores the previous scene, camera and view overlays. It leaves the robot undocked, cancels meeting motion and restores the controller enable preferences from before the meeting. Previously enabled controllers can be used again in normal editing after their inputs return to neutral. A camera started by the meeting is released; a camera that was already running is left running. Docking the view or starting timeline playback/recording also ends Meeting mode.

Meeting turns Drive HW off and sends meeting movement only to the URDF. Drive HW is not automatically restored on exit. A microphone failure stops the meeting. Webcam loss holds face-driven motion until fresh tracking returns; controller input for webcam-owned actuators remains reserved.

## OBS and Teams

The face tracker holds the physical webcam. Teams must not also open that webcam.

1. Start Meeting in the editor.
2. In OBS, capture the undocked URDF Model window and arrange/crop the source as needed.
3. Start OBS Virtual Camera.
4. Select OBS Virtual Camera as the camera in Teams.
5. Select the desired speech microphone separately in Teams.

The editor does not configure OBS/Teams or send microphone audio to Teams. Its microphone stream controls only the virtual mouth lights. The window title bar is separate from the hidden URDF overlays; crop it in OBS if needed.

## Shared appearance and live mouth preview

Scene Setup provides Studio, Solid color, Image, Moving image and 3D URDF scenery. Apply keeps those settings without a saved scene. Meeting inherits the complete chosen setup, including background motion and camera framing. Numeric animation commands from the editor timeline do not replace the meeting scene while it is active.

Selecting a microphone starts a URDF-only mouth LED preview in Meeting Configuration. Gain, noise gate, and release affect the preview immediately. Closing the window clears it; starting the meeting uses the same microphone settings.

The clean meeting view hides controls and temporarily disables Collision Warning. Collision Safeguard remains active. If the shared Clean view when undocked option is enabled, stopping the meeting keeps the undocked view clean; dock the view or turn that option off to show its overlays.

Hold a controller button mapped to Face Tracking Override to let the controller or a library pose take over face-mapped controls. Release it to resume webcam tracking. Pose controls that do not overlap face mappings work without holding override. Recording sessions still use their selected per-control takeover checkboxes.

Turning on Face tracking automatically starts the camera preview. Closing without starting stops a camera started by this window. Expand **Connect to OBS / Teams…** for brief connection instructions.

## Input readiness

The **Ready to start** card has separate Scene, Camera, Controller and Microphone rows. Messages explain what needs attention, such as choosing a scene, connecting a controller, or selecting an available microphone. Start Meeting is unavailable until the scene preview and required devices are ready. A connected controller is enabled on Start; its previous enable preference is restored on Stop. Physical output stays off after the meeting.
