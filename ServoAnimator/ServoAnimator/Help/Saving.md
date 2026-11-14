# Save, export and reuse your work

Use the save action that matches what you want to keep. Saving one item does not save every open document or setting.

## Editable animation files

**File > Save Sequence** stores authored commands, spline settings, audio references and the sequence description. It retains spline control points rather than adding sampled points. Save As chooses another file under Configuration.

**File > Save Movie** stores sequence order, loops, triggers and the movie description. Save edits to existing Sequence files separately. For a new Movie sequence draft, Save Movie can first save the draft and append its block.

**Ctrl+S** saves the active Sequence or Movie. Use the explicitly named File menu action if unsure which is active. An asterisk after each document name marks unsaved work.

## Movie and Sequence ZIP packages

Use **File > Export Movie ZIP…** to collect the current Movie, every referenced Sequence, audio, video, LCD/TV pictures, saved scenes and background images into one ZIP. Repeated Sequence blocks share one packaged file. Loops, triggers, descriptions, splines and editable command data are retained, including commands archived by an audio trim.

**File > Export Sequence ZIP…** collects the current Sequence and its dependencies without a Movie. Export saves pending edits to the current Sequence and, for Movie export, the Movie before collecting files. A new document first needs a save location. Missing referenced files stop export; repair those references and try again. The ZIP can be saved outside Configuration.

On the receiving computer, choose **File > Import Movie ZIP…** or **Import Sequence ZIP…** and select a ZIP created by the corresponding export command. Import checks and stages the package, then lists every file under the active Configuration folder. Files that would be overwritten appear first, with existing and incoming modification dates in local time and exact sizes in bytes. Hover over a filename to see its complete path. **Proceed** installs the files and opens the imported Movie or Sequence. **Cancel** leaves the package files uninstalled. The normal unsaved-document prompt may appear before this review.

Import preserves the package's folders under Configuration and incoming modification dates. It rejects corrupt packages and paths outside Configuration. If a destination changes after review, start import again to review the new comparison. If a replacement fails, import attempts to restore all files it already replaced; if restoration is blocked, the error gives the backup folder location.

## Separate URDF model ZIP packages

Movie and Sequence ZIPs assume matching robot and 3D scenery models are already installed. They include model references, not URDF files or meshes. Import matches installed models and adjusts scene references for the receiving computer. If no match is available, import that model first.

**File > Export URDF Model ZIP…** lets you choose a URDF and packages it separately with its meshes, referenced textures and model animation sidecars. **File > Import URDF Model ZIP…** uses the same comparison and Proceed/Cancel review, installing under Configuration. The completion message shows the URDF path to choose in **Config > URDF Scene Setup > Robot > Change Robot Model…**. Importing a model does not select it as a new robot automatically.

Model exports include only referenced visual/collision meshes and textures, relevant tread animation frames, the baked disassemble/reassemble clip when it belongs to that model, and any validated rendering masks with their required validation files. All authored parts are kept, including hidden laser and toolbox geometry. Unreferenced meshes, drawings, documentation, CAD/Blender source files and unused animation frames are excluded, even when stored inside Meshes. Export leaves the original model folder unchanged. Models sharing a folder do not automatically share unrelated robot animations in their ZIPs.

The URDF Model ZIP preserves the model assets needed for disassembly/reassembly, laser movement and TV geometry. The editor supplies the laser bolt visuals and video player. Their timeline commands, firing sounds and TV video/image files belong to the Movie or Sequence ZIP. To transfer a complete show to a computer without those models, import its robot and any separate scenery/TV model ZIPs first, then import the Movie or Sequence ZIP. Older model ZIPs remain importable with their original contents shown in the review.

## Hardware export

**File > Export Animation JSON…** writes physical commands plus samples from enabled splines. The selected spline sample frequency controls exported sampling; it does not set live preview speed. Export resolves nod and tilt into physical piston commands and excludes virtual body, placement, camera and scene actions. Beam's optional RGB sparkle is exported as an RGB command.

Generated position samples use N/C to retain the active speed profile. Authored speed changes remain in the export. Export does not replace the editable Sequence or add samples to its timeline. Keep the Sequence JSON as the editing master.

## Scenes, backgrounds and timeline placement

**Apply** in Config > URDF Scene Setup keeps the current complete setup as editor defaults without creating or changing a saved scene. Meeting can inherit it with **Use current editor setup**.

**Save Scene** in Config > URDF Scene Setup stores the robot model reference, background, placement and camera together. Save Scene As creates another scene. Referenced model and mesh folders are not copied.

**Save Background Only…** saves scenery; loading it preserves the robot and its framing.

**Animation > Save Scene and Assign to Sequence** saves the edited scene and assigns it to that Sequence. Save Sequence afterward. Animation Library > Sequence Scene can assign an existing scene.

**Animation > Record Position** creates numeric placement and camera commands at the cursor, including orbit and applicable 3D stage channels. Recording again at the same timestamp updates those channels. Smooth movement between recorded positions enables interpolation for the saved channels. Save Sequence to retain the commands; this action does not replace Save Scene.

## Reusable poses and fragments

**Create Library Pose** in Edit Commands saves that window's command rows. **Library +** in URDF Pose mode saves the complete draft. Library Load changes a draft; Insert Library Pose places commands on the timeline.

**Animation Library > Create Library Sequence…** saves a reusable interval with relative timing. See [Animation Library](AnimationLibrary.md).

## Recovery and hidden commands

Recovery snapshots retain unsaved work after interruptions without overwriting normal files. Save Sequence and Save Movie remain necessary. See [Files, Configuration, and Open Recent](FilesConfiguration.md).

Hide Group changes only the current display and is not saved as a mute setting. Its commands still play and export. Commands archived by an audio trim are different: they remain stored with the audio handle and do not play until restored. See [Recording, trimming and converting audio](RecordingAudio.md).
