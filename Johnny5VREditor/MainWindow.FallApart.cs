namespace ServoAnimator;

public partial class MainWindow
{
    private void ApplyFallApartTimeline() => ForEachHeadView(view =>
    {
        var clip = view.FallApartClip;
        view.SetTimelineFallApart(clip == null ? null : FallApartTimeline.FrameAt(_doc.Commands, _cursorTime, clip.Fps, clip.FrameCount));
    });

    private object FallApartState()
    {
        var clip = ApiUrdfView?.FallApartClip;
        return clip == null ? new { available = false, fps = 0, frameCount = 0, durationSeconds = 0d }
            : new { available = true, fps = clip.Fps, frameCount = clip.FrameCount, durationSeconds = clip.DurationSeconds };
    }

    private object HandleFallApartApi(EditorApiRequest request, string payload)
    {
        var clip = (ApiUrdfView ?? throw new InvalidOperationException("Open a URDF view first.")).FallApartClip
            ?? throw new InvalidOperationException("The loaded robot model has no fall-apart clip (" + FallApartClip.RelativePath + ").");
        var settings = new FallApartCommand { Reverse = request.Reverse ?? false, Speed = request.Speed ?? 1 };
        settings.Validate();
        double end = _cursorTime + FallApartTimeline.DurationSeconds(clip, settings);
        if (end > 86400) throw new InvalidOperationException("The effect would end after the 24-hour timeline limit.");
        var command = new ServoCommand { Servo = ServoNames.FallApart, TextValue = settings.ToJson(), OffsetSeconds = ServoCommand.TimeKey(_cursorTime),
            Reason = request.Description is { Length: > 0 } reason ? reason : settings.Label() };
        if (CommandConflicts.Find(_doc.Commands.Append(command)).Count != 0) throw new InvalidOperationException("A command already occupies that target/time. Nothing changed.");
        var previousUndo = _undoStack.ToArray(); var previousRedo = _redoStack.ToArray();
        try
        {
            PushUndo("Codex: " + request.Method); _doc.Commands.Add(command);
            RefreshAfterEdit();
        }
        catch
        {
            _doc.Commands.Remove(command);
            _undoStack.Clear(); _undoStack.AddRange(previousUndo);
            _redoStack.Clear(); _redoStack.AddRange(previousRedo); RefreshAfterEdit(); throw;
        }
        _apiEditGeneration++;
        object result = new { ok = true, requestId = request.RequestId, inserted = 1, endSeconds = ServoCommand.TimeKey(end), state = EditorApiState() };
        _apiReceipts.Add(request.RequestId, (payload, result)); return result;
    }
}
