# Insert Audio copies outside files into Config/Library/Audio

November 13, 2026. J5 Studio Desktop and VR Editor.

## What changed

Every audio file picker in the editors used to refuse an audio file outside the Configuration
folder:

- **Insert Audio…** on the Start a sequence panel. It runs Open Audio…, which sets the
  sequence's primary audio.
- **File → Open Audio…**, the same path.
- **Insert audio** at the cursor from the timeline context menu, which adds a Play command.

Now:

- A file **outside** Configuration is **copied** into `Config/Library/Audio`, the same folder
  Library Sequences use for their audio. The sequence uses the copy: the primary audio, or the
  new Play command as a Config-relative path such as `Library/Audio/beep.wav`. The status bar
  says what was copied and under which name.
- A file already **inside** Configuration is used where it is, with the same Config-relative
  reference as before. Nothing is copied.
- The user's original file is never moved, changed or deleted.
- If `Library/Audio` already has a file with that name:
  - identical content (same size and SHA-256): that file is reused, so no duplicate is made;
  - different content: the existing file is **not** overwritten. The copy gets the next free
    name, `name-2.ext`, `name-3.ext` and so on (an identical file under one of those names is
    reused too).
- If the copy fails (missing file, disk full, access denied) an error is shown and nothing is
  inserted.

Both editors call the same shared code, so they behave the same way.

Second pass, same day: the first version fixed only Insert audio at the cursor. The Start
panel's Insert Audio… button calls `OpenAudio_Click`, so it still showed the "outside
Configuration" error. Both pickers now go through `MainWindow.ImportPickedAudio`.

## Files

| File | Change |
|---|---|
| `Shared/Configuration/LibraryAudioImport.cs` | New. `LibraryAudioImport.ImportIntoConfig(configRoot, source, out copied)` and `Folder(configRoot)`. |
| `Shared/EditorUI/MainWindow.AudioImport.cs` | New. `ImportPickedAudio(picked)`, shared by both editors: copies when needed, shows a status message, or shows an error and returns null. |
| `ServoAnimator/ServoAnimator/MainWindow.xaml.cs` | `OpenAudio_Click` (also behind the Start panel's Insert Audio…) and `InsertAudioFileAtCursor` call `ImportPickedAudio` instead of `RequireConfigPath`. |
| `Johnny5VREditor/MainWindow.xaml.cs` | Same change. |
| `Shared/Configuration.Checks/LibraryAudioImportChecks.cs` | New checks (below). |
| `Shared/Configuration.Checks/Program.cs` | Runs the new checks and the wiring check (below). |
| `ServoAnimator/ServoAnimator/Help/SequenceEditor.md`, `Johnny5VREditor/Help/SequenceEditor.md` | One paragraph describing the behavior. |

The VR Player's dev editor copy (`Johnny5VR/packaging/editor-0.9.0`) was refreshed with the new
`AnimationEditorPlayer.dll` and `J5Studio.*.dll`.

## Checks

The shared configuration checks (`Shared/README.md`) now also run:

- 12 library audio import assertions on a temporary Config folder: an outside file is copied
  with the same content and a Config-relative reference; the original is unchanged; importing
  it again reuses the copy; a different file with the same name becomes `beep-2.wav`, then
  `beep-3.wav`, without overwriting; a file inside Config is not copied; a missing source is
  reported.
- An IL wiring check on the editor assembly:
  - `ImportPickedAudio` calls `LibraryAudioImport`.
  - `OpenAudio_Click` and `InsertAudioFileAtCursor` both call `ImportPickedAudio` and no longer
    call `RequireConfigPath`.
  - The Start panel's `EmptyInsertAudio_Click` reaches `OpenAudio_Click`.

  It fails on the original build and on the first-pass build, which missed the button (both
  were checked against the editor-0.9.0 dev copy). It passes on both new builds. No editor
  window is opened.

```bash
dotnet build Shared/Configuration.Checks/J5Studio.Configuration.Checks.csproj -c Release
```

```bash
dotnet Shared/Configuration.Checks/bin/Release/net10.0-windows/J5Studio.Configuration.Checks.dll ServoAnimator/ServoAnimator/bin/Release/net10.0-windows/AnimationEditorPlayer.dll Config Shared/Configuration.Checks/baseline.json
```

```bash
dotnet Shared/Configuration.Checks/bin/Release/net10.0-windows/J5Studio.Configuration.Checks.dll Johnny5VREditor/bin/Release/net10.0-windows/AnimationEditorPlayer.dll Config Shared/Configuration.Checks/baseline.json
```

Note: the VR Editor now builds to `Johnny5VREditor/bin/Release/net10.0-windows` (no `win-x64`
folder), unlike the path in `Shared/README.md`.

Results on November 13: both editors build. Against the committed `baseline.json` the run stops
early with `Configuration compatibility changed: ModelSettings, URDFconfig.json`. That comes from
the live `Config` settings (camera framing, the new TipToe motion, robot finish and accessory),
not from this change. With a baseline captured from today's Config, every group passed for both
editors: 31 persistence, 12 library audio import, the Insert Audio wiring check, and the settings
compatibility check. The committed baseline was not changed.

## Backups

`Archive/DesktopChanges/audio-library-copy-20261113/` holds the previous copies of both
`MainWindow.xaml.cs` files, both `Help/SequenceEditor.md` files, `Shared/Configuration.Checks/Program.cs`
and `PersistenceChecks.cs`, `Documentation/README.md`, and the replaced editor-0.9.0 DLLs and `Help/SequenceEditor.md`
(`editor-0.9.0-before/`). `second-pass/` holds the files as they were after the first pass:
both `MainWindow.xaml.cs` files, `Program.cs`, this document, and the first-pass editor-0.9.0
DLLs.
