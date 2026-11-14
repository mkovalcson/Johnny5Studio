namespace ServoAnimator;

public partial class MainWindow
{
    private bool _editingUrdfFraming;
    private readonly Dictionary<RobotHeadView, UrdfFraming> _urdfTimelineBaselines = new();

    private void ApplyUrdfTimelinePosition()
    {
        if (_editingUrdfFraming || _meetingActive || !PlaybackOutputAllowed(false)) return;
        ApplyBackdropTimeline();
        ApplyVideoTimeline();
        ApplyFallApartTimeline();
        ApplyTvTimeline();
        ApplyBeamTimeline();
        ApplyLcdTimeline();
        ApplyLaserTimeline();
        var active = _rows.Where(r => ServoCommand.IsUrdfOnly(r.Servo) && !BodyControlCatalog.IsBody(r.Servo) && !UrdfStageSettings.IsMotion(r.Servo) && !UrdfCameraCommands.IsStageCamera(r.Servo) && r.Servo != ServoNames.LoadBackdrop && r.Servo != ServoNames.FallApart && r.Servo is not (ServoNames.TvFront or ServoNames.TvShow or ServoNames.TvReturn or ServoNames.UrdfBeam or ServoNames.LcdShow or ServoNames.HideLaser or ServoNames.ToolboxSwap or ServoNames.FireLaser) && r.Servo != ServoNames.TrackForward && r.Offset.HasValue && r.Offset.Value >= _activeBackdropTime).ToArray();
        ForEachHeadView(view =>
        {
            var frame = view.CaptureCurrentFraming();
            if (!_urdfTimelineBaselines.TryGetValue(view, out var baseline))
            {
                if (active.Length == 0) return;
                baseline = view.CaptureCurrentFraming();
                _urdfTimelineBaselines[view] = baseline;
            }
            double Value(ServoNames name, double fallback) => active.FirstOrDefault(r => r.Servo == name)?.Value ?? fallback;
            double horizontal = Value(ServoNames.URDFSlide, baseline.HorizontalPercent);
            double vertical = Value(ServoNames.URDFVertical, baseline.VerticalPercent);
            double spin = Value(ServoNames.URDFSpin, baseline.SpinDegrees);
            double rotate = Value(ServoNames.URDFRotate, baseline.RotateDegrees);
            double yaw = Value(ServoNames.URDFCameraYaw, baseline.Yaw * 180 / Math.PI) * Math.PI / 180;
            double pitch = Value(ServoNames.URDFCameraPitch, baseline.Pitch * 180 / Math.PI) * Math.PI / 180;
            double distance = 115 / Value(ServoNames.URDFZoom, 115 / baseline.Distance);
            if (frame.Yaw != yaw || frame.Pitch != pitch || frame.HorizontalPercent != horizontal || frame.VerticalPercent != vertical ||
                frame.SpinDegrees != spin || frame.RotateDegrees != rotate || Math.Abs(frame.Distance - distance) > 1e-10)
            {
                frame.Yaw = yaw; frame.Pitch = pitch;
                frame.HorizontalPercent = horizontal; frame.VerticalPercent = vertical;
                frame.SpinDegrees = spin; frame.RotateDegrees = rotate; frame.Distance = distance;
                view.ApplyFraming(frame);
            }
            if (active.Length == 0) _urdfTimelineBaselines.Remove(view);
        });
        ForEachHeadView(view =>
        {
            bool staged = view.CaptureStage() != null;
            var path = TrackPathFor(view, staged ? view.CaptureBackground().Stage : new UrdfStageSettings(), Math.Max(0, _activeBackdropTime));
            var caster = path.CasterAt(_cursorTime);
            view.SetTimelineTreads(staged ? path.ForwardDistanceAt(_cursorTime) : 0, caster.Angle, caster.Target,caster.Left,caster.Right);
        });
    }

    private bool SaveUrdfPositionAtCursor(UrdfFraming frame, UrdfStageSettings stage = null, bool splined = false)
    {
        if (!ApplyOpenCommandEditor()) return false;
        double time = ServoCommand.TimeKey(_cursorTime);
        var values = new List<(ServoNames Name, double Value)>
        {
            (ServoNames.URDFSlide, frame.HorizontalPercent),
            (ServoNames.URDFVertical, frame.VerticalPercent),
            (ServoNames.URDFZoom, 115 / frame.Distance),
            (ServoNames.URDFSpin, frame.SpinDegrees),
            (ServoNames.URDFRotate, frame.RotateDegrees),
            (ServoNames.URDFCameraYaw, frame.Yaw * 180 / Math.PI),
            (ServoNames.URDFCameraPitch, frame.Pitch * 180 / Math.PI)
        };
        if (stage != null)
        {
            values.Add((ServoNames.URDFStageX, stage.RobotX * 100));
            values.Add((ServoNames.URDFStageY, stage.RobotY * 100));
            values.Add((ServoNames.URDFStageHeading, stage.HeadingDegrees));
            foreach (var name in UrdfCameraCommands.StageChannels)
                if (name != ServoNames.URDFFloorHeight || !stage.DetectFloor)
                    values.Add((name, UrdfCameraCommands.Read(stage, name)));
        }
        // Timeline files store whole-number values; do not overflow a very long live session.
        if (values.Any(p => !double.IsFinite(p.Value) || p.Value < int.MinValue || p.Value > int.MaxValue))
        {
            ShowStatus("This turn count exceeds the timeline's whole-number storage range. Set Spin to an equivalent smaller angle before saving a position.");
            return false;
        }
        PushUndo($"Save URDF position at {time:F3} s");
        foreach (var (name, value) in values)
        {
            var existing = _doc.Commands.LastOrDefault(c => c.Servo == name && ServoCommand.TimeKey(c.OffsetSeconds) == time);
            var command = existing?.Clone() ?? new ServoCommand { Servo = name, OffsetSeconds = time,
                Speed = ServoSpeed.NoChange, Reason = "URDF position" };
            command.NumericValue = (int)Math.Round(value);
            command.ClampToRange();
            _doc.Commands.RemoveAll(c => c.Servo == name && ServoCommand.TimeKey(c.OffsetSeconds) == time);
            _doc.Commands.Add(command);
            if (_rows.FirstOrDefault(r => r.Servo == name) is { } row) row.SplineEnabled = splined;
        }
        _doc.SplineServos = SplineServosEnabled().Select(s => s.ToString()).ToList();
        return RefreshAfterEdit();
    }
}
