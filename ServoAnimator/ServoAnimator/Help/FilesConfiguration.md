# Files, Configuration, and Open Recent

## Configuration folder

J5 Animation Editor & Player uses the selected Configuration folder from `Paths.json`. A relative `Config` path refers to the folder beside `AnimationEditorPlayer.exe`. If the saved folder is missing, the application first looks for that adjacent `Config` folder, then the development workspace's `Config` folder, and finally uses the executable folder. When no paths are saved and neither layout is available, the application prompts for a Configuration folder.

Movie, Sequence, exported animation, Audio, and Library selections are limited to the active Configuration folder and its child folders. References saved in JSON are relative to that folder, so the complete configuration can be moved or deployed without rewriting drive-specific paths. Older absolute references inside the active Configuration folder remain readable and are converted when saved.

Application path fields and tooltips show paths relative to Config, for example `Models/Robots/Johnny5FullBody/johnny5_head.urdf` or `Projects/MyProject/Sequence.json`. Typed relative paths are resolved from Config. This also applies to embedded command settings, recovery data, controller mapping selections, Library pose artwork and remembered frame-export folders. Older references remain readable. An external file on the same drive is shown with `..` to indicate its location outside Config.

The Config location in **Set Paths** remains the location anchor. Windows file pickers use normal filesystem locations, and a path on a different drive cannot be expressed relative to Config. URDF mesh/texture filenames and model animation sidecars follow their model format and remain relative to the URDF/model folder.

File > Export/Import Movie ZIP and Export/Import Sequence ZIP transfer editable documents with their media and scene dependencies. ZIP files may be selected outside Configuration; imported contents go into the appropriate subfolders of the active Configuration folder. Review every existing/incoming date and size before choosing Proceed. URDF models are transferred separately using Export/Import URDF Model ZIP. See [Save, export and reuse your work](Saving.md).

Common persistent files include:

- `ServoConfig.json` - physical Maestro/Tic servo configuration override.
- `URDFconfig.json` - optional URDF calibration override.
- `EditorLayout.json` - window placement and size, splitter positions, dock and panel layout, and URDF camera orientation/zoom.
- `RecentFiles.json` - last active Movie/Sequence and the 10-item Open Recent history.
- `EditorRecovery.json` - temporary autosave data for unsaved Sequence, Movie, and configuration work. It is removed when everything is saved.

The editor writes recovery data every 30 seconds while anything is unsaved. After an interruption or a close with unsaved work, the next startup offers to restore it. Autosave never overwrites the selected Sequence, Movie, or configuration files.

## Automatic reopen

When the application closes it remembers the last active Movie or standalone Sequence. The next startup automatically reopens it when the file still exists.

Internal sequences selected while a Movie is open do not replace the Movie as the logical last-open document.

## Open Recent

File > Open Recent lists the last 10 Movie/Sequence files with the most recently opened first. Missing files are shown as unavailable rather than causing an error.

## Repairing missing files

When a loaded Sequence or Movie refers to unavailable audio or sequence files, the repair window opens automatically. It is also available from **File > Repair Missing Files**. Use **Locate**, **Search Folder**, **Replace All Matching Paths**, or **Remove Reference**, then click **Apply**.

## Saving

Opening another Sequence, loading another Movie, or starting a new workspace checks for unsaved documents before replacing them. Choose Save, Discard (No), or Cancel; a canceled or failed save leaves the current document available. Closing the editor retains the existing recovery-snapshot behavior.

Save Sequence and Save Movie write to the current source file. The corresponding Save As command chooses a new path within the active Configuration folder. Save Movie falls through to Save Movie As when a new movie has no path yet.

There is no bottom status bar. An asterisk directly after a Sequence or Movie name indicates unsaved changes, including description edits. Unsaved-change prompts remain active.

**File > New** clears both the current Sequence and the current Movie workspace, including Movie blocks and Movie metadata.

## Capture and meeting settings

Face tracking uses the active Configuration folder. FaceTracking.json stores the face mapping set, camera index, row neutral values and versioned piston settings. ControllerRecordingSettings.json stores recording-window choices when Ready to Record or Discard / close is pressed, including parent/child selections and microphone preference. MeetingSettings.json stores the meeting input, microphone, LED response and the choice between the current URDF setup and a saved scene.

Microphone WAV takes are stored beside the named sequence as SequenceName_Take#.wav. Keep retains the file and Play command; discard deletes both for that draft. Undo after Keep retains the WAV for Redo. Meeting creates neither WAV takes nor timeline commands. Selected backdrop images are copied into Config/Backdrops and referenced relatively, so the originals can remain elsewhere. Older image references are imported there when used; saving a scene, defaults or embedded timeline background retains the managed reference.

## URDF model location

**Config > URDF Scene Setup > Robot > Change Robot Model…** chooses and loads the URDF file. Robot defaults and per-model framing are stored in UrdfModelSettings.json under Config. Runtime robot models belong in `Config/Models/Robots`, and 3D backdrop models in `Config/Models/Scenery`, with a separate folder per model containing its URDF and required meshes, textures and animation data. Image backdrops belong in `Config/Backdrops`.

Relative robot and backdrop model paths are resolved from the Configuration folder. For example, `Models/Robots/Johnny5FullBody/johnny5_head.urdf` and `Models/Scenery/MatrixConstruct/MatrixConstruct_backdrop.urdf` remain usable when the complete Config folder is moved. Saving defaults, scenes and embedded timeline backgrounds retains relative references for models inside Config. Older external model paths remain supported. New URDF ZIP exports use the Robots or Scenery layout; older ZIP layouts remain readable.

Keep complete authoring folders, including STEP files, drawings, documentation and source tools, as separate source archives. Runtime copies do not replace or remove those originals. Builds do not copy or migrate the active Configuration folder.

Use **Save Scene** to retain robot, background, placement and camera together, or **Save Background Only…** for scenery alone. Scene files live in Config/LibraryScenes; scenery presets live in Config/LibraryBackgrounds. Record Position creates commands that still require Save Sequence. See [URDF Scene Setup](UrdfFraming.md) and [Save, export and reuse your work](Saving.md).
