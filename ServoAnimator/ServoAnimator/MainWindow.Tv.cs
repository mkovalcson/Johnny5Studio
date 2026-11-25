namespace ServoAnimator;

public partial class MainWindow
{
    private readonly TvFrameBuffer _tvFrameBuffer = new();
    private ServoCommand[] _tvPreloadSource;
    private (double Time, TvShowCommand Show)[] _tvPreloadCues = Array.Empty<(double, TvShowCommand)>();

    private (double Time, TvShowCommand Show) UpcomingTvVideo()
    {
        if (!ReferenceEquals(_tvPreloadSource, _orderedCommands))
        {
            _tvPreloadSource = _orderedCommands;
            var cues = new List<(double, TvShowCommand)>();
            foreach (var command in _orderedCommands)
            {
                if (command.Disable || command.Servo != ServoNames.TvShow) continue;
                try
                {
                    var show = TvShowCommand.Read(command.TextValue);
                    if (show.IsVideo) cues.Add((command.OffsetSeconds, show));
                }
                catch (Exception) { /* Ignore invalid cues just as TV playback does. */ }
            }
            _tvPreloadCues = cues.ToArray();
        }
        foreach (var cue in _tvPreloadCues)
            if (cue.Time > _cursorTime) return cue;
        return default;
    }

    private void ApplyTvTimeline()
    {
        // A video clip hides the 3D stage and its TV, so TV commands rest while one plays.
        var state = VideoClipTimeline.At(_videoSpans, _cursorTime) != null ? TvState.Inactive : TvTimeline.StateAt(_doc.Commands, _cursorTime);
        var audible = ApiUrdfView;
        double rate = IsRunning ? _playbackClock?.Rate ?? 1 : 0;
        var next = UpcomingTvVideo();
        var current = state.Show?.IsVideo == true ? state.Show : null;
        const double preloadLeadSeconds = 4;
        var upcoming = current != null
            ? (state.OverlayOpacity > 0 ? null : current)
            : (next.Time - _cursorTime <= preloadLeadSeconds ? next.Show : null);
        string root = ConfigRoot;
        _tvFrameBuffer.Prepare(current ?? next.Show, root);
        var pictures = current == null ? null : _tvFrameBuffer.Frames;
        bool waitingForPictures = current != null && _tvFrameBuffer.IsPending;
        ForEachHeadView(view =>
        {
            view.SetTvFrameBuffer(pictures, waitingForPictures);
            view.SetTimelineTv(state, IsRunning, rate, root, ReferenceEquals(view, audible));
            if (upcoming != null) view.PreloadTvVideo(upcoming, root);
        });
    }

    private object HandleTvApi(EditorApiRequest request, string payload)
    {
        var view = ApiUrdfView ?? throw new InvalidOperationException("Open a URDF view first.");
        ServoCommand command; double end = _cursorTime;
        switch (request.Method)
        {
            case "insert_tv_front":
            {
                var front = new TvFrontCommand { Seconds = request.Seconds ?? 3, Fill = request.Fill ?? .85, Screen = string.IsNullOrWhiteSpace(request.Screen) ? TvFrontCommand.DefaultScreen : request.Screen };
                front.Validate();
                if (!view.HasTvScreen(front.Screen)) throw new InvalidOperationException("The current backdrop has no TV screen link '" + front.Screen + "'. Load a 3D URDF backdrop with a TV first.");
                command = new ServoCommand { Servo = ServoNames.TvFront, TextValue = front.ToJson(), Reason = "URDF TV Front" };
                end += front.Seconds; break;
            }
            case "insert_tv_return":
            {
                var back = new TvReturnCommand { Seconds = request.Seconds ?? 3 }; back.Validate();
                command = new ServoCommand { Servo = ServoNames.TvReturn, TextValue = back.ToJson(), Reason = "URDF TV Return" };
                end += back.Seconds; break;
            }
            default:
            {
                var show = new TvShowCommand
                {
                    StartSeconds = request.StartSeconds ?? 0, DurationSeconds = request.DurationSeconds ?? 0,
                    Loop = request.Loop ?? false, Volume = request.Volume ?? 100,
                    Fit = string.IsNullOrWhiteSpace(request.Fit) ? TvFit.Contain
                        : Enum.TryParse<TvFit>(request.Fit, true, out var fit) ? fit : throw new InvalidOperationException("fit must be contain, cover or stretch.")
                };
                show.Validate();
                if (!string.IsNullOrWhiteSpace(request.MediaPath))
                {
                    string source = System.IO.Path.IsPathRooted(request.MediaPath) ? request.MediaPath : System.IO.Path.Combine(ConfigRoot, request.MediaPath);
                    show.Path = TvShowCommand.Import(ConfigRoot, source); show.Validate();
                }
                if (show.IsVideo) end += show.DurationSeconds;
                command = new ServoCommand { Servo = ServoNames.TvShow, TextValue = show.ToJson(), Reason = show.IsBlank ? "TV Show (blank)" : "TV Show " + System.IO.Path.GetFileName(show.Path) };
                break;
            }
        }
        command.OffsetSeconds = ServoCommand.TimeKey(_cursorTime);
        if (!string.IsNullOrWhiteSpace(request.Description)) command.Reason = request.Description;
        if (end > 86400) throw new InvalidOperationException("The command would end after the 24-hour timeline limit.");
        if (CommandConflicts.Find(_doc.Commands.Append(command)).Count != 0) throw new InvalidOperationException("A command already occupies that target/time. Nothing changed.");
        var previousUndo = _undoStack.ToArray(); var previousRedo = _redoStack.ToArray();
        try { PushUndo("Codex: " + request.Method); _doc.Commands.Add(command); RefreshAfterEdit(); }
        catch
        {
            _doc.Commands.Remove(command);
            _undoStack.Clear(); _undoStack.AddRange(previousUndo);
            _redoStack.Clear(); _redoStack.AddRange(previousRedo); RefreshAfterEdit(); throw;
        }
        _apiEditGeneration++;
        object result = new { ok = true, requestId = request.RequestId, inserted = 1, text = command.TextValue, endSeconds = ServoCommand.TimeKey(end), state = EditorApiState() };
        _apiReceipts.Add(request.RequestId, (payload, result)); return result;
    }
}
