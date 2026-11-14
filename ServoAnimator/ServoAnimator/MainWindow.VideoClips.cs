using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace ServoAnimator;

public partial class MainWindow
{
    private readonly VideoClipAudio _videoAudio = new();
    /// <summary>Parsed Video Clip commands, rebuilt with the audio clips after every edit. Start reads the live command offset.</summary>
    private List<VideoClipSpan> _videoSpans = new();
    private List<VideoClipVisual> _videoClipVisuals = new();
    // Videos whose sound could not be decoded (none, or an unsupported codec): not retried on every refresh.
    private readonly HashSet<string> _silentVideos = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Undo/redo restores a state the user already had, so the overlap guard must not undo it again.</summary>
    private bool _restoringHistory;

    /// <summary>Rebuild the video bars (name, span, sound envelope) from the document's Video Clip commands.</summary>
    private void RefreshVideoClips()
    {
        _videoSpans = VideoClipTimeline.Clips(_doc.Commands);
        var visuals = new List<VideoClipVisual>();
        foreach (var span in _videoSpans)
        {
            string path = span.Settings.FullPath(ConfigRoot);
            bool exists = File.Exists(path);
            float[] min = null, max = null;
            if (exists && !_silentVideos.Contains(path))
            {
                try { (min, max, _) = BuildPeaks(path); }
                catch { _silentVideos.Add(path); }
            }
            visuals.Add(new VideoClipVisual
            {
                Command = span.Command, Name = Path.GetFileName(span.Settings.Path), Duration = span.Settings.PlayedSeconds,
                SourceStart = span.Settings.SkipSeconds, PeakMin = min, PeakMax = max, Missing = !exists,
                Muted = span.Settings.Muted || span.Settings.Volume <= 0,
            });
        }
        _videoClipVisuals = visuals;
        Waveform.VideoClips = visuals.Where(v => !_doc.HiddenCommandGroups.Contains(v.Command.GroupId)).ToList();
    }

    private void ApplyVideoTimeline()
    {
        var state = VideoClipTimeline.At(_videoSpans, _cursorTime);
        if (_exportingFrames) { ForEachHeadView(view => view.SetVideoForExport(state != null)); return; }
        double rate = IsRunning ? _playbackClock?.Rate ?? 1 : 0;
        ForEachHeadView(view => view.SetTimelineVideo(state, IsRunning, rate, ConfigRoot));
    }

    private void SyncVideoAudio(double t) =>
        _videoAudio.Sync(IsRunning ? VideoClipTimeline.At(_videoSpans, t) : null, ConfigRoot, t, IsRunning, _playbackClock?.Rate ?? 1, _playbackVolume);

