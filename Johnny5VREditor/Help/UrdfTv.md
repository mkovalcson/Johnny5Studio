# URDF TV and Screen Media

## Present a video or image

1. Load a 3D URDF backdrop that contains a supported TV screen.
2. Add **URDF TV Front** at the desired timeline time. Use **Build…** to choose travel time, screen fill and screen link.
3. Add **TV Show** at that time or later. Use **Build…** to import the image/video and set fit, start offset, loop and volume.
4. Add **URDF TV Return** when the presentation should end. Set the return time in its builder.
5. Apply and close Edit Commands, then preview and save the sequence.

Expected result: the camera faces the TV, plays the selected media, then returns to normal robot framing. These are red-icon URDF-only actions; they do not drive hardware.

## Controls and playback details

With a 3D URDF backdrop that has a TV (the Matrix Construct backdrop does), three Edit Commands entries put images and video on the TV screen:

- **URDF TV Front** glides the camera past the robot to face the TV squarely, sized so the screen fills most of the view. Text: `{"seconds":3,"fill":0.85,"screen":"backdrop_tv_screen_link"}`. The robot is hidden while the camera passes it and while facing the TV; while it is hidden, the editor no longer redraws the 3D scene for servo changes, which leaves more time for video playback.
- **TV Show** draws an image or video over the screen. **Build…** picks a file and copies it into `Config/TvMedia`. Text: `{"path":"TvMedia/clip-….mp4","fit":"Contain","startSeconds":0,"loop":false,"volume":100}`. `fit` is Contain, Cover or Stretch; `volume` is 0–100. A TV Show with an empty path blanks the screen. Each show lasts until the next TV Show or URDF TV Return, so a series of TV Shows makes a slideshow or playlist.
- **URDF TV Return** fades the show out and glides back to the timeline's normal camera. Text: `{"seconds":3}`.

The show fades in 0.3 s after the camera arrives. Video time counts from the later of the TV Show command and the arrival, plus `startSeconds`, so a show placed during the glide starts from its beginning. Playback follows the editor's play, pause, seek and speed controls. Images: png, jpg, bmp, gif, tif. Video: mp4, m4v, mov, wmv, avi, asf (whatever Windows Media Foundation can decode). When the head is undocked, only the undocked view plays video sound.

The images and video are drawn by the editor above the 3D view, not rendered in 3D, so they are sharp at any resolution. The screen area is 4:3; `get_state.urdf.tv.screenRect` gives its pixel rectangle for the current view size. These commands are display-only and never reach hardware or exports.

While a [video clip](VideoClips.md) plays behind the robot, the 3D stage and its TV are hidden, so TV commands rest until the clip ends.
