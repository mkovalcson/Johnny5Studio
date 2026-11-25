# Build a scene and animate the robot

Stop timeline playback completely before opening Help.

## Load and frame the full-body robot

1. Open Config > URDF Scene Setup > Robot and use Change Robot Model… to choose and load the full-body URDF.
2. Adjust framing in Camera and room placement in Robot Position. Apply keeps the setup without saving a scene; Save Scene stores a reusable setup.
3. Open URDF Configuration > Body. Green icons and paired Left/Right controls identify the joints; use the circular range editors and preview sliders.
4. Add B_ commands on the timeline. Use negative finger curl to open the nearly closed neutral hand; the closing-plane stop constrains combined bends.
5. Use Animation > Record Position if the placement itself should become timeline commands. Save Sequence separately to keep those commands.

Expected result: body and framing commands animate only the URDF. Model settings and sequence commands are saved separately.

## Drive through a 3D stage

1. In Config > URDF Scene Setup > Background, choose 3D URDF, choose the scenery file, and check placement on Robot Position and Camera. Save Scene.
2. In Edit Commands add Load Scene, use Choose Scene… to select that saved scene, and apply. Robot Centric keeps the robot framed; Room Centric uses room perspective.
3. Add Track Forward 100 at the start and Track Forward 0 two seconds later.
4. Add URDF Spin 0 at the start and URDF Spin 90 at the end; enable Spline on Spin for a moving turn.
5. Preview, then save. Use negative Track Forward to reverse.

Expected result: the robot travels relative to its front. Treads and rear casters animate on a compatible model. Robot Centric moves the scenery around the robot; Room Centric changes apparent size with distance. Scenery does not provide wall collision avoidance.

## Switch camera shots during playback

Use [Camera Shots](CameraShots.md) to cut or blend between a wide view, a tight shot of Johnny's head and shoulders, and a view of his hands. Add **Standard view** to return to the scene camera.

## Present TV media or collapse the robot

For TV, load compatible scenery, then insert URDF TV Front, TV Show and URDF TV Return. Use each Build… window to set camera travel and media options. Preview the whole segment, including the return.

For collapse, add Disassemble with a compatible full-body model. Build… selects forward collapse or reverse reassembly and effect speed. Add a later reverse action to reassemble. See the dedicated effect topics for asset requirements and playback behavior.

For saving and export choices, see [Save, export and reuse your work](Saving.md).
