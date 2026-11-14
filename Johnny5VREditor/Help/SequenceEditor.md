# Sequence Editor and Audio Timeline

The Sequence header shows the filename without its `.json` extension, an unsaved `*` directly after the name when modified, and the complete Sequence duration. The description below it is read-only; click the small bold **edit** link immediately after the text to change it in a popup editor. The same link is available beside Sequence descriptions inside Movie blocks.

A Sequence is an animation document containing timed servo, RGB and audio-play commands plus sequence metadata.

**File > Export Sequence ZIP…** packages the current editable Sequence with its audio, other media and scene dependencies. **Import Sequence ZIP…** restores it under Configuration after reviewing existing/incoming modification dates and file sizes and choosing Proceed or Cancel. URDF models are transferred separately. See [Save, export and reuse your work](Saving.md).

## Timeline navigation

Use the compact, single-line picklist immediately left of **?** to choose **Combined** (the default), **Head & Audio / Body / Scene**, **Head & Audio only**, **Body only**, or **Scene only**. The choice is remembered with your editor layout. Older Waveform top / Spline top preferences open as Combined.

The individual-section modes use the full timeline height for that section's markers,
spline curves and legend. Audio and its handles are shown in Head & Audio; video bars
are shown in Scene. Hidden sections are not available for direct marker/spline editing
in that view. This changes only the editing view: all commands and media still play.

**Combined** overlays spline curves and value-grid lines on the waveform in one shared timeline. The Spline checkbox legend sits above the command triangles, and all triangles sit above the +100 grid line. Spline points remain editable; blank waveform space retains its normal cursor, marker-selection, zoom and context-menu behavior. The **show all** button follows the final legend item and wraps with the legend when needed.

Playback cursors are lightweight overlays updated on every display frame. Timeline pose/slider refreshes target 30 updates per second; calibrated model motion and controller visuals run at display cadence, up to 60 per second. Command/audio timing still uses the playback clock.

**▶ Sequence** starts from the current cursor; move the cursor to zero to play from the beginning. Starting or resuming playback initializes the URDF and enabled hardware to the evaluated pose at the cursor, including spline values and active speed profiles. The URDF snaps to that pose; physical actuators travel to it under their configured speed and acceleration limits. Hidden command groups continue playing.

- Left-click positions the cursor.
- Double-left-click a command triangle to open Edit Commands for its time group.
- Mouse wheel and Zoom +/- expand or contract around the Sequence cursor, keeping it at the same screen position while timeline bounds allow. The mouse pointer's position does not change the zoom anchor.
- Middle-drag pans the visible time range.
- Right-click opens timeline actions.
- Drag a command triangle horizontally to move its complete command group.

Command triangles that overlap are vertically staggered for visibility.

## Head & Audio / Body / Scene layout

Three equal-height timeline lanes share the same cursor, zoom, pan and playback:

A narrow left-hand label strip stacks **Head**, **Neck** and **Audio** on separate
lines, followed by **Body**, then **URDF** and **Scene**. The label cells use pale
blue, green and red backgrounds with dark contrasting text. It appears only when all three
sections are shown together and leaves the markers and curves unobstructed.

- Top: head and neck commands, LED/audio commands and audio waveform/handles.
- Middle: Body (`B_`) commands.
- Bottom: URDF presentation, stage movement, camera and effect commands.

Each lane has command triangles and its enabled spline curves. URDF curves are dotted. Click a lane to work with its commands; Ctrl/Shift selection and dragging stay within that lane. An explicit saved command group still moves or deletes together, including its members in another lane. Changing lanes clears the temporary triangle selection. The spline legend above the timelines controls curve visibility in all three lanes.

## Selecting and editing command groups

Ctrl-left-click a triangle to select or deselect all commands at that timestamp. Shift-left-click selects every triangle between the selection anchor and the triangle nearest the click, including both endpoints. Selected triangles have a cyan outline.

Drag any selected triangle to move the entire selection together, preserving its internal timing. The group stays selected when you click elsewhere in the waveform; a mouse-button click outside the waveform clears it. Double-click remains available to edit the commands at one triangle.

Right-click while a group is selected for these actions:

