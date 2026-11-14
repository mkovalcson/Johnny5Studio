using System.IO;
using System.Windows;
using System.Windows.Controls;
using NAudio.Wave;

namespace ServoAnimator;

public partial class MainWindow
{
    private sealed record PrimaryAudioSnapshot(string Path, string File, string StoredPath, double Duration);
    private PrimaryAudioSnapshot CapturePrimaryAudio() => new(_audioPath, _doc.AudioFile, _doc.AudioFilePath, _doc.DurationSeconds);
    private void RestorePrimaryAudio(PrimaryAudioSnapshot state, bool tolerateUnavailable = true)
    {
        if (_audioPath != state.Path)
        {
            StopPlayback();
            _reader?.Dispose(); _reader = null; _activeSource = null; _lastDesiredKey = null;
            _audioPath = state.Path; _primaryDuration = 0;
            Waveform.PrimaryAudioName = ""; Waveform.SetAudio(null, null, .001, 0);
            if (!string.IsNullOrWhiteSpace(state.Path) && File.Exists(state.Path))
            {
                try
                {
                    var (min, max, duration) = BuildPeaks(state.Path);
                    _primaryDuration = duration;
                    Waveform.SetAudio(min, max, .001, duration);
                    Waveform.PrimaryAudioName = System.IO.Path.GetFileName(state.Path);
                }
                catch (Exception error)
                {
                    if (!tolerateUnavailable) throw;
                    ShowStatus("Audio reference restored, but waveform unavailable: " + error.Message);
                }
            }
            else if (!tolerateUnavailable && !string.IsNullOrWhiteSpace(state.Path))
                throw new FileNotFoundException("Replacement audio is no longer available.", state.Path);
        }
        _doc.AudioFile = state.File; _doc.AudioFilePath = state.StoredPath; _doc.DurationSeconds = state.Duration;
        Waveform.AudioOffset = _audioOffset;
    }

    private void ShowAudioHandleMenu(AudioClipVisual clip)
    {
        var menu = new ContextMenu { PlacementTarget = Waveform };
        var timing = new MenuItem { Header = "Audio timing / Skip to / End at…" };
        timing.Click += (_, _) =>
        {
            if (clip != null) { ShowAudioTrimDialog(clip); return; }
            if (string.IsNullOrWhiteSpace(_audioPath)) return;
            var proxy = new AudioClipVisual
            {
                Command = new ServoCommand { Servo = ServoNames.Play, TextValue = ConfigPathService.ToRelative(ConfigRoot, _audioPath), OffsetSeconds = _audioOffset },
                Name = Path.GetFileName(_audioPath), ResolvedPath = _audioPath,
                SourceDuration = _primaryDuration, Duration = _primaryDuration
            };
            ShowAudioTrimDialog(proxy);
        };
        var replace = new MenuItem { Header = "Special Audio Replace…" };
        replace.Click += (_, _) => SpecialAudioReplace(clip);
        menu.Items.Add(timing); menu.Items.Add(replace); menu.IsOpen = true;
    }

    // Promote primary audio only when a timing change is committed, after its undo snapshot.
    private void EnsureAudioCommand(AudioClipVisual clip)
    {
        if (_doc.Commands.Contains(clip.Command)) return;
        RestorePrimaryAudio(new(null, "", "", _doc.DurationSeconds));
        _doc.Commands.Add(clip.Command);
    }

