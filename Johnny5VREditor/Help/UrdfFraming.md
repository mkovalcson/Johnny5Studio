# URDF Scene Setup

Open **Config > URDF Scene Setup**. The five tabs separate **Robot**, **Background**, **Robot Position**, **Camera**, and **Animation**. The saved-scene selector and the name of the scene being edited stay at the top. Apply, Save Scene, Save Scene As… and Close stay at the bottom.

## Apply or save

Changes preview immediately in the 3D view. **Apply** keeps the current robot, background, placement and camera after closing and stores them as the editor's current defaults. It does not create or overwrite a saved scene, assign a scene to a sequence, or add timeline commands. Meeting can inherit the applied setup using **Use current editor setup**.

**Save Scene** writes the complete setup to a reusable named scene and also applies it. **Save Scene As…** creates a separate scene. The Editing line indicates whether the saved scene matches the current setup.

When closing with preview changes, choose **Apply and Close**, **Discard Preview**, or **Keep Editing**. Discard restores the setup from the last Apply, explicit load or scene save. Saving a background preset alone does not retain the complete setup. Playback, recording and sequence changes still close Scene Setup automatically and discard uncommitted previews.

## Robot

**Change Robot Model…** chooses and loads the robot URDF in one action. Scenery is selected separately in Background. Keep the model's mesh folders alongside its URDF file.

Loading a robot retains the background and uses that model's previously saved framing when available. Otherwise it keeps the current framing. Robot selection and framing defaults are stored in Config/UrdfModelSettings.json.

## Background

Choose a background type and configure it directly. Expand **Reusable backgrounds** to load or save scenery presets:

- **Studio:** the standard viewport background.
- **Solid color:** enter a color or choose Chromakey Green.
- **Image:** import an image. It fits proportionally inside the viewport, with the whole image visible and one dimension filling the view.
- **Moving image:** animate the image with smooth random pan, zoom and optional rotation.
- **3D URDF scenery:** choose the scenery URDF, separate from the robot URDF.

Only settings relevant to the selected type appear. Image selection starts in Config/Backdrops when that folder exists and accepts files from anywhere. The selected image is copied into Config/Backdrops and referenced relative to Config. Selecting the same unchanged file reuses its copy; different images with the same name and changed source files receive separate copies. The original file is preserved. Pasted image paths are imported when applied, and older backdrop paths are copied into this folder when used. Static images are centered without cropping or distortion, leaving margins when the viewport has a different shape. The robot scales with the fitted image, and horizontal/vertical placement uses the image area. Saved zoom, orbit and placement values are retained. Older static-image Stretch, Cover and Tile settings now use this proportional fit as well. Moving images and 3D URDF scenery retain their existing framing.

Moving-image settings include speed, background frame-rate cap, minimum/maximum zoom, pan amount, target interval, random seed, gentle rotation and 180-degree swaps. Minimum zoom is 100%; safe pan/rotation coverage prevents empty edges. A repeated seed and settings reproduce the motion. The background frame-rate cap is independent of robot rendering.

Moving backgrounds update within the shared visual frame cycle, after robot animation
and before Direct3D synchronization/drawing. The default cap remains 15 fps and can be
set from 1 to 60 fps. The cap controls how often motion is sampled; motion speed uses
elapsed visible-frame time. Late frames skip to the current position, and hidden time
is excluded when the view resumes.

**Save Background Only…** creates a reusable scenery preset with a name, category and description. **Manage Backgrounds…** opens the background library. Loading a background preserves the selected robot, robot framing and room position. A background is not a complete scene.

**Clean view when undocked**, under **Camera > View options**, hides the viewport controls, fps and legend, and suppresses collision warnings while undocked. It does not automatically lock the camera. Docking restores normal controls.

## Robot Position

### Orientation and room placement

- **Spin:** cumulative degrees around the neck; values can continue beyond one revolution. Enter a value or use the fine-adjustment slider.
- **Roll / tilt sideways:** clockwise/counterclockwise roll, from −360 to +360 degrees.
- With a 3D background: robot X/Y in metres, heading in degrees, movement mode and floor alignment.

