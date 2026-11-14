# Commands and Edit Commands

## Edit Commands

Double-click a command in Commands at Cursor, a waveform command triangle, or a selected Spline control point to edit the commands at that time point. A command can contain:

- Offset
- Servo or individual child control
- Spline checkbox (to the left of the Control or Action picklist)
- Break Spline checkbox (available when Spline is checked for a numeric group command)
- Value
- Disable state
- Speed

The Reason field is no longer shown in this editor. Existing Reason metadata remains preserved in saved files.

Changes are staged while the editor is open. **Apply and Close** commits them;
**Cancel** discards the drafts. Saving or closing the application also asks the
editor to apply its pending changes. Servo and URDF previews still respond while editing.

Spline is a sequence-wide setting, not a property of one command. All rows for the same servo show the same state. Neck Nod and Neck Tilt have independent spline settings and combine through the shared neck piston mixer. Spline changes are saved with the sequence and participate in Undo/Redo. The checkbox is disabled for RGB/audio and individual child-only targets, which the current spline engine does not interpolate.

**Break Spline** belongs to the individual command. It stops interpolation and hides the outgoing line to
the next point on that control's spline. The starting value holds until the next command's timestamp; both command dots remain visible and
editable. Later segments interpolate normally unless their starting command also has
Break Spline checked. Neck Nod and Neck Tilt are separate curves, each with its
own breaks. A break on the final point takes effect if another point is added after it.
The hold applies to URDF playback, live hardware playback and sampled hardware exports. Physical speed/acceleration limits still apply when the next command takes effect.
The flag is saved with the command and retained by copy/paste, Library operations,
and Undo/Redo. Uncheck it to restore the line and interpolation.

The Command List also has **Spline** and **Break Spline** checkboxes immediately
after each control name. Spline affects the whole control's sequence; Break
Spline affects just that command. Right-click a spline control point without
dragging to toggle **Break Spline**. Right-drag still moves the point in time.

For selected command triangles, choose **Show all modified controls**. Splined
controls offer **Set Breaks**, plus **Clear Breaks** when any matching selected
command has a break. These buttons affect only commands of that exact control
type at the selected times. Each bulk change is one undoable edit.

**Insert Pose** asks **Break Preceding Splines?**. Yes marks the last enabled
point strictly before insertion on every active spline, even if the new pose
does not contain that control. Disabled and individual child-only commands are
skipped. Neck Nod and Neck Tilt each have their own preceding point. No leaves existing breaks
unchanged. The pose and preceding breaks undo together.

## Redundant commands

Only one command for the same **Servo + individual Control** may be kept at a
given time point (rounded to the nearest millisecond). Different servos and
different child controls may share a time point; a ganged command and an
individual child override are distinct targets.

If an insertion, paste, pose, edit, time move, or loaded sequence contains
duplicates, a warning lists each conflicting group. Candidates appear in the
order they were added, earliest first and most recent last, with their values,
speed, Disable state, color and Reason. Identical values are also duplicates.

Select one command per group and click **Keep selected**. The other commands in
those groups are removed; unrelated commands are retained. **Cancel** cancels
the pending insertion/move/load, or returns to the command editor without
applying its drafts. Undo restores the state before an insertion or edit,
including the commands replaced by your selections.

Resolving conflicts in an existing sequence does not overwrite its file;
save the sequence to retain your choices. Export checks its generated command
list too, including conflicts introduced by expanding ganged commands.

## Command Speed

Speed defaults to **N/C** meaning No Change. An N/C position command does not send Maestro Speed or Acceleration; it only sends the target position and leaves the channel's active speed/acceleration profile unchanged.

An explicit Default, Slow, Fast or Crawl selection sends the matching configured Speed and Acceleration before the target. For ganged commands the profile is sent to every physical child servo in the gang.

