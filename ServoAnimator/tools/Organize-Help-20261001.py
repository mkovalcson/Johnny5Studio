from pathlib import Path
import json,re
r=Path('ServoAnimator');h=r/'Help'
text='''# Step-by-Step Tutorials

Choose a task below. Start with tutorials 1 and 2 if you are new to the editor. Stop playback fully before opening F1 Help; keep physical Live Drive off while learning. See the related reference topics for every field and exception.

## 1. Create a first sequence

1. Choose File > New, or New Sequence in the Start a sequence popup. Name/save the sequence under the active Configuration folder.
2. Optionally open a primary audio file.
3. Place the cursor at 0 seconds, choose Add, and set Control or Action to NeckTurn and Value to -20. Leave Speed at N/C.
4. Apply and close. Add NeckTurn 20 at 1 second and NeckTurn 0 at 2 seconds.
5. Play with URDF Drive enabled, then Save Sequence.

Expected result: three editable commands move the head left, right and back. N/C retains the current speed profile; it does not mean unlimited speed.

## 2. Smooth motion and break a spline

1. Open a NeckTurn command and enable Spline for that control.
2. Apply and play the sequence. The control interpolates between its points.
3. Check Break Spline on a command to end the line/motion connection to its following point. You can also toggle it from a selected spline point's right-click menu.
4. For multiple commands, select their triangles, show all modified controls, and use Set Breaks or Clear Breaks on the matching row.

Expected result: smooth segments and intentional breaks remain editable. Spline visibility checkboxes only hide/show the graph; they do not turn interpolation off.

## 3. Group and retime a gesture

1. Select the gesture's triangles and right-click > Group Commands.
2. The new numbered group gets a distinct color. Move or delete it together.
3. Select the commands and choose Proportional Offset. Use 2 for twice the spacing, or 0.5 for half; the leftmost time remains fixed.
4. Choose Hide Group to exclude it temporarily from display/playback. Its colored Show Group button restores it.
5. Undo if needed, then save the sequence.

Expected result: timing changes without editing every timestamp. Hidden groups remain saved and recoverable.

## 4. Insert a pose or Library sequence

1. Arrange a pose in URDF Pose mode; expand Full Body Pose when a supported full-body model is loaded.
2. Insert Pose at the cursor. Choose whether to break preceding splines.
3. Save reusable poses through Library +; Manage Library Poses lets you choose a pose image.
4. To reuse one, right-click the timeline and select Insert Library Pose or Insert Library Sequence.
5. Use the corresponding Insert …, Break Preceding Splines button when the incoming pose should not be approached by the previous spline.

Expected result: inserted commands remain editable. Loading a Library pose into Pose mode changes the draft; inserting it creates timeline commands.

## 5. Map a controller and try it

1. Open the Xbox or Steam mapping window from Config. Select a MUX layer, then an input on the graphic.
2. Choose Control, Pose or Sequence. For a joystick, choose Relative for jogging or Absolute for position.
3. Set dead zone and joystick Expo. Higher Expo gives gentler movement near the center while retaining full-stick response.
4. For Steam gyro/accelerometer input, optionally require a held trigger button.
5. Save the mapping; it takes effect immediately. Save another mapping file to keep alternative layouts.
6. Enable the controller and use the configuration window's URDF test mode to observe motion and raw/mapped values.

Expected result: green feedback identifies active inputs. With MUX enabled shoulders select four independent layouts; Steam No MUX preserves those layouts and exposes separate shoulder mappings.

## 6. Record a controller or webcam performance

1. Save a sequence and put the cursor where the take should begin.
2. Map a controller button to Start/Stop recording (REC), or use F9 in the editor.
3. Open Record and choose the source. Select the controls to record; Select child controls toggles the child list.
4. Choose overdub if replacing only the selected controls. Leave Group Recorded Commands checked to keep the take together.
5. Press Ready to Record. The window closes. Press REC/F9 to start, perform, then press again to stop.
6. Review smoothing and reduction settings. Replay Recording auditions the take; Keep Take commits it. Discard / close removes the draft.
7. Save the sequence.

Expected result: editable commands, reduced to useful keyframes, without changing unselected controls.

## 7. Combine webcam and controller input

1. Open Webcam Face Tracking, start the camera, Set neutral, and enable the intended signal mappings.
2. Save/apply mappings. For LPiston/RPiston, avoid conflicting parent neck mappings.
3. In Record choose Webcam + X-Box or Webcam + Steam.
4. Use the recording checkboxes to select what is captured. Webcam icons/highlighted labels identify actuator overlap.
5. In the additional left checkbox column, select controls that may be taken over by the controller.
6. Map Face Tracking Override to a controller button. Turn it on during the performance when the controller should own those selected controls.

Expected result: webcam has priority on shared actuators unless the connected, enabled controller's override is on. Recording selection and takeover selection are independent.

## 8. Record speech and convert its voice

1. In Record, check Record Audio and choose a connected microphone.
2. Speak and review the peak meter and clip indicator before arming.
3. Optionally check Automatically Convert Voice when Recording Finishes.
4. Ready the take, start/stop with REC/F9, then Keep Take.
5. Wait for the converted-file-ready status before playing the converted voice. Save the sequence.

Expected result: a numbered mono 44.1 kHz WAV and a Play command at the captured start cursor. Conversion runs after Keep Take; on success the Play reference changes to the _j5 file. Discard removes the draft recording.

## 9. Trim or replace recorded speech

1. Right-click the primary or added audio handle and open Audio timing / Skip to / End at.
2. Enter Skip to in source-audio seconds; optionally scrub commands before it. Commit locks the value; click Commit again to edit.
3. Set End at similarly to shorten the tail. Hidden commands can return when bounds are relaxed; Reset trims restores the original interval.
4. For externally processed speech, choose Special Audio Replace, select the new audio and optional Command Update JSON.
5. Clear active trims before re-timing, review the effects, apply, preview and save.

Expected result: ordinary trims remain reversible; Special Audio Replace can re-time all following commands to match a longer processed track. See its reference page before applying it to a busy sequence.

## 10. Load and frame the full-body robot

1. Open URDF Model and Framing and choose the full-body model folder/file.
2. Save to load it, then adjust horizontal position, vertical position and zoom. Save again to retain the framing.
3. Open URDF Configuration > Body. Green icons and paired Left/Right controls identify the joints; use the circular range editors and preview sliders.
4. Add B_ commands on the timeline. Use negative finger curl to open the nearly closed neutral hand; the closing-plane stop constrains combined bends.
5. Use Save Position in Model and Framing if the placement itself should become timeline commands.

Expected result: body and framing commands animate only the URDF. Model settings and sequence commands are saved separately.

## 11. Drive through a 3D stage

1. Add Load Backdrop and use Build… to choose 3D URDF and the scenery file.
2. Choose Robot Centric or Room Centric. Check floor placement and enable Preserve Robot Framing if desired.
3. Add Track Forward 100 at the start and Track Forward 0 two seconds later.
4. Add Spin 0 at the start and Spin 90 at the end; enable Spline on Spin for a moving turn.
5. Preview, then save. Use negative Track Forward to reverse.

Expected result: the robot travels relative to its front. Treads and rear casters animate on a compatible model. Robot Centric moves the scenery around the robot; Room Centric changes apparent size with distance. Scenery does not provide wall collision avoidance.

## 12. Present TV media or collapse the robot

For TV, load compatible scenery, then insert URDF TV Front, TV Show and URDF TV Return. Use each Build… window to set camera travel and media options. Preview the whole segment, including the return.

For collapse, add Fall Apart with a compatible full-body model. Build… selects forward collapse or reverse reassembly and effect speed. Add a later reverse action to reassemble. See the dedicated effect topics for asset requirements and playback behavior.

## 13. Present through OBS or Teams

1. Prepare webcam/controller mappings and click Meeting beside Record.
2. Choose the source and microphone. Speak while adjusting mouth gain, noise gate and release; watch the URDF mouth preview.
3. Keep the current background or choose a temporary image/moving-background override. Save settings.
4. Start Meeting, capture the clean undocked robot view in OBS, and start OBS Virtual Camera.
5. In Teams select OBS Virtual Camera and your speech microphone separately.
6. Press Stop meeting when finished.

Expected result: live URDF performance without recording a take. The tracker owns the physical webcam, so Teams uses OBS. Meeting does not drive physical hardware.

## 14. Build a movie and run cues

1. Create/open a Movie, then insert Sequence files into the Movie Timeline.
2. Drag blocks to reorder them or use their context-menu operations. Remove a sequence from the movie without deleting its file.
3. Set looping cues where needed and Save Movie. Save any edited Sequence separately.
4. For many numbered audio files, use File > Create Movie from Audio Files and review the generated sequence order.
5. Start the movie. If background arrows are wanted, enable Movie Playback in Focus Control.

Expected result: a saved cue list. Background arrows stay reserved until the movie transport is released; the Focus Control topic explains each key.

## 15. Save and troubleshoot

Use Save Sequence and Save Movie for changed documents; an asterisk marks unsaved work. Keep referenced media under Configuration unless the relevant importer copies it there. Close normally to retain layout. Reset Screen Layout recovers hidden panels.

For a missing model/backdrop after moving folders, browse to the new file. For unexpected vertical placement, review saved model framing and explicit Load Backdrop commands. For slow motion, check calibration and the playback-speed percentage before changing command values.
'''
(h/'StepByStep.md').write_text(text,encoding='utf-8')
idx=json.loads((h/'HelpIndex.json').read_text())
new=[('command-groups','Command Groups and Timing','CommandGroups.md',['group','hide','show group','proportional offset','multiplier','clear breaks','set breaks'],['commands','spline-editor','step-by-step']),('urdf-framing','URDF Model and Framing','UrdfFraming.md',['vertical','horizontal','spin','continuous','zoom','save position','model folder'],['urdf-viewer','urdf-backdrops','urdf-body']),('urdf-tv','URDF TV and Screen Media','UrdfTv.md',['TV','video','TV Front','TV Show','TV Return','screen','playlist'],['urdf-backdrops','commands','step-by-step']),('urdf-effects','Fall Apart and Reassemble','UrdfEffects.md',['fall apart','reassemble','collapse','reverse'],['urdf-body','commands','step-by-step']),('special-audio-replace','Special Audio Replace','SpecialAudioReplace.md',['headroom','stutter','glitch','command update','retime','replace audio'],['recording-audio','commands','step-by-step']),('speed-calibration','Speed Calibration','SpeedCalibration.md',['calibration','speed','acceleration','stepper','Eye Pop','physical seconds'],['servo-configuration','controllers','live-drive-hardware']),('focus-control','Focus Control and Background Input','FocusControl.md',['focus','background input','global arrows','Stream Deck','Escape'],['controllers','movie-timeline','meeting-mode']),('editor-api','Editor API Reference','AnimationEditor_API_For_AI.md',['API','Codex','named pipe','expectedToken','automation'],['commands','urdf-backdrops','animation-library'])]
for ident,title,file,keys,related in new:idx['topics'].append(dict(id=ident,title=title,file=file,keywords=keys,related=related))
lookup={t['id']:t for t in idx['topics']}
for ident,keys,related in [('controllers',['Expo','mapping file','No MUX','Face Tracking Override','REC'],['focus-control','speed-calibration']),('recording-audio',['Skip to','End at','Commit','voice conversion','_j5'],['special-audio-replace']),('urdf-backdrops',['Track Forward','caster','treads','moving background','TV'],['urdf-framing','urdf-tv','urdf-effects']),('urdf-body',['green icons','thumb','closing plane','range dial'],['urdf-framing']),('commands',['Control or Action','Break Spline','icons'],['command-groups','urdf-tv','urdf-effects']),('step-by-step',['TV','stage','recording','overdub','meeting','trim','controller'],['controller-recording','urdf-tv','special-audio-replace','urdf-framing'])]:
 lookup[ident]['keywords']+=keys;lookup[ident]['related']=list(dict.fromkeys(lookup[ident]['related']+related))