    private void SpecialAudioReplace(AudioClipVisual clip)
    {
        if (!ApplyOpenCommandEditor()) return;
        if (IsRunning) PausePlayback();
        string original = clip?.ResolvedPath ?? _audioPath;
        double start = clip?.Start ?? _audioOffset;
        var dialog = new SpecialAudioReplaceWindow(this, original);
        if (dialog.ShowDialog() != true) return;
        try
        {
            using var replacement = new AudioFileReader(dialog.AudioPath);
            double duration = replacement.TotalTime.TotalSeconds;
            HeadroomTimeline timeline = null;
            if (!string.IsNullOrWhiteSpace(dialog.UpdatePath))
            {
                timeline = HeadroomTimeline.Load(dialog.UpdatePath);
                if (Math.Abs(duration - timeline.OutputDuration) > .020)
                    throw new InvalidDataException($"Replacement audio is {duration:F3}s; the Command Update expects {timeline.OutputDuration:F3}s (20 ms tolerance).");
                double oldDuration = clip?.SourceDuration ?? _primaryDuration;
                if (Math.Abs(oldDuration - timeline.OriginalDuration) > .050 && MessageBox.Show(this,
                    $"The old audio is {oldDuration:F3}s, but this update expects {timeline.OriginalDuration:F3}s. It may belong to a different take. Continue?",
                    "Take length mismatch", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            }
            var result = timeline == null ? new HeadroomEditResult(_doc.Commands.Select(c => c.Clone()).ToList(), 0, new())
                : HeadroomTimelineEditor.Apply(_doc, timeline, start, clip?.Command, dialog.Retime, dialog.RepeatFace,
                    dialog.AddMotion, dialog.Strength, _urdfConfig, _servoConfig, ApiUrdfView?.CaptureBackground(), ApiUrdfView?.CaptureCurrentFraming(), SplineServosEnabled().ToHashSet(), _splineHz);
            var replacedPlay = clip == null ? null : result.Commands.Single(c => c.Servo == ServoNames.Play &&
                c.OffsetSeconds == start && c.TextValue == clip.Command.TextValue);
            // Resolve millisecond collisions on the detached draft before changing the open document.
            if (!CommandConflictWindow.Resolve(this, result.Commands)) return;
            if (replacedPlay != null && !result.Commands.Contains(replacedPlay))
                throw new InvalidDataException("The selected audio command was removed by conflict resolution; replacement canceled.");
            if (replacedPlay?.AudioTrim is { } trim && (trim.Start >= duration || trim.EndCommitted && trim.EndSeconds > duration))
                throw new InvalidDataException("The existing Skip to / End at exceeds the replacement audio length. Adjust the trim first.");
            string destination = dialog.AudioPath;
            if (!ConfigPathService.IsWithin(ConfigRoot, destination))
            {
                string folder = Path.Combine(ConfigRoot, "SpecialAudio"); Directory.CreateDirectory(folder);
                destination = Path.Combine(folder, Path.GetFileNameWithoutExtension(destination) + "-" + Guid.NewGuid().ToString("N") + Path.GetExtension(destination));
                File.Copy(dialog.AudioPath, destination);
            }
            // Decode/cache before taking the transaction snapshot.
            BuildPeaks(destination);
            var oldCommands = Snapshot(); var oldAudio = CapturePrimaryAudio(); double oldOffset = _audioOffset;
            var undo = _undoStack.ToArray(); var redo = _redoStack.ToArray();
            try
            {
                PushUndo("Special Audio Replace");
                _doc.Commands = result.Commands;
                if (clip == null)
                {
                    RestorePrimaryAudio(new(destination, Path.GetFileName(destination), ConfigPathService.ToRelative(ConfigRoot, destination), start + duration), tolerateUnavailable: false);
                }
                else
                {
                    replacedPlay.TextValue = ConfigPathService.ToRelative(ConfigRoot, destination);
                    if (timeline != null && dialog.Retime && _primaryDuration > 0 && _audioOffset >= start)
                    {
                        _audioOffset = ServoCommand.TimeKey(start + timeline.Map(_audioOffset - start));
                        _doc.AudioStartOffsetSeconds = _audioOffset; Waveform.AudioOffset = _audioOffset;
                    }
                }
                _doc.DurationSeconds = Math.Max(start + duration, Math.Max(_audioOffset + _primaryDuration, _doc.Commands.Select(c => c.OffsetSeconds).DefaultIfEmpty(0).Max()));
                if (!RefreshAfterEdit()) return;
                ShowStatus($"Special Audio Replace: {Path.GetFileName(destination)} ready. " +
                    (timeline == null ? "Command timing unchanged." : timeline.Summary + $" Generated {result.Generated} keys. " + string.Join(" ", result.Warnings)));
            }
            catch
            {
                RestoreSnapshot(oldCommands, audioOffset: oldOffset, primaryAudio: oldAudio);
                _undoStack.Clear(); _undoStack.AddRange(undo); _redoStack.Clear(); _redoStack.AddRange(redo); throw;
            }
        }
        catch (Exception error) { MessageBox.Show(this, error.Message, "Special Audio Replace", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
}