At the top of **Edit Commands**, choose **Speed for all commands** and click **Set All Speeds** to update every speed-capable command currently in that window. Choices are N/C, Default, Fast, Slow and Crawl. Individual speeds can still be adjusted afterward. Apply and Close saves the draft changes; Cancel discards them. Like individual speed edits, explicit profiles can preview immediately when Drive HW is enabled. RGB, audio and Tic eye-pop commands are skipped.

Use Insert Pose from the timeline menu to capture a pose arranged in the URDF pose editor.

## Individual child commands

A command may target a child control inside a gang. It then affects only that child until a later ganged command supplies a new gang value.

## RGB commands

RGBCommand uses text rather than a numeric servo value. The RGB Builder can create supported Arduino command strings such as `ClearAll`, `SetRGBColor`, fades, pulses, theater chase, Cylon and rainbow effects. The Edit Commands list no longer uses a separate color patch; the URDF viewer shows the actual four 16-LED eye/vent ring state.

## URDF-only position commands

Edit Commands includes URDF Spin (degrees around the neck), URDF Rotate (degrees of front-view roll, positive clockwise), URDF Slide (horizontal viewport percentage), URDF Vertical (vertical viewport percentage, positive down), and URDF Zoom (camera zoom percentage). Spin supports cumulative whole-degree values for multiple turns; Rotate ranges from −360° to +360°; Zoom starts at 10%. These affect only the URDF view. They have no servo Speed or Disable setting and are not sent to physical hardware or included in hardware animation exports. New Rotate commands include units=degrees in their JSON; older percentage commands are converted on load.

Without Spline they change immediately at the command time. Enable Spline for each desired command type to interpolate between positions using the existing timeline curves. **Config > URDF Scene Setup > Record Position** writes the current placement and camera channels at the cursor as one undoable edit. This includes the five channels above plus camera orbit yaw/pitch and applicable 3D stage channels. Saving again at that timestamp updates those channels; it does not open the duplicate-command chooser. Save the sequence to keep the commands.

## Create Library Pose

The **Create Library Pose** button in Edit Commands saves the command rows currently shown as a reusable single-time-point command group. You provide a JSON file name and description. The file is stored in `Library\Commands`. When inserted later with **Insert Library Pose**, all saved commands are placed at the selected timeline time.

## Command List and selected-control actions

The heading is “# Command(s) at time sec” at the right of the command-list area. The time column has been removed; Speed precedes Value. Spline and Break Spline appear after the control name. New command insertion remembers the last control added. Edit Commands also offers a bulk speed setting for its commands.

Select timeline triangles, right-click and choose Show all modified controls. Every listed control has Delete from Selection immediately before the spline-break actions. It removes all commands of that exact parent/child type at the selected marker times, including nonsplined commands. It does not delete other types or matching commands outside the selection. The operation is undoable. Clear Breaks appears when matching splined commands have breaks; Set Breaks applies to eligible splined commands. Parent and explicit child rows are distinct command types.

## Body commands

The Control or Action picklist ends with the B_ body controls. They animate the full-body URDF with values −100 to +100 (0 is the as-built pose), using the travel configured under URDF Configuration > Body. Spline interpolation, grouping and normal timeline edits apply. They remain in editable sequence files and are omitted from physical output and hardware exports. See [URDF body controls](UrdfBody.md) for the joint list. The two neck-lift follower joints move automatically.

## Groups and proportional timing

See **Command Groups and Timing** for numbered colored groups, Hide/Show Group, Proportional Offset, and operations on selected control types.

## Specialized URDF actions

Use **Choose Scene…** for Load Scene to select a saved complete scene file. Use **Build…** for Disassemble, URDF TV Front, TV Show, URDF TV Return, URDF Beam, LCD Show, Hide Laser, Toolbox Swap and Fire Laser. Their dedicated help topics explain camera travel, media files and effect timing. These red-icon actions affect only the virtual scene. Body controls have green icons and native -100 to +100 values; their configured degree ranges are in URDF Configuration.
