# Camera Shots

Add a **Camera Shot** command on the **URDF/Scene** timeline where you want the framing to change. In **Edit Commands**, click **Build…** beside the command.

| Shot | Framing |
| --- | --- |
| **Tight** | Faces Johnny square to his shoulders and follows his head and upper shoulders, with room for neck lift and open flaps. |
| **Wide** | Fits the full robot and keeps the camera square to the stage's back wall. **Stage Front offset** adjusts the direction from the detected wall, or from the stage's +X direction when there is no named back wall. |
| **Hand · Left** or **Hand · Right** | Looks toward the arms from the selected side and leaves space in front of the hands. |
| **Standard view** | Returns to the usual scene camera. |

Choose **Cut** for an instant switch, or **Blend** and enter **Blend seconds**. A blend defaults to two seconds. Apply the command and play the sequence through the cue. Add another **Camera Shot** at a later time to change framing again, and save the sequence to keep the cues.

In Desktop, the shot drives the URDF viewport. In VR, it drives [Clean Output](VR-Clean-Output.md) while your headset view follows your head movement.

See [URDF Scene Setup](UrdfFraming.md) to set the normal scene camera or [Build a scene and animate the robot](SceneGuide.md) for a complete scene workflow.
