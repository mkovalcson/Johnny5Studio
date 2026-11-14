# Create and edit an animation

Stop timeline playback completely before opening Help.

## Create a first sequence

1. Choose File > New, or New Sequence in the Start a sequence popup. Name/save the sequence under the active Configuration folder; choose that folder through Config > Set Paths if needed.
2. Optionally open a primary audio file.
3. Place the cursor at 0 seconds, choose Add, and set Control or Action to NeckTurn and Value to -20. Leave Speed at N/C.
4. Apply and close. Add NeckTurn 20 at 1 second and NeckTurn 0 at 2 seconds.
5. Move the cursor back to 0, play with URDF Drive enabled, then Save Sequence. Sequence playback starts from the cursor.

Expected result: three editable commands move the head left, right and back. N/C retains the current speed profile; it does not mean unlimited speed.

## Smooth motion and break a spline

1. Open a NeckTurn command and enable Spline for that control.
2. Apply, move the cursor to the first point, and play the sequence. The control interpolates between its points.
3. Check Break Spline on a command to hold its value until the following point and hide that connection. You can also toggle it from a selected spline point's right-click menu.
4. For multiple commands, select their triangles, show all modified controls, and use Set Breaks or Clear Breaks on the matching row.

Expected result: each broken interval holds its starting value until the next command, then smooth motion resumes. The hold applies to URDF playback, live hardware playback and sampled hardware exports. Spline visibility checkboxes only hide/show the graph; they do not turn interpolation off.

## Group and retime a gesture

1. Select the gesture's triangles and right-click > Group Commands.
2. The new numbered group gets a distinct color. Move or delete it together.
3. Select the commands and choose Proportional Offset. Use 2 for twice the spacing, or 0.5 for half; the leftmost time remains fixed.
4. Choose Hide Group to hide it temporarily from the timeline while its commands continue playing. Its colored Show Group button restores its visibility.
5. Undo if needed, then save the sequence.

Expected result: timing changes without editing every timestamp. Hidden commands remain in the sequence and still play; the temporary visibility choice is not saved.

## Insert a pose or Library sequence

1. Arrange a pose in URDF Pose mode; expand Full Body Pose when a supported full-body model is loaded.
2. Insert Pose at the cursor. Choose whether to break preceding splines.
3. Save reusable poses through Library +; Manage Library Poses lets you choose a pose image.
4. To reuse one, right-click the timeline and select Insert Library Pose or Insert Library Sequence.
5. Use the corresponding Insert …, Break Preceding Splines button when the incoming pose should not be approached by the previous spline.

Expected result: inserted commands remain editable. Loading a Library pose into Pose mode changes the draft; inserting it creates timeline commands.

For saving and export choices, see [Save, export and reuse your work](Saving.md).
