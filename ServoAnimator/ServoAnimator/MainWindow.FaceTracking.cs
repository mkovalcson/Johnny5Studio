using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace ServoAnimator;

public partial class MainWindow
{
    private readonly FaceTrackingClient _faceClient = new();
    private FaceTrackingSettings _faceSettings = new();
    private FaceTrackingSettings _savedFaceSettings = new();
    private FaceTrackingWindow _faceWindow;
    private readonly DispatcherTimer _faceTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private bool _faceDriving;
    private long _faceLastPacket = -1, _faceLastGood;
    private readonly Dictionary<string, double> _faceFiltered = new();
    private FaceTrackingSettings ActiveFaceSettings => _faceWindow?.PreviewEnabled == true ? _faceWindow.PreviewSettings : _faceSettings;
    private IEnumerable<RobotControls> FaceSpeedLimitedControls => ActiveFaceSettings.EnforceServoSpeeds && FaceOutputActive
        ? ActiveFaceSettings.OwnedControls.Where(control => !FaceOverrideOwns(control) && UsesFaceSpeedLimit(control))
        : Array.Empty<RobotControls>();

    private bool UsesFaceSpeedLimit(RobotControls control)
    {
        if (!RecordingController || _webcamOverdub == null) return true;
        return _controllerTake.OwnsControl(control) ||
            (NeckMix.IsPiston(control) && !_webcamOverdub.SuppressFacePistonOutput);
    }
    private void RefreshFaceSpeedLimits() => ForEachHeadView(view => view.SetFaceSpeedLimitedControls(FaceSpeedLimitedControls));
    private bool FaceOutputActive => _faceClient.Fresh && (_faceWindow?.PreviewEnabled == true || RecordingController && _recordingWindow.UsesFace || _meetingActive && MeetingUsesFace || _meetingPreviewFace)
        && (_meetingActive || _meetingPreviewFace || ControllerWindowActive || _faceWindow?.IsActive == true);
    private void InitializeFaceTracking()
    {
        try { _faceSettings = FaceTrackingSettings.Load(ConfigRoot); } catch (Exception ex) { ShowStatus("Face mappings: " + ex.Message); }
        _savedFaceSettings = System.Text.Json.JsonSerializer.Deserialize<FaceTrackingSettings>(System.Text.Json.JsonSerializer.Serialize(_faceSettings));
        _faceTimer.Tick += (_, _) => FaceTrackingTick(); _faceTimer.Start();
        Closed += (_, _) => { _faceTimer.Stop(); _faceClient.Dispose(); };
    }
    private void FaceTracking_Click(object sender, RoutedEventArgs e)
    {
        if (_recordingWindow != null) { ShowStatus("Close the recording window before configuring face tracking."); return; }
        if (_faceWindow != null) { _faceWindow.Activate(); return; }
        StopPlayback(); ResetControllerMotion();
        _faceWindow = new FaceTrackingWindow(_faceClient, _faceSettings, _savedFaceSettings) { Owner = this };
        _faceWindow.ApplyRequested = settings =>
        {
            _faceSettings = System.Text.Json.JsonSerializer.Deserialize<FaceTrackingSettings>(System.Text.Json.JsonSerializer.Serialize(settings));
            _faceFiltered.Clear(); _faceLastPacket = -1;
            RefreshFaceSpeedLimits();
        };
        _faceWindow.PreviewChanged = () =>
        {
            _faceFiltered.Clear(); _faceLastPacket = -1;
            RefreshFaceSpeedLimits();
        };
        _faceWindow.AppliedOutput = mapping =>
        {
            if (_faceWindow?.PreviewEnabled != true || !_faceClient.Fresh ||
                !ActiveFaceSettings.Mappings.Any(m => m.Enabled && m.Signal == mapping.Signal && m.Target == mapping.Target)) return null;
            string key = FaceTrackingSettings.IsNeckComponent(mapping) ? "NeckMix:" + mapping.Signal : mapping.Target;
            return _faceFiltered.TryGetValue(key, out double output) ? output : null;
        };
        _faceWindow.SaveRequested = settings =>
        {
            settings.Save(ConfigRoot);
            _savedFaceSettings = System.Text.Json.JsonSerializer.Deserialize<FaceTrackingSettings>(System.Text.Json.JsonSerializer.Serialize(settings));
        };
        _faceWindow.Closed += (_, _) =>
        {
            _faceWindow = null; _faceFiltered.Clear(); RefreshFaceSpeedLimits();
            // Rate/mirror can be tried immediately in the camera picture. Closing
            // leaves the last applied configuration active, including its camera mode.
            if (_faceClient.Running && (_faceClient.CameraIndex != _faceSettings.Camera || _faceClient.RequestedFrameRate != _faceSettings.FrameRate || _faceClient.MirrorVideo != _faceSettings.MirrorVideo))
            {
                try { _faceClient.Start(_faceSettings.Camera, _faceSettings.FrameRate, _faceSettings.MirrorVideo); }
                catch (Exception ex) { ShowStatus("Could not restore the applied face-tracking camera: " + ex.Message); }
            }
        };
        _faceWindow.Show();
    }
    private void FaceTrackingTick()
    {
        var interval = TimeSpan.FromSeconds(1d / (_faceClient.Running ? _faceClient.RequestedFrameRate : 30));
        if (_faceTimer.Interval != interval) _faceTimer.Interval = interval;
        RefreshFaceSpeedLimits();
        if (!_faceClient.Fresh)
        {
            _faceFiltered.Clear(); _faceLastPacket = -1;
            foreach (var mapping in _faceSettings.Mappings.Where(m => m.Enabled))
                _controllerPendingHardware.TryRemove(mapping.Target, out _);
            _controllerPendingHardware.TryRemove(FaceTrackingSettings.NeckLeftTarget, out _);
            _controllerPendingHardware.TryRemove(FaceTrackingSettings.NeckRightTarget, out _);
            RefreshControllerOutputPermissions();
            if (RecordingController && _recordingWindow.UsesFace && Environment.TickCount64 - _faceLastGood > 1500)
                StopControllerRecording("Face tracking lost — take stopped. Review or discard it.");
            return;
        }
        _faceLastGood = Environment.TickCount64;
        var packet = _faceClient.Latest;
        if (!FaceOutputActive || packet.TimestampMs == _faceLastPacket) return;
        double dt = _faceLastPacket < 0 ? 1d / _faceClient.RequestedFrameRate : Math.Clamp((packet.TimestampMs - _faceLastPacket) / 1000.0, .001, .2);
        _faceLastPacket = packet.TimestampMs;
        _faceDriving = true;
        try
        {
            RefreshControllerOutputPermissions();
            var settings = _faceWindow?.PreviewEnabled == true ? _faceWindow.PreviewSettings : _faceSettings;
            var signalValues = settings.SignalValues(packet, out _);
            double pitch = 0, roll = 0;
            var neckSpeed = ServoSpeed.NoChange;
            bool mixedNeck = false, neckComplete = true;
            foreach (var mapping in settings.Mappings.Where(m => m.Enabled))
            {
                if (RecordingController && _webcamOverdub?.UsesMapping(mapping) == false) continue;
                if (FaceTrackingSettings.IsNeckComponent(mapping))
                {
                    mixedNeck = true;
                    neckSpeed = mapping.Speed;
                    if (!signalValues.TryGetValue(mapping.Signal, out double angle) || !double.IsFinite(angle))
                    { neckComplete = false; continue; }
                    string key = "NeckMix:" + mapping.Signal;
                    double previous = _faceFiltered.GetValueOrDefault(key);
                    double blend = mapping.SmoothingMs <= 0 ? 1 : 1 - Math.Exp(-dt / (mapping.SmoothingMs / 1000));
                    double component = previous + blend * (mapping.Map(angle) - previous);
                    _faceFiltered[key] = component;
                    if (TrySetWebcamLogicalNeck(mapping.Target, component, mapping.Speed))
                    { mixedNeck = false; continue; }
                    if (mapping.Signal == "headPitch") pitch = component; else roll = component;
                    continue;
                }
                if (!signalValues.TryGetValue(mapping.Signal, out double input) || !double.IsFinite(input)) continue;
                if (mapping.Signal.StartsWith("gaze") && packet.Values.Where(p => p.Key is "eyeBlinkLeft" or "eyeBlinkRight").Any(p => p.Value > .5)) continue;
                double target = mapping.Map(input);
                double before = _faceFiltered.TryGetValue(mapping.Target, out var old) ? old : ControllerCurrentValue(mapping.Target);
                double alpha = mapping.SmoothingMs <= 0 ? 1 : 1 - Math.Exp(-dt / (mapping.SmoothingMs / 1000));
                double value = before + alpha * (target - before); _faceFiltered[mapping.Target] = value;
                if (!TrySetWebcamLogicalNeck(mapping.Target, value, mapping.Speed))
                    ApplyControllerPositionWithSpeed(mapping.Target, value, mapping.Speed);
            }
            if (mixedNeck && neckComplete)
            {
                // Nod and tilt are complementary: send both parents and let NeckMix resolve the
                // pistons (nod keeps priority at the limits, exactly as on the timeline).
                var range = ServoCommand.RangeFor(ServoNames.NeckNodUp);
                ApplyControllerPositionWithSpeed("Servo:NeckNodUp", Math.Clamp(pitch, range.Min, range.Max), neckSpeed);
                ApplyControllerPositionWithSpeed("Servo:NeckTiltRight", Math.Clamp(roll, range.Min, range.Max), neckSpeed);
            }
            _controllerPreview.Flush();
            if (RecordingController)
            {
                if (_webcamNeckTargets.Count > 0) PushHeadPose();
                SampleControllerRecording(RecordingTime);
            }
        }
        catch (Exception ex) { ShowStatus("Face output stopped: " + ex.Message); _faceClient.Dispose(); }
        finally { _faceDriving = false; }
    }
    private void ToggleFaceRecording()
    {
        if (_recordingWindow?.UsesFace != true) return;
        if (RecordingController) StopControllerRecording("Face take stopped.");
        else _recordingWindow.StartFromController(_recordingWindow.Source);
    }
}
