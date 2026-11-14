# Spline Editor

The Spline Editor creates smooth interpolated servo motion between control points. The application uses cubic Hermite interpolation for spline-enabled servo motion.

## Mouse controls

The legend uses compact checkbox chips with servo-color dots. Hidden curves have subdued
chips; **show all** remains directly after the final chip. The zero-value line is stronger
than other reference lines. Combined mode softens the waveform behind the splines.

- Left-drag a control point vertically to change its value.
- Right-drag a point horizontally to change its time.
- Right-click the vertical sequence cursor to open the same cursor actions as the waveform.
- Ctrl+left-click **or double-left-click** a spline line to add a control point.
- Select a point and press Delete to remove it.
- Double-left-click a selected control point to open Edit Commands at that point's time. Double-clicking an empty part of a curve still adds a new point.
- Hold the middle mouse button and drag anywhere in the graph, including over a control point, to pan the timeline.
- Hover over a point to see the exact servo, time and value just to the right of the mouse pointer. The readout follows the pointer while editing points.
- Selecting a control point also moves the sequence cursor to that point and refreshes the Commands list, showing every command at that exact cursor time.

The spline view shares timeline zoom and pan with the waveform so the same time range remains aligned vertically.

Mouse-wheel zoom in the Spline graph expands around the Sequence cursor, not the mouse pointer. The cursor stays in the same screen position unless a timeline boundary limits scrolling.

The colored show/hide checkbox legend sits immediately above the graph, leaving the curves unobstructed.

If any legend checkbox is unchecked, a small **show all** button appears beside the legend. Click it to check every legend checkbox and restore all enabled Spline lines. It changes graph visibility, not which servos have interpolation enabled.

Enable or disable a servo's spline using the checkbox to the left of its Control or Action picklist in Edit Commands. This changes interpolation for that servo throughout the sequence; the legend only changes graph visibility.

Horizontal reference lines mark -100, -75, -50, -25, 0, +25, +50, +75 and +100 for typical centered servos. Other servo ranges remain normalized to the graph height; hover a point for its exact value. At small graph heights, some labels are omitted to avoid overlap, but every grid line remains.

## Neck nod and tilt

`NeckNodUp` and `NeckTiltRight` are complementary: both apply at the same time, each with its own commands, spline checkbox and curve. They always resolve into the two neck pistons (`NeckTiltLeft` and `NeckTiltRight`): nod moves both pistons together and tilt moves them oppositely, so each piston gets nod plus or minus tilt. Each piston is then limited to its own calibrated travel. Nod therefore keeps its full range and tilt uses whatever travel is left: with the head nodded fully up, a tilt can only lower one piston (and fully down, only raise one), giving less tilt and dropping that side slightly.

The same resolution is used for hardware output, the URDF preview, calibrated-speed motion, face tracking, controller output and exports (exports write the two pistons). A command for a single piston (`NeckNodUp:NeckTiltLeft`, for example) sets that piston directly. In timeline playback, a child command newer than both neck parents overrides its piston until the next nod or tilt parent command. That parent retakes the pair using the authored nod and tilt values. Live face/controller input re-derives nod and tilt from direct piston changes before subsequent live parent input.

## Playback

During playback the spline is evaluated at the current timeline time and the resulting values are used for the URDF preview and enabled hardware output. High-frequency spline-generated movement does not repeatedly send Maestro Speed/Acceleration because those generated position updates use N/C speed behavior.

## Break Spline and selection tools

Break Spline stops interpolation from a command to the next point for that control and hides that drawn segment. The control holds the starting command's value until the next command's timestamp; that command then takes effect and normal interpolation resumes unless it also has a break. This applies to the URDF model, live hardware playback, recording replay and sampled hardware exports. Physical speed/acceleration limits still govern movement to a commanded value. Toggle it in Edit Commands, the Command List, or a selected spline point's right-click menu. Set Breaks/Clear Breaks in Show all modified controls apply to the selected commands of that control type.

Pose and Library insertion can mark preceding spline commands automatically through Break Preceding Splines, holding those controls until their next command. Spline legend checkboxes only change graph visibility; they do not change playback.

## URDF camera and placement curves

Numeric scene channels support the same spline interpolation and point editing as other controls. URDF presentation curves use dotted lines. Their native ranges are normalized to the −100…+100 graph scale; hover to read the real value. Use Scene Setup > Record Position at two different times with the spline option enabled to animate a camera/placement transition. Head & Audio / Body / Scene layout separates head/audio, body, and URDF curves while sharing one cursor and horizontal time scale.
