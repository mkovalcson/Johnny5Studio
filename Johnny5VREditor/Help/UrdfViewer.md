# URDF 3D Viewer

The URDF viewer renders the selected robot model, including supported full-body joints, and follows command editing, timeline playback and live input.

## Preview controls

The studio-gradient viewport keeps the familiar lower-left vertical control stack,
with camera controls in a separate row beneath it and the legend at lower right.
Pose and Face Reset sit near the bottom-center handle. Library Pose actions appear
above the preview controls while Pose is enabled:

- Collision Warning: On/Off
- Drive: On/Off
- Undock/Dock
- ← / Recenter / → camera row
- -90° / +90° camera turns

Drive controls only visual URDF updates. Turning URDF Drive off freezes the model pose to reduce rendering work; it does not disable Drive HW.

## Docking

Undock opens the URDF Model window and removes its pane from the editor. Commands expands across the freed width. The URDF Model window uses the standard Windows maximize/restore controls.

If the URDF Model window is not visible, use Dock URDF in the Commands header.

## Docked height

A small handle centered at the bottom of the docked URDF view can be dragged vertically. The URDF can extend downward through the audio/spline region while the Commands row remains uncovered. The selected layout is saved in `EditorLayout.json`.

## Camera

Use normal mouse controls to orbit and zoom. **Recenter** returns yaw and pitch to straight-on while preserving zoom. The **←** and **→** buttons immediately to either side rotate camera yaw by exactly 90 degrees. Framing and floor placement depend on the selected model and scene; adjust them in URDF Scene Setup. When the model is undocked, the current docked camera view is carried into the URDF Model window; docking carries the current view back. Camera orientation and zoom are saved in `EditorLayout.json` and restored at the next launch.

Mouse-wheel zoom is centered on the surface under the pointer. Horizontal and vertical framing adjust automatically to keep that spot in place. Over empty space, the camera's focus plane supplies the anchor. This works in WPF and Direct3D 11, including fitted static-image backgrounds and 3D scenery, within the existing zoom and placement limits. The compensated placement appears in URDF Scene Setup and is retained with saved framing. Wheel input over the viewport controls does not zoom the model.

The URDF camera and its on-screen controls remain operational while the modeless **Edit Commands** window is open.

## Pose editor

The **Pose** button is positioned between the camera-right control and the bottom-center resize handle. Pose mode is a non-destructive URDF drafting mode: timeline refreshes do not overwrite the draft while Pose is active. Leaving Pose mode without inserting it restores the current timeline pose.

