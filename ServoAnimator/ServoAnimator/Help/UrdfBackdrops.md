# 3D URDF backdrops and stage movement

In **Config → URDF Scene Setup > Background**, choose **3D URDF** from Background, then use **Choose background URDF…** to select the actual scenery file. It may be anywhere; there is no required backdrop folder. If a saved/default path points to a folder that was moved, browse to its new location. Mesh paths resolve relative to that URDF. Save Scene persists the complete configuration.

The backdrop and robot share the same 3D viewport. Backdrop geometry is visual scenery, excluded from the robot collision model. It does not add room/wall collision avoidance or physical driving commands.

## Position and view

- **Robot X / Robot Y**: robot position in the room, in metres. At heading zero, forward is +X and left is +Y.
- **Heading Degrees**: robot heading relative to the room. Positive turns left. This is independent of the existing robot Spin and Rotate controls.
- **Detect Floor**: use the top of the first box visual on a link with “floor” in its name as ground level. For other floor geometry, turn this off and enter Floor Height in room coordinates. The robot’s neutral model bounds establish tread/ground height; an unusual model may need a floor adjustment.
- **View Pan X/Y/Z**: stage view offsets in metres.
- **View Zoom Percent**: 10–1000%, with 100% initially.
- **View Yaw Degrees**: view orientation around the stage.
- **Preserve Robot Framing**: defaults on. Entering the stage retains the robot’s current horizontal position, vertical position and camera zoom, and clears initial Spin/Rotate so stage heading can turn the robot independently.
- **Robot Position and Camera** tab: adjust robot framing and camera orbit. Its yaw/pitch entries are degrees; API framing objects store yaw/pitch in radians. Camera zoom is a percentage and Spin/Rotate use degrees.

**Robot Centric**, the default, keeps the robot at its screen position. Movement translates the backdrop around it. Stage zoom scales only the backdrop; the robot’s on-screen size stays unchanged. Heading still turns the robot relative to the room.

**Room Centric** moves the robot through the room with perspective, so it becomes larger approaching the camera and smaller moving away. The view follows laterally once it reaches the edge margin. **Edge Margin Percent** controls that follow threshold (5–45%). Near-camera following prevents crossing the camera plane. Stage zoom affects the complete perspective view in this mode.

## Load Scene timeline command

First save a complete scene in **Config > URDF Scene Setup** using **Save Scene** or **Save Scene As…**.

Add a timeline command, select **Load Scene**, and press **Choose Scene…**. Select a saved scene JSON file; the picker starts in **Config/LibraryScenes**. The command references that file and loads its robot URDF model, background, placement and camera together at the command's time. Edit these settings in Scene Setup rather than in the timeline command.

Seeking backward restores the preceding scene, or the sequence's assigned/default scene before the first command. Keep the scene file and its referenced assets available. Re-entering a command loads the saved file again, so later edits to that scene are used. Missing or invalid scene files produce a status message. Timeline loading does not overwrite the application's saved default scene.

Older Load Backdrop commands with embedded settings still play. They now display as Load Scene; choosing a scene replaces those embedded settings with a scene-file reference. The internal `LoadBackdrop` command identifier remains unchanged for existing files and API clients.

For smooth robot movement, add **URDF StageX**, **URDF StageY**, and **URDF StageHeading** numeric commands. X/Y use whole centimetres (100 = 1 metre); heading uses degrees. Enable Spline and provide start and destination commands. Without Spline the value changes immediately. Existing URDF Slide/Vertical/Zoom/Spin/Rotate commands still control robot framing. A Load Scene starts a new framing baseline; earlier framing commands do not override it. Numeric stage controls also appear in controller mapping targets.

All backdrop/stage commands are URDF-only and are omitted from physical-model exports. Scenery is loaded when its file changes, rather than once per rendered frame.

## API

`get_state` now includes `urdf`, and `get_urdf` provides the same view/background/stage state. See [Editor API extensions](EditorApiExtensions.md) for `set_backdrop`, `set_urdf_framing` and `move_robot`. API edits require a fresh token and unique request ID, paused playback and Drive HW off, and are added to the active sequence.

## Track Forward speed command

Add **Track Forward** in Edit Commands to drive through the 3D URDF space relative to the robot's front (stage heading plus the model's Spin/Rotate orientation projected onto the floor). The **Speed (cm/s)** value ranges from −500 to +500: positive moves forward, negative reverses, and 0 stops. For example, 100 means 1 metre per second. This is the numeric command value, not the hardware Default/Slow/Fast speed profile. It requires a 3D URDF backdrop and never drives physical tracks.

