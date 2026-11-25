using System.Windows;

namespace ServoAnimator;

public partial class MainWindow
{
    private WebcamOverdubPlan _webcamOverdub;
    private WebcamOverdubPreview _webcamOverdubView;
    private readonly Dictionary<ServoNames, double> _webcamNeckTargets = new();
    private ServoSpeed _webcamNeckSpeed;

    private RobotHeadView OverdubSourceView => (_urdfUndocked ? _head?.HeadView : EmbeddedHeadView) ?? EmbeddedHeadView;

    private bool PrepareWebcamOverdub()
    {
        CloseWebcamOverdubView();
        _webcamOverdub = null;
        if (!_recordingWindow.IsWebcamOverdub) return true;
        if (LiveDrive)
        {
            _recordingWindow.SetStatus("Turn off Drive HW before opening the webcam overdub preview.");
            return false;
        }
        var source = OverdubSourceView;
        if (source == null || !source.CollisionModelAvailable || !source.UrdfDriveEnabled || source.PoseEditorActive)
        {
            _recordingWindow.SetStatus("Load the URDF model, enable URDF Drive, and leave pose editing before recording.");
            return false;
        }
        double end = _recordingCursor + _recordingWindow.ArmedDuration;
        _webcamOverdub = WebcamOverdubPlan.Prepare(_recordingWindow, _doc.Commands, _recordingCursor,
            Math.Min(end, Math.Max(_recordingCursor, TimelineDuration)), _recordingWindow, _faceSettings);
        if (_webcamOverdub == null) return false;
        var sourceKind = _recordingWindow.Source;
        var window = new WebcamOverdubPreview(_webcamOverdub.SourceSummary, () => OverdubSourceView,
            () => _cursorTime, _recordingCursor, end, TimelineDuration, ToggleFaceRecording,
            _recordingWindow.UsesController ? sourceKind : null, ShowOverdubControllerView) { Owner = this };
        _webcamOverdubView = window;
        window.Closed += (_, _) =>
        {
            if (_webcamOverdubView != window) return;
            _webcamOverdubView = null;
            if (RecordingController) StopControllerRecording("Webcam overdub stopped.");
            else { _webcamOverdub = null; _recordingWindow?.ShowConfiguration(); }
        };
        return true;
    }

    private void ShowOverdubControllerView(ControllerKind kind)
    {
        if (!_floatingControllers.TryGetValue(kind, out var entry)) ToggleFloatingController(kind);
        else entry.Window.Activate();
    }

    private void CloseWebcamOverdubView()
    {
        var window = _webcamOverdubView;
        _webcamOverdubView = null;
        window?.Close();
    }

    private void SeedWebcamNeckTargets(RobotPoseSnapshot pose)
    {
        _webcamNeckSpeed = ServoSpeed.NoChange;
        if (_webcamOverdub == null) return;
        foreach (var servo in WebcamOverdubPlan.Axes.Where(_webcamOverdub.TakesLogicalAxis))
            SetWebcamLogicalNeck(servo, servo == ServoNames.NeckNodUp ? pose.NeckNod : pose.NeckTilt, ServoSpeed.NoChange);
    }

    private bool TrySetWebcamLogicalNeck(string target, double value, ServoSpeed speed)
    {
        if (!RecordingController || _webcamOverdub == null ||
            !ControllerTargets.TryServo(target, out var servo, out var child) || child.HasValue ||
            !_webcamOverdub.TakesLogicalAxis(servo)) return false;
        SetWebcamLogicalNeck(servo, value, speed);
        return true;
    }

    private void SetWebcamLogicalNeck(ServoNames servo, double value, ServoSpeed speed)
    {
        var range = ServoCommand.RangeFor(servo);
        int position = (int)Math.Round(Math.Clamp(value, range.Min, range.Max));
        _webcamNeckTargets[servo] = position;
        if (speed != ServoSpeed.NoChange) _webcamNeckSpeed = speed;
        _controllerTake.Accept(new ServoCommand { Servo = servo, NumericValue = position, Speed = speed });
    }

    private double WebcamOverdubNeckValue(ServoNames servo, double existing) =>
        RecordingController && _webcamNeckTargets.TryGetValue(servo, out double value) ? value : existing;
}
