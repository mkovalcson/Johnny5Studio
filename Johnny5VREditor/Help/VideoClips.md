# Video Clips behind the Robot

## Put a video behind the robot

1. Place the timeline cursor where the video should start.
2. Right-click the timeline and choose **Insert video clip at … s…**, then pick the video.
3. The video is copied into `Config/VideoMedia` and appears as a violet bar just above the time axis, with its file name and the outline of its sound.
4. Press **▶ Sequence**. The video plays behind the robot for the bar's span, then the scene in effect returns.

Expected result: the robot is drawn over the video. Its own audio and mouth lights are unchanged; the video's sound plays alongside it.

## How a video clip relates to backdrops

- A video clip **covers whatever backdrop is in effect** while it plays. When the clip ends, the current **Load Scene** backdrop shows again; no extra Load Scene command is needed.
- A **3D stage backdrop** (for example the Matrix Construct) is hidden while a clip plays. Its position keeps updating underneath, so the room is in the right place when the clip ends.
- **Contain** leaves bars where the video's shape differs from the view. The bars show the backdrop's colour, so a Chromakey Green backdrop stays green for keying.
- **URDF TV** commands rest while a clip plays, because the TV belongs to the hidden stage.
- Meeting mode's temporary background still takes precedence.

## Editing a clip

- **Drag the bar** to move the clip. Only that clip moves; other commands stay where they are. A dragged bar stops at its neighbours.
- **Double-click the bar**, or right-click it and choose **Video clip settings…**:
  - **Fit**: Contain, Cover or Stretch.
  - **Volume** 0–100 and **Muted**. Playback Volume also applies.
  - **Skip to**: the video position shown at the clip's start.
  - **End at**: the video position where the clip ends; leave it empty to play to the end of the file.
- Right-click the bar and choose **Delete video clip** to remove it. The copy in `Config/VideoMedia` is kept, because other sequences or undo may still use it.
- Clicking a bar without dragging moves the cursor there.

Clips **never overlap**. Inserting, moving or lengthening a clip onto another one is refused, and any other edit that would make two clips overlap (dragging command markers, Edit Commands, paste) is undone with a message.

## Sound and the mouth

The video's sound plays on its own output, mixed with the timeline's audio. Timeline audio normally plays one source at a time; video sound is separate from that. The mouth lights follow only the timeline audio, never a video's sound. When no timeline audio is playing, the video's sound keeps the playback clock in step with the video.

Video clips are URDF-only: they never reach the physical robot, hardware animation exports or Library Sequences.

## Formats and playback

- Video: mp4, m4v, mov, wmv, avi, asf, whatever Windows Media Foundation can decode. H.264 in MP4 works best.
- For smooth seeking and sync, use **constant frame rate** video (OBS records this by default) with a keyframe about every second.
- Importing the same file again reuses the earlier copy in `Config/VideoMedia` (same name, size and modified time).
- The picture follows play, pause, seek and Playback Speed. While paused or scrubbing, it shows the frame at the cursor, without sound.
- A missing file shows as a red bar; a video without sound shows **(no sound)**.

In Edit Commands, a clip appears as a **Video Clip** command at its start time, with its settings as text.

If seeking a clip is slow or jumps, the file may be a fragmented or otherwise unindexed recording; remux it to a regular MP4 (in OBS: **File > Remux Recordings**).

## Final render: Export Robot Frames

For the best finished video, choreograph against the clip in the editor, then export the robot on its own and composite it over the original video in Vegas (or another editor). Nothing is captured in real time, so frames are never dropped and the timing is exact.

1. Show the URDF view (docked or undocked) at the shape you choreographed in, and turn off **Drive HW**.
2. Choose **File > Export Robot Frames (PNG with transparency)…**.
3. Check the settings, then press **Export**:
   - **Size**: default 1920 × 1080. **Frames per second**: default 30; match the video.
   - **Start** / **End**: default 0 s to **End of the sequence**. With End of the sequence ticked, a later export follows the sequence if it grows.
   - **Folder**: default "*sequence name* frames" beside the sequence. Earlier `frame_*.png` files there are replaced after you confirm.
   - **Robot only** leaves a 3D stage backdrop out. **Frame the video clip's picture area** makes the robot line up with the video exactly as in the editor.
   - The dialog shows the frame count, a size estimate and the export time, measured from frames actually rendered with these settings. **Measure size again** re-measures after changing the size or options.
4. In Vegas, open `frame_000000.png` as an image sequence at the same frame rate and place it at the Start time on the video's timeline (0 s for a clip starting at 0 s).

The settings are remembered for each sequence in `Config/RobotFrameExports.json`, so a later export of the same sequence starts from them. The folder also gets `robot-frames.json`, recording the start time, frame rate and frame count.

How it renders: each frame sets the cursor to its exact time, steps calibrated servo motion by exactly one frame, drives the mouth from the timeline audio, and captures the robot from a hidden renderer drawn by the graphics card. Each frame is captured over black and over white, and the difference gives true transparency with smooth edges. Frames hold only the 3D scene: buttons, the legend, TV screen media and the video itself are left out. The renderer sits behind the editor during the export, and the framed area must fit on the main screen (1920 × 1080 needs a screen at least that size). A 1080p frame takes roughly a quarter of a second and a few hundred KB. A 5-minute clip at 30 fps is about 9,000 frames: roughly 40 minutes and 2–3 GB. Cancel keeps the frames written so far.

## Choreographing with Claude

With **File > Enable Codex Connection** on, Claude (through the Johnny 5 editor MCP server) can see the video: it lists the clips, looks at pictures from the video at timeline times, and takes a snapshot of the robot's current pose drawn over the video at the cursor. Ask, for example, "look at the clip from 4 to 10 seconds and have him duck when the car swerves". Claude can read clips but not add or move them.