- The eye bullseye is constrained to the eye-tube target circle. Center is centered eyes; an axis at the circle edge is 100% of that eye travel. Diagonal positions mix horizontal and vertical movement. Horizontal bullseye motion uses the visually reversed command-axis mapping required by the eye mechanism.
- In **LR Joined**, the gaze bullseye is on the robot's right eye and the shared iris slider sits immediately below the robot's left eye tube. **LR Split** exposes both eye targets and places an iris slider immediately below each eye tube.
- Flap Open/Close, Nose Body, and Nose Basket are vertical sliders centered on the overall robot-head frame rather than the moving NoseBody. Flap Open/Close is centered, Nose Body is on the robot-right side, and the half-height Nose Basket slider is on the robot-left side. In **LR Split**, Nose Body/Basket stay in those same locations while the left/right Flap Open/Close sliders appear side-by-side at the center. **T** handles still control upper-flap tilt. The Vent control is a 90° slider arc following the CAD outer edge of the robot-left eye tube from the top down to the side. In **LR Split**, a mirrored arc appears on the opposite eye and the two vents are controlled independently.
- Eye Pop is anchored 15 mm outside the physical head sides. The bottom of each vertical Eye Pop slider is aligned with the projected top of the mouth, so that relationship follows the model through head/camera motion. Joined mode uses one outside slider; Split mode exposes independent left/right sliders.
- NeckTurn uses a 50%-larger editable circular dial positioned 75% of the way from the bottom-center resize-handle reference toward the lower-right legend. Its zero pointer is straight down and acts as a top-down heading indicator for the front of the head. A **↺** reset is centered inside the dial. NeckNod remains anchored at the physical front-middle of the Fabco neck assembly, while NeckTilt remains anchored to the physical robot-left Fabco cylinder midpoint; Nod and Tilt combine into the two neck actuators. Each has its own value; moving one retains the other.
- Whip antenna height and rotation controls are anchored at the top-left edge of the URDF viewport; MFRC height and rotation controls are anchored at the top-right. Both groups keep their 8-pixel top inset even in narrow panes. Microphone Up/Down sits just left of the RGB Command box. Antenna height-reset buttons sit beneath their sliders.
- Straight pose sliders use pale rounded tracks and blue, white-bordered handles without numeric labels. Their existing ranges, dragging, keyboard controls and reset actions are unchanged.
- Every Pose slider has a small **↺** reset button positioned toward the outside of the robot relative to that control; each visible Vent arc has one as well. The NeckTurn, Whip rotation, and MFRC rotation dials have reset buttons centered inside their circles. Resetting NeckNod or NeckTilt resets that component while retaining the other.
- While Pose mode is on, **Face Reset** appears immediately to the right of the Pose button. It neutralizes eye gaze, irises, flap open/close and tilt, vents, Nose Body, Nose Basket, and Eye Pop without changing NeckTurn, NeckNod, or NeckTilt. Face Reset also clears the Pose RGB Command and applies the same lighting-off result as `ClearAll` to the URDF preview and Drive HW hardware.
- **LR Joined** is the default. **LR Split** exposes separate eye targets/iris sliders, eye-pop sliders, mirrored left/right Vent arcs, and left/right flap controls so asymmetric poses can be created.
- The taller **RGB Command** text field and **Build** button above LR Joined/Split use the normal Build RGB Command window. The result previews immediately on the URDF and follows Drive HW for hardware output.
- Pose controls use the same normalized authoring limits and calibrated URDF endpoints as the rest of the application; their handles cannot drive the URDF past its configured range.
- Right-click the sequence timeline and choose **Insert Pose** to commit the complete visible URDF pose at the cursor. Split values are written as individual child-servo commands when required, and a Pose RGB command is inserted at the same timestamp when one has been added.

- While Pose mode is active, **Library +** and **Library Load** appear above Collision Warning. **Library +** saves the complete current URDF pose (including an optional Pose RGB command) as a reusable Library Pose. Unless you attach a custom image, it also automatically captures a clean centered URDF PNG with Pose controls hidden and crops it around the head, flaps, and neck. **Library Load** selects a saved Library Pose and applies it directly to the URDF Pose editor without inserting anything on the timeline.
- Each visible eye-gimbal target circle has a small **↺** reset just outside the circle on its inner-facing edge. Joined mode resets both eye axes together; LR Split provides an independent reset for each eye.
- Vent quarter-arcs are projected from the actual 44.45 mm-radius CAD outer eye-tube edge, so the arc and handle remain attached to the tube under camera orbit and head motion.
## Rendering performance

Visual updates share one ordered display-frame cycle: timeline and live-input targets,
robot motion, moving background, Direct3D scene synchronization, then Direct3D drawing.
WPF draws the updated scene after the frame callback. Duplicate frame notifications do
not repeat this work, and delayed frames sample the current state rather than replaying
missed visual frames.

The cycle retains the individual limits: timeline pose updates target 30 Hz, calibrated
motion and Direct3D drawing target up to 60 Hz, and moving backgrounds use their selected
1–60 fps cap. Background motion pauses while hidden and resumes without counting hidden
time. Audio buffering, controller/webcam polling and hardware output queues retain their
existing timing; the shared UI thread can still delay command generation under heavy load.

### Optional Direct3D 11 preview

