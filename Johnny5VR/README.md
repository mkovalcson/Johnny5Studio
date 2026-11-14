# J5 Studio VR Player and VR Editor

An independent SteamVR viewer with an optional, separately built VR Animation Editor. The existing desktop editor under `../ServoAnimator/ServoAnimator` retains Face Tracking, Meeting mode and its 2D URDF interface. VR editor source lives in `../Johnny5VREditor`; its build does not import the original version-stamping target.

Extract the portable ZIP into a new writable folder, run **J5 Studio VR Player.cmd**, start SteamVR, and choose **Open J5 Studio VR Editor**. The A-button menu also opens, hides and recentres the editor workspace. Detailed controls and limitations are in **PORTABLE-START-HERE.txt**.

Version 0.6.1 uses persistent, double-buffered Direct3D textures for menu, editor and cursor overlays, avoiding SteamVR's asynchronous raw-image loader. Failed submissions retain the previous GPU image; recoverable menu errors report their operation and retry without ending VR. Both tracked controller rays render unlit even without a panel hit. The active right-hand pointer (left fallback) can point at menu rows and select with Trigger; stick navigation remains available.

The dedicated editor renders its own WPF windows/popups on its dispatcher and streams compressed pixel snapshots over a separate same-user pipe. WPF surfaces use RenderTargetBitmap, avoiding their PrintWindow repaint flicker. Version 0.6.2 additionally captures this editor process's native file dialogs, message boxes and native popup menus through their native paint path; these lack a WPF PresentationSource and were previously omitted. Images omit the desktop OS title bar; the outer VR frame supplies movement and resizing. Last-good images remain visible when a capture is missing or blank. These changes need final headset validation on the target PC.

## Laser and disassembly effects (0.8.0)

Standalone movies/sequences now evaluate FireLaser, HideLaser, ToolboxSwap and FallApart commands. Laser bolts glow and flash at the muzzle, with per-shot sounds mixed separately from voice audio. The optional accessory follows its saved None/Laser/Toolbox default and timeline commands. Disassembly uses the full robot's baked rigid-body clip, holds the scattered parts, and reverse playback reassembles before returning joint control. Seeking and movie sequence carry preserve effect state.

A > Robot effects provides timeline-free laser, disassemble and reassemble previews in standalone and VR editor modes. Pause playback first. Laser preview temporarily shows the laser; Clear effect preview restores timeline/default state. The full-body baked clip is bundled under its model's Meshes/Body/FallApart folder. The head-only model has no full-body disassembly clip.

## Controller posing

A > Pose robot with controllers opens the dedicated editor and hides its floating workspace. Pause/stop playback: all supported controls appear in stable label banks beside the robot, cyan for individuals and purple for gangs. Select a label with Trigger, release, then hold Trigger on the circular dial and point around its arc. The dial shows native limits, a green zero/default marker, and the current value. Other labels hide while you adjust and return when you release. Right stick up/down provides fine adjustment; Reset default previews zero. The X closes the dial without another change. Robot X/Y and heading have pageable local ranges to avoid hypersensitive motion. A hover leader identifies the selected robot location without covering the scene in lines. Head, eye/flap/vent gangs, individual body joints, hand-opening gangs and robot scene placement are available where supported by the model. The right controller points, with left fallback.

A > Insert changed pose at cursor inserts only the affected controls at the current sequence cursor as one undoable batch. Existing command conflicts use the editor's conflict chooser. This edits the timeline; save the sequence/movie through the editor as usual. Discard pose preview restores the timeline pose. Moving the timeline cursor, changing documents/models or starting playback clears an uninserted preview. Preview and insertion requests carry an editor context token and unique request ID. Posing cannot run during playback or modal dialogs, and never drives hardware.

## Live editing

The viewer launches its bundled `Editor/AnimationEditorPlayer.exe` with a selected Config folder and random, same-user named pipe. The editor sends its evaluated scene graph: cached geometry/texture deltas, current transforms, materials, ambient/directional lighting and media surfaces. Only the separate VR copy suppresses Face Tracking, Meeting and the 2D viewport. Desktop source and installed binaries are not rebuilt or replaced.

The compositor displays the dedicated editor's windows and dialogs in a framed panel. Trigger-grab the top bar to move it upright, or a highlighted corner to resize proportionally around the fixed opposite corner; release to place it. Grabs stay captured off-handle. Lock Layout prevents manipulation while leaving content usable. The toolbar has sequence Play/Pause, Stop, Undo/Redo, Show Robot, Bring Here and Lock. Grip/stick-click no longer move the workspace.

Inside the editor, Trigger clicks/drags and B right-clicks; the right controller points, with left as fallback. Pointer smoothing helps small controls. Right stick left/right pans the pointed waveform/spline/movie timeline, and up/down zooms around the pointed time. Over ordinary scroll panels and lists it scrolls vertically. Timeline and toolbar actions travel over the editor bridge on the WPF dispatcher; clicks remain scoped to the dedicated editor. Physical keyboard input remains available. Keep its desktop windows visible and unminimized.

