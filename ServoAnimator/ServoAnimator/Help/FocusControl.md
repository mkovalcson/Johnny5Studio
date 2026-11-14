# Focus Control and Background Input

Open **Config > Focus Control**. These settings decide which enabled inputs can continue while another application has focus.

## Choose input and output independently

Xbox and Steam each have an enable choice plus separate **Drive URDF model** and **Drive HW** choices. Stream Deck / Library API has the same destination choices. Physical output also requires Drive HW to be on. Save applies the settings.

Controller connections stay open when focus changes. The device button enables or disables control; its connected light indicates availability, not permission to move hardware. Ordinary editor dialogs pause controller output; mapping configuration provides its own Test URDF mode.

## Movie playback and global arrows

After starting a movie, enabling Movie Playback reserves plain arrow keys for the movie while another app has focus, including pauses and waits between cues:

- Up: play/pause.
- Right: next cue.
- Left: restart/previous.
- Down: stop and rewind.

The square Stop button, starting standalone sequence or Library playback, closing the movie, or disabling background Movie Playback releases these keys. Audio playback itself continues when focus changes.

## Stop output

When Drive HW is active, Escape is a global stop for playback and disables servos. Use the Hardware buttons to disable a particular controller or Stream Deck input. Meeting mode has its own Stop meeting button and always drives only the URDF.