lookup['recording-audio']['title']='Recording, Trimming and Converting Audio'
lookup['servo-grid']['title']='Manual Control and Pose Editing'
lookup['urdf-configuration']['related']+=['urdf-body','urdf-framing','urdf-backdrops']
lookup['servo-configuration']['related']+=['speed-calibration']
order=['getting-started','step-by-step','interface','controls-hotkeys','sequence-editor','commands','command-groups','spline-editor','servo-grid','movie-timeline','animation-library','controllers','focus-control','webcam-face-tracking','face-signals','piston-signals','controller-recording','recording-audio','special-audio-replace','meeting-mode','urdf-viewer','urdf-framing','urdf-body','urdf-configuration','urdf-backdrops','urdf-tv','urdf-effects','collision-warning','servo-configuration','speed-calibration','live-drive-hardware','rgb-lighting','files-configuration','editor-api','troubleshooting']
idx['topics']=[lookup[i] for i in order];(h/'HelpIndex.json').write_text(json.dumps(idx,indent=2)+'\n')
# Wire focused help entry points. No functional output/playback changes.
def edit(n,old,new):
 p=r/n;s=p.read_text(encoding='utf-8-sig');assert old in s,n;p.write_text(s.replace(old,new),encoding='utf-8-sig')
edit('FocusControlWindow.cs','Title = "Focus Control";', 'HelpSystem.EnableContextHelp(this, "focus-control");\n        Title = "Focus Control";')
edit('ControllerMappingWindow.cs','Title = profile.Kind == ControllerKind.Xbox', 'HelpSystem.EnableContextHelp(this, "controllers");\n        Title = profile.Kind == ControllerKind.Xbox')
edit('SpeedCalibrationWindow.cs','HelpSystem.EnableContextHelp(this, "servo-configuration")','HelpSystem.EnableContextHelp(this, "speed-calibration")')
edit('SpecialAudioReplaceWindow.cs','HelpSystem.EnableContextHelp(this, "recording-audio")','HelpSystem.EnableContextHelp(this, "special-audio-replace")')
edit('MainWindow.UrdfModel.cs','HelpSystem.EnableContextHelp(dialog, "urdf-viewer")','HelpSystem.EnableContextHelp(dialog, "urdf-framing")')
edit('CommandSettingsWindow.cs','Title = title; Width = 560;', '''HelpSystem.EnableContextHelp(this, title == "Fall Apart" ? "urdf-effects"
            : title.Contains("TV", StringComparison.OrdinalIgnoreCase) ? "urdf-tv" : "commands");
        Title = title; Width = 560;''')
edit('BackdropEditorWindow.cs','var panel = new StackPanel { Margin = new Thickness(12), Background = Brushes.WhiteSmoke };','var panel = new StackPanel { Margin = new Thickness(12), Background = Brushes.WhiteSmoke };\n            HelpSystem.SetTopic(panel, name == "Robot framing" ? "urdf-framing" : "urdf-backdrops");')
