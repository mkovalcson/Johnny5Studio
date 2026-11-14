# Headroom audio animation: markup syntax and procedure

Give this document to Claude (Claude Code in the desktop app, with the `johnny5-editor` MCP tools) together with an audio clip that is already on the timeline and a markup text file. Claude aligns the script to the recording, optionally rebuilds the audio with Max Headroom effects, and inserts head movements that match. Written 2026-09-30 against Animation Editor 1.38.

## 1. What you provide

1. The Animation Editor open on the sequence, with **File > Enable Codex Connection** checked, playback stopped and Live Drive off.
2. The clip on the timeline as a **Play** command. If Skip to / End at are set, those trims define the part to use.
3. **Save the sequence** before starting. The API does not report audio trims, so Claude reads Skip to / End at from the saved file (it only reads it).
4. A markup text file (plain text, UTF-8) written in the syntax below, containing exactly the words spoken in the trimmed part of the clip.

Clean voice recordings work best: no music under the voice, one speaker.

## 2. Markup syntax

A markup file is the spoken script with two kinds of additions. Everything else (letters, digits, spaces, hyphens, apostrophes, ordinary punctuation) is spoken text and is matched against the recording as written. `M-M-Max` is therefore a stutter the speaker actually recorded, not a generated effect.

### 2.1 Movements: `{…}`

Curly braces hold a movement instruction. They are never spoken.

- Placement: the movement starts at the **start of the next spoken word**. A movement at the very end of the text starts at the end of the last word.
- Content: free description (`{eyes wide}`, `{slow head tilt left, suspicious}`, `{looks down at watch}`) or a Library item by exact name (`{pose: eyeroll}`, `{sequence: Bored_HeadOnly}`).
- Optional duration: `{nod yes, 1.5s}`. Without one, Claude chooses a natural length that ends before the next movement.
- Movements use only the physically driven head controls: NeckTurn, NeckNodUp, NeckTiltRight, EyesHorizontalRight, EyesVerticalUp, IrisClose, FlapsOpen, FlapTiltUp, VentsOpen, NoseBody, NoseBasket, MFR_UpDown, MFR_Rotate, Whip_Antenna_RaiseLower, Whip_Antenna_Rotate, Microphone_RaiseLower. No eye pops, no body controls. The mouth LEDs are left to the editor's audio-driven mouth.

### 2.2 Max Headroom effects: `|` `^` `~` `<` inside words

These four characters never occur in spoken words, so they always mean "change the audio". Remove them and you get the word that was actually said, which is what Claude matches against the recording.

```
You write       Spoken word   Effect
d|||elighted    delighted     Stutter: the letters BEFORE the bars ("d") repeat, one repeat per bar (3 here)
wh||^at         what          Stutter; each repeat steps up in pitch. ^ after the bars = +1 semitone per
                              repeat, ^^ = +2
^meet           meet          Pitch jump on the whole word; each ^ at the start = +3 semitones (^^meet = +6)
we~~ll          well          Stretch the letter BEFORE the tildes ("e"): length becomes 1 + (number of ~)
                              times the original (~~ = 3x)
stop<           stop          Tape-stop: the end of the word winds down to a stop; << doubles the wind-down
```

Rules:

- Marks go **inside or at the edges of a single word**, never on their own or between words.
- One effect of each kind per word. A stutter and a stretch may share a word only when they touch different letters.
- A stutter's repeated slice is the letters from the start of the word (or from the end of the previous stutter/stretch in that word) up to the bars.
- To speak one of these characters literally (rare), escape it: `\|` `\^` `\~` `\<`.

If the file contains none of `| ^ ~ <` (unescaped), it is a **plain script**: the audio is not changed. If it contains any, it is a **Headroom script**: the audio is rebuilt with the effects.

### 2.3 Example

```
{eyes wide} Well, well, we~~ll. I'm {head tilt right} d|||elighted to ^meet you!
{pose: eyeroll} wh||^at did you expect? {nod, 1s} Time to stop<
```

## 3. Default amounts

| Setting | Default |
|---|---|
| Stutter pitch step | +1 semitone per `^` after the bars, per repeat |
| Pitch jump | +3 semitones per leading `^` |
| Stretch | length × (1 + number of `~`) |
| Tape-stop | the final 40% of the word (at most 0.5 s), slowing to 0; `<<` doubles that |
| Crossfades | 5 ms at every cut, so there are no clicks |

You can override any default for one job by saying so when you start (for example "make pitch jumps +5 semitones").

## 4. Procedure Claude follows

Nothing is written into the timeline until step 5 is confirmed, and original audio files are never modified or overwritten.

### Step 1: Read the editor and inputs