Under Movement and floor alignment, Keep robot framed (Robot Centric) keeps the robot framed while scenery moves around it. Move robot through room (Room Centric) uses perspective: moving toward the camera makes the robot larger, and the camera follows near the edges. Floor alignment can be detected from the scenery or set manually.

## Camera

To switch framing during a sequence, add [Camera Shot commands](CameraShots.md) on the **URDF/Scene** timeline.

- Horizontal and vertical framing use viewport percentages. Positive horizontal moves right; positive vertical moves down, including below the window.
- Camera Zoom starts at 10%; 100% is the original camera distance. Opening the window does not alter the current zoom.
- Camera orbit yaw and pitch are entered in degrees.
- For 3D scenery, Background camera controls adjust pan, zoom, view yaw and the edge-follow margin. In Robot Centric mode, background zoom keeps the robot's screen size.

Scene Setup stays open alongside the editor. Drag or wheel the URDF view to adjust the camera; the orbit and zoom values update in Robot Position and Camera. The tab controls also update the view. Background pan X/Y/Z and zoom have both sliders and numeric entry. Camera locking still applies to orbit gestures. Scene Setup closes when playback, recording or Meeting starts, or when the active sequence changes.

**Animation > Record Position** saves Slide, Vertical, Zoom, Spin, Rotate and camera orbit yaw/pitch at the cursor. For 3D backgrounds it also saves robot room X/Y/heading, background pan/zoom/yaw, the edge-follow margin, and manual floor height when automatic floor detection is off. Saving again at the same time updates these channels. These numeric commands never drive physical hardware.

Leave **Smooth movement between recorded positions** checked to enable smooth motion: save a position, move the timeline cursor, adjust the view, and save another position. URDF curves are dotted and scaled into the graph's −100…+100 display range; hover a point to see its native value. Distances for new background pan/floor commands and room X/Y are stored in centimetres; angles use degrees and zoom/margin use percentages. Discrete choices such as the model, background type, movement mode and automatic floor detection use a saved scene or Load Scene rather than spline interpolation. Break Spline holds the starting value until the next command and hides the displayed segment.

## Save or load a complete scene

A scene includes the **robot URDF model**, **background**, **robot placement**, and **camera/framing settings**.

- Choose a scene in the top list and press **Load Scene…** to restore the complete setup.
- **Save Scene** updates the currently loaded/saved scene; if none is active, it asks for a new name.
- **Save Scene As…** creates another named scene, with category and description.
- **Animation > Save Scene and Assign to Sequence** saves the edited scene and assigns it to the active sequence. Save the sequence normally afterward.

Loading a robot, background or scene applies it immediately and retains it as the current default. Apply retains the complete current setup without changing any scene file. Close asks how to handle preview edits made since the last Apply, load or scene save.

Use **Animation Library > Manage Library Scenes…** for the scene library and **Manage Library Backgrounds…** for scenery presets. Scene files live in Config/LibraryScenes; backgrounds live in Config/LibraryBackgrounds. Imported images live in Config/Backdrops. Existing scenes are not rewritten merely by opening them; their managed image reference is retained when you next save. Robot and scenery URDF files reference their existing mesh folders; the library does not copy these models.

## Sequence scenes

Choose **Animation Library > Sequence Scene…**, select a scene and press **Use for sequence**. **No sequence preset** removes the assignment. Assignment changes support Undo/Redo and recovery snapshots.

A sequence loads its assigned scene before applying timeline background and movement commands. Switching to another sequence restores the previous base robot and framing before loading the next assignment. Load Scene commands override the complete scene at their timeline times; seeking before them restores the preceding scene. Older embedded Load Backdrop commands continue to change scenery/framing without replacing the robot model.

Older background/framing assignments remain readable through the existing `urdfBackgroundFile` field. They keep their original framing behavior and do not replace the robot model. New assignments point to complete scene files. Missing or invalid assets produce a status message. Keep referenced model and scenery files available when moving a project.

Button explanations appear after a short hover, offset to the side of the Scene Setup window. They do not receive mouse clicks. The complete-scene save controls stay below the tabs; Save Background Only stays under Background > Reusable backgrounds.
