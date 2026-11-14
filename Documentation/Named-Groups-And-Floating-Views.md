# Named groups, group timelines and floating views

Available in J5 Studio Desktop and J5 Studio VR Editor. The implementation is shared in `Shared/EditorUI` and group data is shared in `Shared/Animation`.

## Named groups

Open **Edit > Named groups / timelines** or **View > Named groups / timelines**.

Select command triangles and choose **Group selected commands**, or right-click selected markers and choose **Group Commands**. A new group receives its own timeline; library sequence insertion also creates a named group using the library sequence name. Existing numbered groups remain compatible and can be renamed in the manager.

Each manager entry shows its name, first and last command times, duration between those commands, command count, affected sections, and affected controls. Rename, Select, Show/Hide commands, Duplicate at cursor, Offset and Ungroup operate on that group. Negative offsets move earlier, stopping at time zero. Duplicate retains relative timing and creates a separate group and independent audio-take identities. Command conflicts still use the existing review dialog.

Names and the set of visible group timelines are saved with the sequence. They are also included in undo/redo and recovery snapshots. Older sequence files need no conversion.

## Group timelines

Check **Own timeline** to show a group's strip below the main timeline; use its down triangle to hide it. The manager can reopen any strip or show them all. Many rows scroll vertically so the main timeline stays available.

All strips use the main timeline's zoom, scroll and cursor. The colored bar spans the group's commands and vertical marks show their times. Click its name to select the group. Drag the bar/marks to move the whole group; groups at matching timestamps remain independent. Right-click a strip for group actions. Mouse wheel changes the shared zoom.

**Hide timeline** only removes that strip. **Hide commands** hides that group's command markers, spline points and clip handles; an enabled strip remains visible but dimmed. Neither action mutes playback or deletes commands. Non-destructive mute/solo layers remain a future feature.

## Floating views

Use **View > Float / dock Control View**, **Xbox Controller View**, **Steam Controller View**, or **Stream Deck controls**. The Control View also has a small arrow button. Floating windows can be moved and resized; **Dock back** or closing a floating window returns its controls to the editor. **View > Dock all floating views** also docks the URDF; **Config > Reset Screen Layout** restores the docked workspace.

Xbox and Steam Controller windows show live controller mappings independently. Stream Deck has connection and enable controls in the editor; those existing controls can float. The Stream Deck key layout remains in the separate Stream Deck application.

When the Control View and URDF are floating and no mapping diagram remains docked, the empty upper editing row collapses and its height becomes available to timelines. Hardware remains accessible at the right. Floating view choices last for the current session; existing URDF layout persistence is unchanged.

## Validation

Both editors are compiled and checked with focused group save/clone/undo, overlapping-time group selection/movement, and floating panel/docking checks. Full regression testing and headset testing are not run for this update.
