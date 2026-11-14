namespace ServoAnimator;

public partial class MainWindow
{
    private bool _faceTrackingOverride;
    private HashSet<RobotControls> _faceOverrideControls = new();
    private void SetFaceTrackingOverride(bool enabled)
    {
        if (_faceTrackingOverride == enabled) return;
        // Recording has an explicit per-actuator takeover selection. Live preview
        // and meetings let the enabled override take over every face-mapped actuator.
        if (enabled) _faceOverrideControls = _recordingWindow?.FaceOverrideControls ?? new();
        _faceTrackingOverride = enabled;
        Interlocked.Increment(ref _controllerOutputEpoch); _controllerPendingHardware.Clear();
        _controllerPreview.Clear(); _faceFiltered.Clear(); _controllerPositions.Clear();
        _faceLastPacket = -1;
        RefreshActiveControllerMapping();
        if (_controllerMappingWindow != null) _controllerMappingWindow.FaceTrackingOverride = enabled;
        ShowStatus("Face Tracking Override " + (enabled ? "on — release button to resume webcam" : "off"));
    }
    private bool FaceOverrideOwns(RobotControls control) => _faceTrackingOverride
        && (_recordingWindow == null || _recordingWindow.UsesFace && _recordingWindow.UsesController && _faceOverrideControls.Contains(control));

    private bool FaceHasPriority => RecordingController && _recordingWindow.UsesFace
        || _meetingActive && MeetingUsesFace || _meetingPreviewFace || _faceWindow?.PreviewEnabled == true;

    private IEnumerable<ServoCommand> FaceCompatibleLibraryCommands(IEnumerable<ServoCommand> commands)
    {
        if (!FaceHasPriority) { foreach (var command in commands) yield return command; yield break; }
        var settings = _faceWindow?.PreviewEnabled == true ? _faceWindow.PreviewSettings : _faceSettings;
        var reserved = settings.OwnedControls.Where(c => !FaceOverrideOwns(c)).ToHashSet();
        foreach (var command in commands)
        {
            if (command.Control.HasValue)
            { if (!reserved.Contains(command.Control.Value)) yield return command; continue; }
            var controls = ServoConfiguration.ControlsFor(command.Servo).ToArray();
            if (!controls.Any(reserved.Contains)) { yield return command; continue; }
            // Parent poses may overlap only part of a face mapping. Preserve the
            // independent children rather than dropping the whole parent command.
            foreach (var control in controls.Where(c => !reserved.Contains(c)))
            { var copy = command.Clone(); copy.Control = control; yield return copy; }
        }
    }
}
