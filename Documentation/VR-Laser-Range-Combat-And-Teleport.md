# Laser Range — combat, instructions, event audio and lookout teleport

## Start and setup

Start SteamVR, tap A, choose **Game · Johnny 5 Laser Range**, then **Start game**. Use the full-body robot with its existing baked FallApart assets. The VR Editor does not need to run. The game preloads the instruction sequence and event audio before beginning.

Set optional media in **Game setup · Sequence and audio** in VR, or **Laser Range setup…** in the Player's desktop sidebar. These choices are saved in **Config/VR/LaserRange.json** and apply on the next Start / Restart:

- Instruction sequence: choose an existing library/project sequence, or None.
- Hit audio: plays when a shot damages Johnny.
- Disassemble audio: starts just before disassembly begins.
- Reassemble audio: plays once Johnny has fully reassembled.
- Dalek audio: drone speech during the warning before firing, with a cooldown to prevent constant chatter.

The VR pickers select files already in Config. Desktop Browse copies outside audio into **Config/Library/Audio/LaserRange**, retaining existing files by choosing a distinct name when necessary. References are saved relative to Config. Blank choices use defaults. Default hit sounds are generated; the default voices for critical damage, Johnny being alive again, and drone speech are bundled with the application. Custom audio is preloaded, not repeatedly read while playing.

## Optional introduction

With a sequence selected, your viewpoint begins on the ground in front of Johnny, facing him. The sequence plays its motion, audio, RGB effects and mouth illumination. Integrated TrackForward movement also animates treads and casters. Sequence scene/backdrop references do not replace the game arena.

Pull either trigger after releasing the menu-selection trigger to skip the introduction. After finishing or skipping, Johnny turns and moves to his starting position, blending the instruction pose back toward neutral. You then automatically teleport to the first lookout. Enemy combat is inactive throughout the introduction and deployment.

Author instruction movement relative to the robot's presentation position. The game adds a two-metre offset behind the combat starting position. Keep any root movement within the clear presentation area so the transition to the starting line is sensible.

With **None** selected, or if an instruction file cannot load, that section is skipped: Johnny starts ready at the line and you are placed directly on the first lookout. This is the default until you add an instruction sequence.

## Combat and lives

- Left stick Y drives Johnny; X turns him.
- Right stick Y aims the shoulder laser vertically.
- Hold either trigger to fire. Release after a menu or startup interaction before firing.
- Tap A for the pause/menu controls.

Drones warn with a blue flash and speech before firing visible blue bolts at approximately five metres per second. Aim is captured during the warning, so movement can evade the shot. Solid obstacles block shots in both directions. A brief damage immunity prevents several simultaneous bolts from instantly consuming a life.

Johnny takes **three hits per life**, with **three lives**. A fatal hit plays the critical-damage sound, waits briefly, and runs the existing disassembly animation. If a life remains, the fallen robot is relocated to the marked safe bay behind the starting line and reassembles using the same clip in reverse. The alive audio plays on completion. Johnny then receives **five seconds of shield protection**. Drones do not target him during these protected phases, and stale enemy bolts are cleared. The viewpoint stays on its observation platform during respawning.

Losing the third life leaves Johnny disassembled and shows Game Over. Tap A for Restart or Exit. Score and destroyed targets persist across respawns. The range still contains three waves of 6, 8 and 10 drones, worth 100 points each. The status panel shows phase, lives, hits remaining, score, targets, aim elevation and protection.

## Teleport between designated lookouts

There are four marked flat-topped observation platforms. They have cyan landing rings; a valid selected platform highlights green. The first lookout is used automatically at game start.

**Hold A** for at least a quarter-second to aim teleportation. The beam starts at your right controller and follows a gravity arc. Pointing the controller higher raises the arc; the right stick adjusts its range and horizontal direction. A glowing circle marks the first surface the arc hits.

Only an arc ending inside a designated platform's landing area is accepted. Unmarked ground, rocks, crate sides and blocked destinations are invalid and display a red landing indicator. **Release A over a green platform** to teleport to its centre, facing Johnny. Releasing over an invalid destination keeps you where you are. A short tap of A still opens the menu.

Holding A temporarily reserves the sticks and trigger for teleport selection, so laser aiming/firing and Johnny's driving do not change accidentally. Combat continues while you choose a destination; use the pause menu if you need a break. Teleport moves only your viewpoint. Johnny stays where you drove him. Your tracked standing height is retained above the platform floor, and head pitch remains under your physical control.

## Integration and limits

The game remains local to the VR Player. It does not edit timelines, save robot poses, alter body calibration or send commands to physical hardware. The desktop Animation Editor is unchanged. Exiting restores the loaded backdrop and your previous room transform; it does not automatically resume playback or Follow mode.

The combat geometry, enemy bolts, teleport arc and landing circles render in both headset and Clean Output. Enable **Include VR overlays** to capture the score/menu panels as well. Default voices live under the application's **Audio/LaserRange** folder; chosen media and gameplay settings stay under Config.

A model without a baked FallApart clip still uses the life/respawn states but cannot display that animation; the Player reports this. The supplied full-body model's actual baked clip was checked. Enemy drones are still fixed-position opponents rather than pathfinding actors.

## Validation

Release publish succeeded. **46 focused combat/startup/teleport checks**, **29 existing game checks**, and **52 Follow-controls checks** passed. Checks cover warning/fire/hits, cover, three lives, safe respawning, actual baked clip forward/reverse application, completed-reassembly audio timing, optional intro/deployment states, tap/hold routing, ballistic valid/invalid landings, restored trigger behavior, deck height and facing, media persistence, custom/default audio preloading and GPU range rendering. A current full-robot preview was rendered from the first lookout.

The full suite and headset testing were not run. Real-controller teleport feel, introduction playback in the headset and combat balance still require gaming-PC testing.
