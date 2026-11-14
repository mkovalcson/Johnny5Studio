namespace ServoAnimator;

public partial class MainWindow
{
    private sealed record BackdropTimelineState(string Key, UrdfFraming Before, BackdropCommand Preset);
    private readonly Dictionary<RobotHeadView, BackdropTimelineState> _backdropTimeline = new();
    private readonly Dictionary<RobotHeadView, SceneViewState> _timelineSceneModels = new();
    private void RestoreTimelineSceneModels()
    {
        RestoreSceneViews(_timelineSceneModels.Values);
        _timelineSceneModels.Clear();
    }
    private object _backdropDocument;
    private double _activeBackdropTime;
    private TrackForwardPath _trackPath;
    private (double Start, double Heading, double Spin, double Rotate, double CasterRadius, double CasterTrail) _trackPathContext;
    private void ApplyBackdropTimeline()
    {
        if (!ReferenceEquals(_backdropDocument, _doc))
        {
            RestoreTimelineSceneModels();
            foreach (var entry in _backdropTimeline)
            { entry.Key.SetTimelineBackground(null); entry.Key.ApplyFraming(entry.Value.Before); }
            RestoreSequenceModels();
            _trackPath = null;
            _backdropTimeline.Clear(); _urdfTimelineBaselines.Clear(); _backdropDocument = _doc;
            _sequenceBackgroundFile = null;
        }
        if (_sequenceBackgroundFile != (_doc.UrdfBackgroundFile ?? "") && _timelineSceneModels.Count > 0)
        {
            RestoreTimelineSceneModels(); _backdropTimeline.Clear(); _urdfTimelineBaselines.Clear();
        }
        RefreshSequenceBackground();
        var command = _doc.Commands.Where(c => c.Servo == ServoNames.LoadBackdrop && !c.Disable && c.OffsetSeconds <= _cursorTime + 1e-9)
            .OrderBy(c => c.OffsetSeconds).LastOrDefault();
        _activeBackdropTime = command?.OffsetSeconds ?? double.NegativeInfinity;
        string key = command == null ? (_sequenceBackground == null ? null : "sequence|" + _sequenceBackgroundRevision + "|" + _sequenceBackgroundFile) : command.OffsetSeconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "|" + command.TextValue;
        ForEachHeadView(view =>
        {
            _backdropTimeline.TryGetValue(view, out var previous);
            if (previous?.Key != key)
            {
                var before = previous?.Before ?? (_sequenceModelBaselines.TryGetValue(view, out var modelBaseline) ? modelBaseline.Frame : view.CaptureCurrentFraming());
                var rollback = new SceneViewState(view, view.LoadedModelFile, view.CurrentModelScene,
                    view.CaptureCurrentFraming(), view.CaptureTimelineBackground());
                _timelineSceneModels.TryGetValue(view, out var priorModelBaseline);
                try
                {
                    var stored = command == null ? _sequenceBackground : BackdropCommand.Read(command.TextValue);
                    var scene = !string.IsNullOrWhiteSpace(stored?.SceneFile) ? SavedSceneCommand.Read(ConfigRoot, stored.SceneFile) : null;
                    var backdrop = scene?.Settings ?? stored;
                    backdrop?.Validate(ConfigRoot);
                    // Prepare assets before replacing a visible model. Reads happen only at command transitions.
                    var model = scene == null ? null : string.Equals(view.LoadedModelFile, scene.RobotModelFile, StringComparison.OrdinalIgnoreCase)
                        ? view.CurrentModelScene : UrdfScene.Load(scene.RobotModelFile);
                    if (backdrop != null) view.PrepareBackdrop(backdrop.Background);
                    if (scene != null)
                    {
                        if (!_timelineSceneModels.ContainsKey(view))
                            _timelineSceneModels[view] = new(view, view.LoadedModelFile, view.CurrentModelScene, before, view.CaptureTimelineBackground());
                        if (!ReferenceEquals(view.CurrentModelScene, model)) view.InstallModel(scene.RobotModelFile, model, backdrop.Framing);
                    }
                    else if (_timelineSceneModels.Remove(view, out var original))
                    {
                        if (original.Model != null && !ReferenceEquals(view.CurrentModelScene, original.Model))
                            view.InstallModel(original.Path, original.Model, before);
                    }
                    if (backdrop == null)
                    {
                        view.SetTimelineBackground(null); view.ApplyFraming(before);
                        _backdropTimeline.Remove(view); previous = null;
                    }
                    else
                    {
                        view.SetTimelineBackground(backdrop.Background);
                        view.ApplyFraming(backdrop.Framing ?? before);
                        previous = new(key, before, backdrop); _backdropTimeline[view] = previous;
                    }
                    _trackPath = null;
                    _urdfTimelineBaselines.Remove(view);
                }
                catch (Exception ex)
                {
                    try { RestoreSceneViews(new[] { rollback }); } catch { /* Retain the original load error. */ }
                    if (priorModelBaseline == null) _timelineSceneModels.Remove(view);
                    else _timelineSceneModels[view] = priorModelBaseline;
                    previous = new(key, before, null); _backdropTimeline[view] = previous;
                    ShowStatus("Load Scene: " + ex.Message);
                }
            }
            if (command != null && previous?.Preset == null) return;
            var stage = view.CaptureStage();
            if (stage == null) return;
            // Evaluate from the cached preset, so backwards seeks restore the origin without parsing per frame.
            var initial = previous?.Preset?.Background.Stage ?? view.CaptureBackground().Stage;
            double Value(ServoNames servo, double fallback) => _rows.FirstOrDefault(r => r.Servo == servo && r.Offset.HasValue &&
                r.Offset.Value >= _activeBackdropTime)?.Value ?? fallback;
            foreach (var camera in UrdfCameraCommands.StageChannels)
                UrdfCameraCommands.Write(stage, camera, Value(camera, UrdfCameraCommands.Read(initial, camera)));
            view.ApplyStageCameraSettings(stage);
            var travel = TrackPathFor(view, initial, Math.Max(0, _activeBackdropTime)).At(_cursorTime);
            if(!VrRecordingOwns(ServoNames.URDFStageX))view.ApplyStageMotion(Value(ServoNames.URDFStageX, initial.RobotX * 100) / 100 + travel.X,
                Value(ServoNames.URDFStageY, initial.RobotY * 100) / 100 + travel.Y, Value(ServoNames.URDFStageHeading, initial.HeadingDegrees));
        });
    }
    private TrackForwardPath TrackPathFor(RobotHeadView view, UrdfStageSettings initial, double start)
    {
        var framingOrigin = _urdfTimelineBaselines.TryGetValue(view, out var storedFraming) ? storedFraming : view.CaptureCurrentFraming();
        var context = (start, initial.HeadingDegrees, framingOrigin.SpinDegrees, framingOrigin.RotateDegrees, view.CasterTurnRadius, view.CasterTrail);
        if (_trackPath == null || _trackPathContext != context)
        {
            _trackPathContext = context;
            SplineCurve Curve(ServoNames servo) => _rows.First(r => r.Servo == servo).SplineEnabled ? _splineCurveIndex.GetValueOrDefault(servo) : null;
            _trackPath = new TrackForwardPath(_doc.Commands, start, initial.HeadingDegrees,
                Curve(ServoNames.TrackForward), Curve(ServoNames.URDFStageHeading), framingOrigin.SpinDegrees, framingOrigin.RotateDegrees,
                Curve(ServoNames.URDFSpin), Curve(ServoNames.URDFRotate), view.CasterTurnRadius, view.CasterTrail);
        }
        return _trackPath;
    }

}
