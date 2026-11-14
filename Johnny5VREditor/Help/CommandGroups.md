# Command Groups and Timing

## Persistent command groups

Select timeline command triangles, right-click, and choose **Group Commands**.
Each new group receives a sequential number (Group 1, Group 2, …) and its own
triangle color, and is deselected. Selecting or dragging any member
selects the whole group, so its commands move and delete together. Right-click
a grouped triangle and choose **Ungroup Commands** to restore independent
movement and normal colors. Grouping and ungrouping support Undo/Redo and survive
saving and reopening the sequence. Pasted and repeated groups are independent
of their source group. Each triangle represents all commands at its timestamp.

**Hide Group** temporarily hides a group's command markers, spline points/segments
and audio handles. All commands still play, including servo, spline, RGB and audio.
A matching colored **Show Group #** button appears on the right of the Sequence
playback bar. Click it to restore the group. Hiding does not delete commands or
save the visibility state to the sequence file. This differs from commands archived by audio Skip to/End at trims: those are retained with the audio handle and do not play until restored. See [Recording Audio](RecordingAudio.md).

## Proportional Offset

Select commands, right-click, and choose **Proportional Offset**. Enter a positive multiplier: 2 doubles the spacing and 0.5 halves it. The leftmost selected command stays fixed; the remaining selected times move relative to that anchor. Use Undo to restore the original timing. Review conflicts if moved commands land on existing commands.

## Work by control type

Right-click a selection and choose **Show all modified controls**. **Delete from Selection** removes instances of that exact control in the selection. For splined controls, **Set Breaks** marks selected commands and **Clear Breaks** removes their breaks; the latter appears when relevant breaks exist. Commands outside the selection remain unchanged.

## Named groups and independent timelines

Open **Edit > Named groups / timelines** to name groups, review command spans and affected controls, duplicate at the cursor, offset timing, hide command visuals, or show/hide each group’s own timeline strip. Dragging a group strip moves only that group. Names and strip visibility are saved with sequences and support undo/redo. Hiding is display-only and does not mute playback. **View** provides floating Control, Xbox, Steam Controller and Stream Deck control windows; closing a floating window docks it again.