The supplied head and full-body models use smooth shading on curved eye gimbals,
eye cylinders and lens rings, the nose body and basket, both sets of vent fins, curved side panels near the
whip antenna, the black side panel and rear head covers. Sharp rims, flat faces,
vent slots and holes remain distinct.
This is applied once when the model loads in either renderer; the source STL files,
dimensions, joint motion and collision geometry remain unchanged.

Open **Config > URDF Configuration** and find **Renderer and appearance**, below Vent Light
Intensity. Select **WPF** or **Direct3D 11** there. Each renderer has independent appearance
sliders, so switching between them restores its own settings for a live A/B comparison.
**Save Default** saves both profiles and the selected renderer. New, reopened and undocked
views use that selection. WPF is the initial default when no renderer choice has been saved.
DX11 resources are released when a view unloads. DX11 failures automatically return the
view to WPF with a status message.

DX11 uses the same URDF meshes, joint transforms and animation state. Backgrounds, TV/video,
pose controls and other WPF overlays remain composited around the 3D scene. Lighting, specular
highlights and transparency sorting can differ from WPF; this is an experimental preview option.
Library thumbnails and API snapshots temporarily use WPF with the active appearance
profile because bitmap capture cannot reliably read the DX11 surface. Robot Frames window
captures use the configured renderer. See [URDF Configuration](UrdfConfiguration.md) for
the lighting and tone controls. Review appearance and real movie/Meeting playback when
choosing a renderer.

The viewer skips the hidden back sides of closed, opaque CAD parts. Exterior
surfaces and moving parts retain their original detail. Open and transparent
surfaces keep both sides, and collision checks continue to use the original meshes.
The optimization is automatic; changed model files fall back to full-detail rendering.

Timeline playback targets **30 pose/slider updates per second**, while calibrated
URDF motion and controller visual updates run at display cadence, up to **60 per second**.
Audio, timeline cursor updates, controller polling, and hardware command dispatch
retain their independent timing. Delayed preview frames skip ahead to the current playback
position rather than replaying a backlog. Command-time collision checks are retained.
Controller visuals coalesce the latest values between frames, including Library
poses/sequences and config testing. Controller polling and its hardware dispatch are
not throttled by the visual update queue. Live timeline spline targets are generated at the timeline pose refresh rate, approximately 30 updates per second, and pending samples coalesce if hardware writes are slower. The exported spline sample frequency is a separate setting.

The `fps:` indicator above the legend estimates **WPF render-callback frequency**
while the URDF view is visible. Each unique rendering timestamp counts once,
including when the model is still or only the camera moves. The whole-number
rate covers the last second and the label refreshes at most twice a second, only
when the value changes. A stalled render stream decays to zero.

Sampling uses fixed storage with no per-frame allocations. The counter does not
advance the model or explicitly request a redraw. Its label uses a low-priority
timer and fixed width to minimize UI work. Hover over the indicator for its meaning.
This is a rendering-cadence estimate, not a measurement of completed GPU frames;
other UI activity can still change WPF's scheduling and therefore the measured rate.
Actual speed depends on the graphics hardware,
window size, and other editor activity.

## Webcam preview and clean Meeting view

Webcam Face Tracking offers Drive URDF preview for testing applied face mappings without physical output. Explicit left/right neck child commands can combine pitch and roll, including LPiston/RPiston-derived commands.

Meeting mode undocks the head and hides pose controls, the control stack, status, FPS and resize overlays. Its configuration displays the current background and can temporarily override it with a color or image. Stop meeting restores background and overlay visibility but leaves the view undocked. The ordinary window title bar can be cropped in OBS. F1 in the active meeting viewport opens Meeting Mode help.

## Model placement and backgrounds

For model files, backgrounds, placement, continuous Spin, zoom and Record Position, see [URDF Scene Setup](UrdfFraming.md). For scenery, travel, treads and casters, see **3D URDF Backdrops and Stage Movement**. TV media and Disassemble each have a dedicated help topic.

Clean Undocked URDF View hides buttons, FPS and the legend and temporarily turns Collision Warning off. It does not automatically lock the camera. Saved scene settings determine the retained framing; docking restores the hidden overlays.