Speed holds until another Track Forward command. Add a zero-speed command at the desired stopping time. Spline interpolates speed for acceleration/deceleration, and URDF StageHeading or Spin changes steer the path while moving. Camera orbit and backdrop-view rotation do not redefine forward. Reverse travels directly opposite the robot’s front. Translation is added to the StageX/Y position; a new Load Scene resets accumulated travel and starts with speed zero unless there is a Track Forward command at that time. Hidden groups continue contributing to movement; Hide Group only changes their display. Movement uses sequence time, so playback speed changes affect it with the rest of the animation, and backward seeking reconstructs the same path without accumulating drift. Pausing playback stops movement.

For Xbox or Steam, select **Track Forward** as a joystick target. Low/High specify reverse/forward speed limits in cm/s; Invert reverses the stick direction. Stick deflection controls velocity directly, regardless of a previously saved Relative/Set response. Centre is always zero. A button can also hold forward speed while pressed. Releasing input, changing MUX, losing the device, disabling control or stopping output stops controller movement. Config-window testing uses the same behavior. Robot Centric moves the backdrop around the robot; Room Centric moves the robot in the room.

The API accepts `TrackForward` through `insert_commands`, with signed speed in `value`. See the API extensions for an example.

## Animated treads and hubs

When the robot model contains `Meshes/Body/Treads/tread_animation.json`, the viewer automatically loads its tread frames and animates them with **Track Forward**. Positive speed advances both tracks in the forward-driving direction; negative speed reverses them; zero holds their current phase. **URDF Spin** adds opposite motion to the two tracks: a positive/left turn reverses the left tread and advances the right tread. Stage-heading turns use the same animation. Forward travel and turning combine, so on a moving turn one tread runs faster than the other.

Track spacing is read from the model's front roller centres. The supplied model has a 0.42-metre centre-to-centre spacing. The front hub caps and bolts rotate continuously using the manifest's rotation ratio, synchronized to belt distance. Timeline seeking reconstructs belt travel and turn phase, including spline speed changes, rather than accumulating motion from rendered frame count. Controller motion uses the same visual animation.

Frames are preloaded and frozen once per model load. Only the displayed geometry changes when the phase changes; static bands, track frames and collision geometry remain stationary. Hidden/minimized views defer mesh updates until visible again. Clean Undocked View and Meeting do not gain extra controls. A model without a valid manifest stays static; missing layers are skipped with diagnostic logging. This is visual only and sends nothing to physical motors.

## TV screen media

Use **URDF TV Front**, **TV Show**, and **URDF TV Return** to present screen media. See **URDF TV and Screen Media** for a step-by-step workflow and supported formats.

## Rear caster swivel and wheel rolling

The full-body rear caster wheels automatically swivel with the drive animation. The wheels trail behind an offset swivel, so they behave like a real castor: the robot's motion rolls them round, through a full 360 degrees.

- **Forward:** the wheels trail straight behind the swivel.
- **Reverse:** the wheels swing round until they sit in front of the swivel, under the caster arm. They are about halfway round after 30–40 cm of reversing and fully round by about 80 cm. Driving forward again swings them back the same way.
- **Turning:** a spin swings them across the swivel's circle, angled slightly outward. Reversing the spin swings them over to the other side. A moving turn settles them between these.
- **Stopped:** the caster stays where it is. It only swivels while the robot moves, so how far it has swung depends on distance travelled, not time.

The swing rate depends on the trail (the distance from the swivel to the axle) measured from the loaded model. A model whose wheel sits directly under its swivel keeps the earlier behaviour, turning to at most 90 degrees either way.

The yoke, fork, axle, wheels and hubs swivel together around the caster pivot; the support arm, spring and mounting head stay fixed. This works for timeline commands, controller motion and commands inserted through the API. Timeline seeking reconstructs the swivel from the preceding motion, and pausing freezes it at the corresponding timeline state. Models without the expected caster parts retain their existing appearance. The animation is URDF-only.

The twin caster tires, tread blocks and hubs also roll around their wheel axles as the robot travels. Forward/reverse movement and turning determine the signed rolling distance; the two wheels travel different distances as the fork turns. The fork and axle steer without spinning with the tires. Timeline seeking restores wheel rotation as well as steering, and stopping or pausing holds their current positions. Both WPF and Direct3D 11 use these visual transforms.

### Continuous Spin
Spin retains cumulative degrees: 0 → 720 makes two positive turns; 0 → -720 makes two negative turns. Enable Spline and space the endpoints to control turn speed. Rendering wraps the visible angle only; treads, caster steering and Track Forward retain the continuous turn count. The framing window offers a cumulative angle field, ±360° buttons and a local fine-adjustment slider. Timeline/API commands retain their existing signed 32-bit whole-degree storage (approximately ±5.96 million turns); live controller/framing rotation is not clamped to that range.