1. `list_editors` / `select_editor` / `get_state`: confirm the sequence, playback stopped and Live Drive off.
2. `list_commands` with `servo: "Play"`: find the clip, its timeline start and its file (relative to the workspace's `Config` folder unless absolute). If there are several Play commands, ask which one.
3. Read the **saved** sequence JSON (read only) for that Play command's `audioTrim`. A trim only counts when committed: Skip to = `skipSeconds` if `skipCommitted` is true, otherwise 0; End at = `endSeconds` if `endCommitted` is true, otherwise the end of the file. At the Play command's timeline time the file is already at Skip to. If the editor reports unsaved changes, ask you to save first.

### Step 2: Check the markup

Parse the file into spoken words, movements (with the word each attaches to) and effects (with the letters each affects). Report any rule violations (section 2.2) and stop until they are fixed. Report the path, plain or Headroom, and summarize: word count, movements, effects.

### Step 3: Time every word

1. Environment: `py -3.10-64` (Miniconda Python 3.10). Every script must first preload Windows' runtime DLLs, or ctranslate2 and onnxruntime crash:

   ```python
   import ctypes, os
   for dll in ("msvcp140.dll", "msvcp140_1.dll", "msvcp140_2.dll", "vcruntime140.dll", "vcruntime140_1.dll"):
       p = os.path.join(os.environ["SystemRoot"], "System32", dll)
       if os.path.exists(p): ctypes.WinDLL(p)
   ```

2. Take the trimmed part of the audio (Skip to … End at). faster-whisper model `small` (CPU, int8), `word_timestamps=True`, `vad_filter=True`.
3. Match the recognised words to the script's spoken words (normalised: lower case, punctuation removed) with sequence alignment. The script's words are authoritative; recognition only supplies times. Words the recogniser missed are interpolated between their neighbours.
4. Report matched percentage and any word with confidence below 0.5 or interpolated timing. Stop and ask if under 90% matched.

### Step 4 (Headroom scripts only): Rebuild the audio

1. Slice times inside a word: letter position ÷ letter count × word duration, then snapped to the nearest sound onset within ±40 ms (spectral-flux peak).
2. Effects (pedalboard 0.9 / Rubber Band for pitch and stretch; numpy for the rest):
   - stutter: insert the repeats before the original slice (repeat *k* pitch-shifted by *k* × step);
   - stretch: replace the slice with `time_stretch` (high quality);
   - pitch jump: pitch-shift the word in place (duration unchanged);
   - tape-stop: resample the final part with speed falling linearly to 0.
   The rest of the audio is copied through unchanged, with 5 ms crossfades at every cut.
3. Write to `Config\SpecialAudio\<clip>-headroom-<yyyyMMdd-HHmmss>.wav` (same sample rate and channels as the source), plus the matching `…-headroom-<…>.timeline.json`.
4. The `.timeline.json` must pass the editor's validation (`HeadroomTimeline.Validate`). Snake_case keys; `format: "johnny5-headroom-timeline"`, `version: 1`; `sample_rate`, `original_duration` (the trimmed input), `output_duration`, `total_added_seconds` (= output − original, within 0.00002 s).
   - `time_map`: contiguous, ordered segments covering both files. `kind`: `original` (same duration; also used for pitch-jumped words), `inserted` (stutter repeats: zero original length), `stretch`, `tape_stop`.
   - `events`: ordered by `output_start`, unique `id` ≥ 1. `type`: `stutter` (`repeats`, `slice_seconds`, `pitch_step_semitones`, and a `repeat_list` of contiguous windows with `output_start`, `output_end`, `duration`, `pitch_semitones`, matching an `inserted` segment exactly), `stretch` (`factor` > 0), `pitch_jump` (`semitones`), `tape_stop` (`slowest_speed` 0–1).
   - `words`: one entry per script word with `align_confidence` 0–1.

   Claude validates the file with the same rules before handing it over.

### Step 5: Confirm with you

Summarize what will change, with times: the movement list and, for Headroom scripts, the effect list and the new file's length. Wait for your OK. For Headroom scripts, also send the new WAV so you can listen first.

### Step 6 (Headroom scripts only): You swap the audio

The API cannot replace audio or change trims, so you do this part in the editor:

1. If Skip to / End at were set: open the clip's **Audio timing / Skip to / End at…** and clear the trims. The new file already starts and ends where the trims were, so the clip still starts at the same timeline time.
2. Right-click the audio handle, choose **Special Audio Replace…**, and pick the new WAV. The `.timeline.json` is filled in automatically.
3. Leave **Re-time the animation** and **Repeat the face during stutters** checked. Uncheck **Add motion** if you only want your `{movements}`. Leave it checked to also get the editor's automatic Headroom motion during effects; Claude then keeps your movements clear of the effect windows.
4. Apply (one undo step), then tell Claude it is done. Claude checks with `list_commands` that the Play command now points at the new file.

### Step 7: Insert the movements

1. Timeline time of a movement = Play start + word start in the audio as it plays now: for plain scripts, word time − Skip to; for Headroom scripts, the word's time in the new file.
2. Each movement becomes smooth keyframes on the head controls (native units, starting from and returning to the pose in effect, eased). Library references are expanded from `Config\Library\Animation\<name>.json`. Movements never overlap each other on the same control.
3. `list_commands` over the whole range first. Existing commands are never replaced; if one conflicts, Claude asks.
4. One `insert_commands` request (one undo step) with `enableSplines` for the controls used. Nothing is sent to hardware.

### Step 8: Verify

Play from just before the clip and summarize: commands inserted, the time range, and any words with uncertain timing. Adjust on request (timing nudges, bigger or smaller movements, different effect amounts: for Headroom scripts that means rebuilding the audio and repeating step 6). **You save the sequence** in the editor.

## 5. Limits

- Word timing is typically within 0.05–0.1 s. Positions inside a word (for effects) are estimates refined by onset detection, not phoneme-exact.
- Claude cannot hear the result: you are the judge of sound and feel.
- Free-form movement descriptions are interpreted by Claude, so the same description can come out slightly differently in different chats. Library references (`{pose: …}`, `{sequence: …}`) are exact.

## 6. Installed tools (for reference)

- Miniconda Python 3.10 (`py -3.10-64`): numpy, Pillow, pybullet 3.2.7, faster-whisper 1.2.1 (model `Systran/faster-whisper-small` cached under `%USERPROFILE%\.cache\huggingface\hub`), pedalboard 0.9.25, soundfile 0.14.0.
- Animation Editor API reference: `ServoAnimator\Help\AnimationEditor_API_For_AI.md`.
