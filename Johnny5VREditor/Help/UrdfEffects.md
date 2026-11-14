# Disassemble, Reassemble and Beam

## Disassemble and Reassemble

**Disassemble** is a URDF-only timeline action using the full-body model's baked collapse animation. It sends no motion to physical hardware.

### Add an effect

1. Load the compatible full-body model.
2. Place the timeline cursor and add **Disassemble** in Edit Commands.
3. Press **Build…**, choose forward collapse or reverse reassembly, and set speed from 0.1 to 10 (1 is normal).
4. Apply and close, preview, then save the sequence.

A forward clip leaves the robot in pieces. A reversed clip starts in pieces and reassembles; normal joint control resumes when reassembly finishes. The latest Disassemble command at or before the cursor determines the effect. Seeking evaluates the clip at the chosen time rather than requiring it to play from the beginning.

The model must include its baked effect assets. Loading an unrelated URDF does not create a collapse simulation automatically. The effect speed is separate from the Sequence playback-speed percentage.

## Beam In and Beam Out

**URDF Beam** is a TNG-style transporter effect for any URDF model. The robot shimmers into existence (Beam In) or dissolves away (Beam Out) inside a glowing column of sparkles.

1. Place the timeline cursor and add **URDF Beam** in Edit Commands.
2. Press **Build…** and choose:
   - **Direction**: Beam in (materialize) or Beam out (dematerialize).
   - **Seconds**: 0.5 to 20 (default 3.5).
   - **Particles**: particles fall from the top of the URDF view into the column when beaming in, and rise from the column to the top of the view when beaming out.
   - **Sound**: the synthesized transporter sound (`Library/Audio/Beam_In.wav` or `Beam_Out.wav`), no sound, or any audio file you choose. **Volume** is 0 to 100%.
   - **RGB sparkle**: also sparkles the physical robot's eye and vent rings for the same time.
3. Apply and close, preview, then save the sequence.

### Visibility

- A sequence with no URDF Beam commands always shows the robot.
- If the first beam in a sequence is a Beam In, the robot is invisible until it starts.
- After a Beam Out the robot stays invisible until the next Beam In.

The latest beam at or before the cursor decides what you see, so seeking and scrubbing show the effect at that moment. The beam sound plays only while the sequence is playing and stays in step with the cursor.

To use your own sound, choose **Audio file below** in the builder, or replace the two WAV files in `Library/Audio`. `tools/synth_beam_sound.py` regenerates the originals.

### RGB sparkle on the robot

With **RGB sparkle** on, the beam also sends `Beam,140,200,255,200,Both,LR,<ms>,In|Out` to the Arduino. Pixels twinkle at random, building up and fading out over the beam. The ring preview shows exactly the same pattern as the hardware. Exports and hardware playback include this as an ordinary RGB command. The robot's Arduino Nano needs the updated `ArduinoOpenSaintRGB.ino` sketch, which adds the BEAM effect.