- **Proportional Offset** keeps the leftmost selected time fixed and multiplies each other selected time’s distance from it. Enter a positive multiplier: **2.0** doubles the spacing, **0.5** halves it, and **1.0** leaves it unchanged. Commands at the same time stay together; unselected commands stay in place.
- **Uniform offset** shows the mean spacing between selected triangles. Apply the displayed value, enter a nonnegative decimal number of seconds, or leave the field blank to use the mean. The first selected time remains fixed and the remaining times stay in order, evenly spaced.
- **Repeat** asks for the number of additional copies and an offset, defaulting to zero. The first copy starts at the original group's last command plus the offset. Each later copy starts at the preceding copy's last command plus the offset; internal timing is preserved.
- **Copy** remembers the selected commands and their relative timing. Left-click the desired start position in the waveform, then right-click and choose **Paste**. Paste is available in both the selected-group menu and the normal cursor menu.
- **Delete** removes every command at the selected timestamps.
- **Show all modified controls** temporarily replaces Commands at Cursor with a distinct list of controls used by the selected triangles. Select a list entry to highlight every triangle containing that control in purple, including matches outside the selection. A ganged command includes its individual children. Selecting entries in this list keeps the triangle selection active; clicking elsewhere outside the waveform ends the selection and restores the normal Commands list.

Group moves, Proportional Offset, Uniform offset, Repeat, Paste and Delete can each be undone as one operation. Times use millisecond precision. If a move, zero spacing, repetition or paste creates redundant commands, choose which values to keep in the existing warning; Cancel restores the state before that operation. To return to the normal cursor menu, clear the selection by clicking outside the waveform.

The waveform and Spline share a permanent left inset. At the start of the view, time zero and its command triangle sit just to the right of the green audio grab handle. The inset remains even when the handle is outside the visible area. Zoom and Fit buttons are to the left of the Sequence playback buttons.

Fit still shows the complete Sequence. Zooming out against the start/end of the timeline may move the cursor on screen because scrolling remains within timeline bounds.

## Audio

The primary audio waveform is shown on the timeline. Additional audio clips are represented by `Play` commands and are included in playback. Playback follows the timeline clock and switches audio sources as clip boundaries are crossed.

The Playback Volume control changes audible playback volume. URDF mouth-light response uses the audio amplitude plus the Audio LED Gain configured in URDF Configuration.

Video clips are violet bars just above the time axis (in the Scene section of the three-lane layout). Their sound plays alongside the timeline audio and never moves the mouth. See [Video Clips behind the Robot](VideoClips.md).

## Command markers

A triangle represents the commands at a timeline point. During collision-diagnostic playback a triangle can become bright red when the calibrated URDF pose produced at that point causes one of the configured flap collisions.

Editing commands clears old red collision-marker state. Replay the modified sequence to calculate fresh collision warnings.

## Command and pose drafting

Use Edit Commands to stage servo values and Spline settings, then Apply and Close to commit them. Use the URDF pose editor and Insert Pose to capture a complete pose.

## Insert Pose

When the URDF Pose editor is used to arrange the robot visually, right-click the sequence timeline and choose **Insert Pose**. The editor creates a complete mechanical keyframe at the current cursor from the URDF pose. If existing commands target the same servo/control at that time, a warning shows the earlier and new values so you can choose which to keep. An RGB Command from the pose is compared the same way; unrelated RGB/Play commands remain.

## Starting, recording and meeting

The Start a sequence panel offers New Sequence…, Open Sequence… and Insert Audio…. New Sequence prompts for a name and save location and writes normal metadata with an empty commands list; it does not insert an initial pose.

Insert Audio… (and File → Open Audio… and Insert audio on the timeline) accepts a file from anywhere. A file outside the Configuration folder is copied into `Library/Audio` (your original stays where it is) and the sequence uses that copy; a file already inside Configuration is used where it is. If `Library/Audio` already has a different file with the same name, the copy is named `name-2`, `name-3` and so on; an identical file already there is reused.

Record opens Record from Controller / Webcam for movement capture, optional mono microphone WAV recording, overdubbing and take review. Meeting, immediately to its right, opens microphone-driven live URDF puppeteering without recording. See their dedicated help topics for setup and source ownership.

Playback Speed ranges from 10% to 200%, default 100%. Its value is displayed as a bright percentage. Recorded audio takes temporarily use 100% until the recording window closes. Playback Volume changes listening volume, not recorded microphone gain. Status messages appear in the right half of the Movie controls area.
