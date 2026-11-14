namespace ServoAnimator;

public partial class MainWindow
{
    private RobotHeadView ApiUrdfView => (_urdfUndocked ? _head?.HeadView : EmbeddedHeadView) ?? EmbeddedHeadView;
    private object EditorUrdfState() => new
    {
        modelFile = ApiUrdfView?.LoadedModelFile, framing = ApiUrdfView?.CaptureCurrentFraming(),
        background = ApiUrdfView?.CaptureBackground(), stage = ApiUrdfView?.CaptureStage(), fallApart = FallApartState(), tv = ApiUrdfView?.TvInfo(),
        trackForward = new { speedCmPerSecond = _rows.FirstOrDefault(r => r.Servo == ServoNames.TrackForward)?.Value ?? 0, controllerSpeedCmPerSecond = _controllerTrackSpeed, min = -500, max = 500 },
        stageUnits = "metres; +X forward at heading 0, +Y robot left; positive heading turns left; timeline StageX/Y use centimetres"
    };
    private object HandleBackdropApi(EditorApiRequest request, string payload)
    {
        var view = ApiUrdfView ?? throw new InvalidOperationException("Open a URDF view first.");
        var commands = new List<ServoCommand>();
        if (request.Method == "move_robot")
        {
            var stage = view.CaptureStage() ?? throw new InvalidOperationException("Load a 3D URDF backdrop first.");
            double seconds = request.Seconds ?? 0, turn = request.TurnDegrees ?? 0, distance = request.ForwardMeters ?? 0;
            if (!double.IsFinite(seconds) || seconds < 0 || seconds + _cursorTime > 86400 || !double.IsFinite(turn) || !double.IsFinite(distance))
                throw new InvalidOperationException("Use finite motion values and a non-negative duration.");
            double heading = stage.HeadingDegrees + turn;
            var trackOffset = _trackPath?.At(_cursorTime) ?? (X: 0d, Y: 0d);
            double baseX = stage.RobotX - trackOffset.X, baseY = stage.RobotY - trackOffset.Y;
            var points = new[] { (ServoNames.URDFStageX, baseX * 100, (baseX + distance * Math.Cos(heading * Math.PI / 180)) * 100),
                (ServoNames.URDFStageY, baseY * 100, (baseY + distance * Math.Sin(heading * Math.PI / 180)) * 100),
                (ServoNames.URDFStageHeading, stage.HeadingDegrees, heading) };
            foreach (var (servo, start, finish) in points)
            {
                var range = ServoCommand.RangeFor(servo);
                if (!double.IsFinite(finish) || start < range.Min || start > range.Max || finish < range.Min || finish > range.Max) throw new InvalidOperationException("Stage motion exceeds command range: " + servo);
                var anchor = _doc.Commands.FirstOrDefault(c => c.Servo == servo && ServoCommand.TimeKey(c.OffsetSeconds) == ServoCommand.TimeKey(_cursorTime));
                if (seconds > 0 && anchor != null && (anchor.Disable || anchor.NumericValue != (int)Math.Round(start)))
                    throw new InvalidOperationException("The start command conflicts with the current stage position: " + servo);
                if (seconds > 0 && !_doc.Commands.Any(c => c.Servo == servo && ServoCommand.TimeKey(c.OffsetSeconds) == ServoCommand.TimeKey(_cursorTime)))
                    commands.Add(new() { Servo = servo, OffsetSeconds = ServoCommand.TimeKey(_cursorTime), NumericValue = (int)Math.Round(start) });
                commands.Add(new() { Servo = servo, OffsetSeconds = ServoCommand.TimeKey(_cursorTime + seconds), NumericValue = (int)Math.Round(finish) });
            }
        }
        else
        {
            var backdrop = request.Method == "set_backdrop" ? request.Backdrop ?? throw new InvalidOperationException("Supply backdrop settings.")
                : new BackdropCommand { Background = view.CaptureBackground(), Framing = request.Framing ?? throw new InvalidOperationException("Supply framing settings.") };
            if (request.Method == "set_urdf_framing" && view.CaptureStage() is { } currentStage) backdrop.Background.Stage = currentStage;
            if (!string.IsNullOrWhiteSpace(request.BackgroundImageSource))
                backdrop.Background.BackgroundImage = UrdfPresentationSettings.ImportImage(ConfigRoot, request.BackgroundImageSource);
            backdrop.Background?.EnsureLocalImage(ConfigRoot);
            backdrop.Validate(ConfigRoot);
            view.PrepareBackdrop(backdrop.Background);
            commands.Add(new() { Servo = ServoNames.LoadBackdrop, TextValue = backdrop.ToJson(), OffsetSeconds = ServoCommand.TimeKey(_cursorTime), Reason = "Load Scene" });
        }
        if (CommandConflicts.Find(_doc.Commands.Concat(commands)).Count != 0) throw new InvalidOperationException("A command already occupies that target/time. Nothing changed.");
        var before = Snapshot(); var previousSplines = SplineServosEnabled();
        var previousUndo = _undoStack.ToArray(); var previousRedo = _redoStack.ToArray();
        try
        {
            PushUndo("Codex: " + request.Method); _doc.Commands.AddRange(commands);
            if (request.Method == "move_robot" && request.Seconds > 0)
                ApplySplineSettings(previousSplines.Concat(commands.Select(c => c.Servo)));
            RefreshAfterEdit();
        }
        catch
        {
            _doc.Commands = before; ApplySplineSettings(previousSplines);
            _undoStack.Clear(); _undoStack.AddRange(previousUndo);
            _redoStack.Clear(); _redoStack.AddRange(previousRedo); RefreshAfterEdit(); throw;
        }
        _apiEditGeneration++;
        object result = new { ok = true, requestId = request.RequestId, inserted = commands.Count, state = EditorApiState() };
        _apiReceipts.Add(request.RequestId, (payload, result)); return result;
    }
}
