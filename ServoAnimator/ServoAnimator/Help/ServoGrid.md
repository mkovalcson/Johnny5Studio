# Commands pane and Pose editing

Use the Commands pane for timed commands, or URDF Pose mode for a complete visual draft.

## Edit a timed command

1. Click the timeline to choose its time.
2. Choose **+ Add**, or select an existing command and choose **Edit**.
3. Select **Control or Action** and set value, speed and Disable as appropriate. A parent drives its gang; a named child targets one actuator.
4. Choose Spline for sequence-wide interpolation, or Break Spline to hold this point until the next point.
5. Choose **Apply and Close** to keep the draft or **Cancel** to discard it.

See [Commands and Edit Commands](Commands.md) for native ranges and speed behavior.

## Arrange and insert a pose

1. Enable **Pose** in the URDF view.
2. Use LR Joined for paired head controls or LR Split for independent sides. Supported models also expose Full Body Pose.
3. Adjust the pose. Neck nod and tilt combine; neither clears the other.
4. Right-click the Sequence timeline and choose **Insert Pose** at the intended cursor time.
5. Choose whether to break preceding splines, then save the Sequence.

Leaving Pose without insertion restores the timeline pose. Library + saves the draft as a reusable pose; Library Load changes the draft without inserting commands. See [URDF 3D Viewer](UrdfViewer.md).

The former Servo Grid and Generate commands from grid values workflow have been replaced by these controls.

Preview edits can move connected hardware when Drive HW is enabled. Numeric values use native editor units, not physical degrees.