    /// <summary>Insert a video at the cursor: it is copied into Configuration\VideoMedia and refused if it would overlap
    /// another clip. The length is read first so a refused clip is never copied.</summary>
    private void InsertVideoClipAtCursor()
    {
        if (!ApplyOpenCommandEditor()) return;
        if (IsRunning) PausePlayback();
        string patterns = string.Join(";", TvShowCommand.VideoExtensions.Select(e => "*" + e));
        var dlg = new OpenFileDialog
        {
            Title = "Insert video clip on the timeline",
            Filter = $"Videos ({patterns})|{patterns}|All files (*.*)|*.*",
            InitialDirectory = _folders?.ProjectFolderOrDefault ?? "",
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            if (!TvShowCommand.VideoExtensions.Contains(Path.GetExtension(dlg.FileName).ToLowerInvariant()))
                throw new InvalidOperationException("Video Clip supports " + string.Join(" ", TvShowCommand.VideoExtensions) + ".");
            double seconds = VideoClipCommand.ProbeSeconds(dlg.FileName);
            double start = ServoCommand.TimeKey(_cursorTime);
            if (VideoClipTimeline.Blocking(_doc.Commands, start, seconds) is { } blocking)
                throw new InvalidOperationException($"This {seconds:F3} s video would overlap {blocking.Name} " +
                    $"({blocking.Start:F3}–{blocking.End:F3} s). Move the cursor to a free part of the timeline, or shorten or move the other clip first.");
            string relative = LoadingWindow.Wait(this, Task.Run(() => VideoClipCommand.Import(ConfigRoot, dlg.FileName)), "Copying the video into Configuration…");
            var settings = new VideoClipCommand { Path = relative, SourceSeconds = seconds };
            settings.Validate();
            PushUndo($"Insert video {Path.GetFileName(dlg.FileName)} at {start:F3} s");
            _doc.Commands.Add(new ServoCommand
            {
                Servo = ServoNames.VideoClip, OffsetSeconds = start, TextValue = settings.ToJson(), Reason = settings.Label(),
            });
            if (RefreshAfterEdit())
                ShowStatus($"Video clip {Path.GetFileName(dlg.FileName)}: {start:F3}–{start + seconds:F3} s");
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Insert Video Clip", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void ShowVideoClipMenu(VideoClipVisual clip)
    {
        var menu = new ContextMenu { PlacementTarget = Waveform };
        void Item(string header, Action action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }
        Item("Video clip settings…", () => EditVideoClip(clip));
        Item($"Move cursor to clip start ({clip.Start:F3} s)", () => SetCursor(clip.Start));
        menu.Items.Add(new Separator());
        Item("Delete video clip", () => DeleteVideoClip(clip));
        menu.IsOpen = true;
    }

    private void EditVideoClip(VideoClipVisual clip)
    {
        if (!ApplyOpenCommandEditor()) return;
        if (IsRunning) PausePlayback();
        VideoClipCommand current;
        try { current = VideoClipCommand.Read(clip.Command.TextValue); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Video Clip", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        var form = new CommandSettingsWindow("Video Clip",
            $"{Path.GetFileName(current.Path)} ({current.SourceSeconds:F3} s) plays behind the robot from {clip.Start:F3} s, covering the backdrop; a 3D stage is hidden while it plays. " +
            "Its sound mixes with the timeline audio and never moves the mouth.") { Owner = this };
        var fit = form.AddChoice("Fit", new[] { "Contain (whole video)", "Cover (fill, crop edges)", "Stretch" }, (int)current.Fit,
            "Contain leaves bars in the backdrop's colour where the shapes differ");
        var volume = form.AddNumber("Volume", current.Volume, 0, 100, "Video sound in percent; Playback Volume also applies");
        var muted = form.AddCheck("Muted", current.Muted, "Silence the video's sound");
        var skip = form.AddNumber("Skip to", current.SkipSeconds, 0, current.SourceSeconds, "Video position shown at the clip's start, in seconds");
        var end = form.AddText("End at", current.EndSeconds?.ToString("0.###", CultureInfo.CurrentCulture) ?? "",
            $"Video position where the clip ends, in seconds (up to {current.SourceSeconds:0.###}). Leave empty to play to the end of the video.");
        form.Apply = () =>
        {
            double? endAt = null;
            string text = end();
            if (text.Length > 0)
            {
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out double value) &&
                    !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                    throw new InvalidOperationException("End at must be a number of seconds, or empty.");
                endAt = value;
            }
            var next = current.Clone();
            next.Fit = (TvFit)fit(); next.Volume = volume(); next.Muted = muted(); next.SkipSeconds = skip(); next.EndSeconds = endAt;
            next.Validate();
            if (VideoClipTimeline.Blocking(_doc.Commands, clip.Start, next.PlayedSeconds, clip.Command) is { } blocking)
                throw new InvalidOperationException($"The clip would then run to {clip.Start + next.PlayedSeconds:F3} s and overlap {blocking.Name} " +
                    $"({blocking.Start:F3}–{blocking.End:F3} s). Choose an earlier End at or a later Skip to.");
            return next.ToJson();
        };
        if (form.ShowDialog() != true || form.Result == clip.Command.TextValue) return;
        PushUndo($"Video clip settings {clip.Name}");
        clip.Command.TextValue = form.Result;
        RefreshAfterEdit();
    }

    private void DeleteVideoClip(VideoClipVisual clip)
    {
        if (!ApplyOpenCommandEditor()) return;
        if (IsRunning) PausePlayback();
        PushUndo($"Delete video {clip.Name}");
        _doc.Commands.Remove(clip.Command);
        RefreshAfterEdit();
        ShowStatus($"Deleted video clip {clip.Name}. Its copy in {VideoClipCommand.MediaFolder} is kept.");
    }

    /// <summary>A bar drag was released. Only this clip moves (other commands stay put), and never onto another clip;
    /// the bar drag already stops at its neighbours.</summary>
    private void Waveform_VideoClipMoved(VideoClipVisual clip, double start)
    {
        start = Math.Max(0, ServoCommand.TimeKey(start));
        if (start == clip.Command.OffsetSeconds) return;
        if (!ApplyOpenCommandEditor()) { RefreshVideoClips(); return; }
        if (VideoClipTimeline.Blocking(_doc.Commands, start, clip.Duration, clip.Command) is { } blocking)
        {
            ShowStatus($"Video clips can't overlap: {blocking.Name} is at {blocking.Start:F3}–{blocking.End:F3} s.");
            RefreshVideoClips(); Waveform.InvalidateVisual(); return;
        }
        if (IsRunning) PausePlayback();
        PushUndo($"Move video {clip.Name} to {start:F3} s");
        clip.Command.OffsetSeconds = start;
        RefreshAfterEdit();
    }

    /// <summary>Any other edit (marker drags, Edit Commands, paste, library insert) that makes two clips overlap is
    /// undone with a message, as long as the state before it was clear of overlaps. Returns false when it undid the edit.</summary>
    private bool GuardVideoClipOverlap()
    {
        if (VideoClipTimeline.FindOverlap(_doc.Commands) is not { } overlap) return true;
        bool introducedByThisEdit = !_restoringHistory && _undoStack.Count > 0 && VideoClipTimeline.FindOverlap(_undoStack[^1].Commands) == null;
        if (!introducedByThisEdit)
        {
            ShowStatus("Video clips overlap: " + VideoClipTimeline.Describe(overlap) + ". The later clip plays over the earlier one; move one apart.");
            return true;
        }
        if (IsRunning) PausePlayback();
        var before = _undoStack[^1];
        _undoStack.RemoveAt(_undoStack.Count - 1);
        RestoreSnapshot(before.Commands, refresh: false, audioOffset: before.AudioOffset, splines: before.Splines, primaryAudio: before.PrimaryAudio, backgroundFile: before.BackgroundFile);
        MessageBox.Show(this, "Video clips can't overlap, so this change was undone:\n\n" + VideoClipTimeline.Describe(overlap),
            "Video Clip", MessageBoxButton.OK, MessageBoxImage.Information);
        return false;
    }
}
