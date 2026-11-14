# Recording Audio

In Record from Controller / Webcam, check Record Audio and select a microphone. Refresh microphones rescans currently connected input devices. Selecting a microphone starts level monitoring, even before a take; it does not yet write audio to disk.

The peak meter shows dBFS: values closer to 0 are louder. CLIP latches red when samples reach the clipping threshold; click it to clear. Adjust the microphone’s Windows/input gain or your distance to avoid clipping. The window does not provide an input gain control for recorded WAV data. Meeting mouth gain is a separate visual adjustment.

## Files and timing

Capture is mono, 44.1 kHz, 16-bit PCM WAV. Unsupported device formats are reported. Name/save the sequence before arming an audio take. Files are created beside the sequence, named SequenceName_Take1.wav, SequenceName_Take2.wav, and so on without overwriting existing files. An available number can be reused after its discarded file is deleted.

Audio starts with the take, not when Ready to Record is pressed. A provisional Play command is added at the starting cursor. That command is excluded from current playback while its WAV is being written, preventing the recording from playing back into itself. Other existing timeline audio may still play; headphones help avoid microphone pickup.

Audio takes temporarily run the timeline at 100% speed so speech and motion share the same time base. Closing the recording window restores the previous playback speed.

## Review and cleanup

Replay Recording includes the take’s audio. Keep Take retains the WAV and its Play command alongside the movement edit. Discard / close removes that take’s WAV and Play command. Undo after Keep removes the timeline edit but leaves the WAV available for Redo. A microphone failure stops the take for review; reconnect/reselect the input before retrying.

Selecting or remembering a microphone can start monitoring when the recording window opens. Closing the window releases capture. No video file is recorded by this option.

## Reversible audio trims

Right-click an audio file's timeline handle to open Audio timing. Timeline start still moves the handle and following commands. **Skip to** and **End at** use seconds within the original audio file. Skip to initially suggests the current timeline cursor time (limited to the file length); End at initially uses the file's end. The waveform and sound reflect the committed bounds without modifying the WAV.

Each **Commit** button applies its value and scrub checkbox, then locks that row. Click it again to unlock the row; edit and click Commit once more to apply. Closing ignores uncommitted edits. Use Skip to = 0 or End at = the full file length to restore the full audio range.

**Scrub commands before skip to** hides commands before the source-file Skip to position and shifts the remaining associated commands left by the skipped duration. **Scrub commands after end at** hides associated commands after the source-file End at position. Hidden commands remain stored with the audio handle, survive sequence save/reopen, and do not play. Reducing Skip to, increasing End at, or disabling the corresponding scrub checkbox restores them. No commands or audio samples are deleted. Each commit is undoable; save the sequence normally to persist it.

New audio recordings are linked to their recorded commands, independently of command grouping. For older files without take links, the first scrub adopts unassigned non-audio commands from the audio handle up to the next audio handle. Commands linked to a different take are not affected. Removing an audio handle also removes its stored hidden commands (Undo restores the handle and archive).

The Sequence bar shows **Audio +seconds** beside the cursor time: elapsed timeline time since the most recent audio handle, matching that handle's color. This is time since the handle, not the source-file position after a skip.

## Automatic Johnny 5 voice conversion

Enable **Automatically Convert Voice when Recording Finishes** under Record Audio to convert a retained take. This setting is remembered. Conversion starts only after **Keep Take**, after the WAV has been finalized; stopping or replaying a take does not start conversion, and discarded takes are not converted.

The editor runs its bundled `Scripts/ConvertOneFileToJohnny5.bat` in the background with the recorded WAV as its argument and `J5_NOPAUSE=1`. The supplied script requires Applio at `X:\Johnny5\Applio` and its ShortCircuit1 model/index; adjust the bundled script if these locations change. Success requires a zero exit code and a readable, nonempty sibling `SequenceName_Take1_j5.wav`. The Play command is then changed to that file at its original timeline position. The lower-right status area reports **Converted File {filename} is ready to play.** Save the sequence normally to persist the changed command.

The original WAV is retained. Failure leaves the Play command pointing to the original recording and reports the error in the status area. Conversion does not overwrite an existing `_j5.wav`. Because of the batch file's shell expansion, folder/sequence names containing shell characters such as `&`, `%`, or `!` are rejected with a status message. Conversions run one at a time. If you switch sequences, undo the take, or replace its Play command while conversion is running, the converted file remains available but the editor does not rewrite another command or an inactive sequence. Replacing the audio reference is a separate undoable edit.

## Replace processed speech

Both primary and additional audio handles offer **Special Audio Replace…**. It can replace audio alone or use a Command Update JSON to retime following animation. See **Special Audio Replace** for the workflow, format and trim restrictions.