Panel overlay/input errors are caught within the interaction layer: mouse buttons and grabs release, recovery instructions appear, and VR remains active for recoverable failures. Invalid poses and parallel resize rays are rejected. Focus/tracking interruption requires controls to be released before rearming. This does not establish the cause of a crash on another PC; test the new handles on the target headset and retain Diagnostics/viewer.log if a failure recurs.

Editor transport, spline evaluation, authoring, library tools, calibrated joints, smooth normals, iris blades/lens geometry, finishes, scene geometry and disassembly remain evaluated by the editor. TV/video/backdrop media become physical textured surfaces. The VR renderer approximates lighting, transparent materials and screen-space effects. WPF tone/FXAA settings and editor camera framing do not change the headset camera. Workspace capture is up to 15 fps; headset rendering and pose tracking remain independent. Mesh/texture assets are transferred only when changed, with transforms/materials updated separately.

The VR editor can save into the selected Config folder. The portable bundle has its own copy, so it does not affect a different machine's library. Selecting a shared folder deliberately shares documents; avoid editing the same document in another editor concurrently. Closing uses normal editor save dialogs and never kills the editor. Switching back to standalone playback leaves unsaved work open and pauses editor playback.

Physical robot output is disabled in the VR editor copy. Hardware control remains a desktop-editor feature; servo configuration and calibration still determine virtual motion.

## Standalone playback and navigation

The independent player retains movie/sequence selection, calibrated joints, audio, looping and manual Next triggers. The A-button menu supports stick navigation and trigger selection. Compatibility notices identify unsupported effects; use live editor mode for finishes, scene textures, video/TV and other editor effects. Standalone playback never writes project files or controls hardware.

Version 0.5 adds voice-envelope mouth illumination (front orange and red/green side LEDs), signed tread phases, roller rotation and trailing-caster steering. Track Forward uses the editor's cm/s units with deterministic 20 ms integration and exact command boundaries. Both tread belts counter-rotate during a spin. The portable model includes its `tread_animation.json` and every referenced phase mesh. Repeating live tread meshes reuse cached GPU buffers.

Version 0.5.1 also applies the editor's selected curved-surface smoothing to standalone playback: 35-degree creases, corner-angle weighted normals and unchanged geometry. Eye tubes/gimbals, head curves, vents, bellows/piston attachments and shoulder sleeves retain sharp rims. Both rear caster wheels now roll about their axles in standalone and live editor modes. Signed contact travel and fork rotation determine separate wheel distances, including during reverse and turns; timeline checkpoints restore these positions on seeks. Forks and stationary axles swivel without spinning with the tires.

All four 16-pixel eye/vent rings now follow authored RGB commands, including brightness, sides and animated patterns. The standalone emulator is ported from the VR editor, preserving Arduino color-order handling and movie-to-movie state carry. Front eye segments/diffusers and rear vent LEDs, halos and inner-tube glow use the model's physical dimensions. Eye/Vent Light Intensity and Audio LED Gain come from URDF configuration. These glow surfaces approximate the editor's rendering; the independent player does not reproduce every WPF point/spot light. No RGB command means lights start off, matching the editor. Human locomotion does not drive the robot's tread animation.

Left Index stick walks/strafe; right stick snap turns. Panel/menu interaction pauses locomotion. SteamVR standing poses and calibrated per-eye projection are unchanged from the headset-tested 0.2.3 behavior. Full robot defaults to six feet; room scaling is independent. Direct Index legacy axis mapping `[1,3,3,0,0]` is preserved. Optional action bindings include panel grab. No teleportation, VR keyboard, controller models or eye tracking yet.

## Build and checks

```powershell

$version = (Get-Content ../ServoAnimator/Build/PROJECT_VERSION.json -Raw | ConvertFrom-Json).version
dotnet publish Johnny5VR.csproj -c Release -r win-x64 --self-contained true -o "packaging/publish-$version" "-p:StudioReleaseVersion=$version"

dotnet publish ../Johnny5VREditor/ServoAnimator.csproj -c Release -r win-x64 --self-contained true -o "packaging/editor-$version" "-p:StudioReleaseVersion=$version"

dotnet "packaging/publish-$version/Johnny5VR.dll" --check

dotnet "packaging/publish-$version/Johnny5VR.dll" --smoke

dotnet "packaging/publish-$version/Johnny5VR.dll" --editor-smoke --config <isolated-test-Config>

python tools/build_portable.py --publish "packaging/publish-$version" --editor-publish "packaging/editor-$version"

python tools/verify_portable.py "../Releases/VR/$version/Johnny5VR-$version-win-x64-portable.zip"

```

Checks cover coordinate conversion, real models/backdrops, source preservation, playback, protocol round trips/deltas, invalid mesh data, panel anchoring and pointer mapping. Smoke tests render actual D3D output; editor smoke launches the dedicated editor, receives a scene, renders it and receives the editor-rendered window stream, then closes normally. Use a disposable Config for integration checks. SteamVR is not installed on the development workstation: the new panel needs physical headset/controller validation on the target gaming PC.

## Timing diagnostics

Every run writes `Diagnostics/bridge-timing-<start>.csv`, one row per second, and the desktop window shows a short summary under the frame rate. `mode` is standalone/editor plus vr/desktop; `panel` says whether the editor workspace is shown. For each step there are count, average and maximum milliseconds:

- Render thread: `Frame` (whole loop), `Work` (Frame minus `WaitPoses`, the time SteamVR makes the app wait), `Interval` (time between frames; `lateFrames` counts intervals over 1.5 × the headset budget), `Pump`/`ApplyLive` (applying an editor scene), `UpdatePose`, `Menu`, `Panel`/`PanelUpload` (editor workspace image upload), `Draw` (both eyes), `Submit`, `Mirror` (desktop copy of the VR view).
- Background: `SceneRoundTrip` (editor captures, encodes and sends a scene), `SceneDecode`, `DesktopRoundTrip` (editor renders its windows to an image), `DesktopCompose`, `PanelChrome`; sizes in `SceneKB`/`DesktopKB`; `DynamicVertices` rebuilt per scene; `DynamicBufferAllocations` counts GPU buffer creations (0 once warmed up); `SceneCycle` is start to start of scene requests, paced at 33 ms, so `SceneCycleN` is robot updates per second. See ../Documentation/VR-Bridge-Pace-And-Memory.md.
- Problem log: `Diagnostics/follow-problems-<date-time>.txt` lists Follow starts/stops and every headset, controller and input-focus dropout with its length in ms. Follow problems also pop up the overhead status panel in red for 8 s. See ../Documentation/VR-Follow-Problem-Display.md.
- Follow smoothing: while following, the player blends every piece between editor scenes and redraws it each headset frame (`Renderer.SmoothLive`, `StartBlend`, `AdvanceLive`); motion targets go to the editor once per scene request; the bridge paces with a high-resolution timer (`HighResolutionTimer`). See Documentation/VR-Follow-Smooth-Drive.md.
- Follow scene rate and treads (protocol 11): while following, the bridge adapts the scene cycle between 75 and 30 Hz from editor UI and player apply cost (`EditorConnection.TargetCycle`, logged as `SceneTargetCycleMs`); tread belt frames and travel travel in `VrFrame.Treads` and the player picks the belt frame every headset frame. See Documentation/VR-Follow-Scene-Rate-And-Treads.md.
- Wrist pivot and tracking recorder: calibration learns where the wrist is relative to the controller origin (`BodyTrackingCalibration.FitWristPivot`, `Wrist()`) and the solver measures arms from there; Follow sessions record raw tracking to `Diagnostics/tracking-<date-time>.bin`, replayed with `--replay-tracking`. See Documentation/VR-Follow-Wrist-Pivot.md.
- Calibration gauges and attempt log: relaxed wrist-roll gates (`CalibratedArmMotion.OppositeMinimumDot`, `AxisSimilarityMaximum`), an animated pose guide with live per-gate gauges (`CalibrationPoseGuide.DrawGauges`, `BodyCalibrationWizard.LiveGauge`), and each calibration attempt recorded to `Diagnostics/tracking-calibration-*.bin` with accepted/rejected steps in the follow-problems log. See Documentation/VR-Calibration-Wrist-Roll-Gauges.md.
- Waist swivel switch: `ViewerSettings.TrackWaistSwivel` (default false) / `VrBodyMotion.TrackWaistSwivel`; when off Follow drops `B_WaistSwivel` from every batch so the timeline keeps it. Follow settings menu row and desktop checkbox.
- TipToe (`B_TipToe`): standalone playback applies `TipToeGeometry.RootChildMotion` to root children in `AnimationRig`; see Documentation/TipToe/README.md.
- Process: garbage collections per generation, `gcPauseMs`, `allocMB` per second, and CPU cores used by the player and the editor.
- SteamVR (VR only): app GPU time, frames shown more than once (`vrReprojected`), dropped and mis-presented frames, and how many frames SteamVR flagged with a CPU or GPU reprojection reason, async reprojection or Motion Smoothing.

In desktop preview the window waits for vsync, so `Work` includes that wait there; use VR runs to judge it. Compare a standalone playback run with an editor run, panel shown and hidden.

The Player requests editor window images only while the VR workspace panel is shown (and during editor smoke). Each image costs the editor's UI thread 100–300 ms, which otherwise delays robot scene updates.

Packaging includes self-contained .NET runtimes, both apps, robot/backdrop runtime meshes (visual and collision), library/projects, calibration and referenced media. CAD originals and source drawing/documentation archives are excluded. ZIP contents have per-file hashes, verified after extraction to a fresh path containing spaces.

## Dependencies

SharpDX 4.2.0 (MIT), NAudio 2.2.1 (MIT), editor dependencies listed in its project, and Valve OpenVR 2.15.6 (BSD). Runtime and dependency notices accompany the portable package. SteamVR, headset drivers and graphics drivers are installed separately. OpenVR references: https://github.com/ValveSoftware/openvr/tree/v2.15.6 and https://github.com/ValveSoftware/openvr/wiki/IVRCompositor_Overview.

