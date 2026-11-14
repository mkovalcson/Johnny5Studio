using System.Windows;
using System.Windows.Media;

namespace ServoAnimator;

public partial class MainWindow
{
    private bool _meetingActive, _meetingStartedCamera, _meetingPreviewFace;
    private MeetingSettings _meetingSettings;
    private List<SceneViewState> _meetingSceneBaseline;
    private Dictionary<ControllerKind, bool> _meetingPreviousControllers;
    private ControllerKind? _meetingPreviousController;
    private TakeAudioCapture _meetingMicrophone;
    private readonly MeetingMouthEnvelope _meetingMouth = new();
    private bool MeetingUsesFace => _meetingSettings?.Source >= 2;
    private ControllerKind? MeetingController => _meetingSettings?.Source switch
    { 0 or 3 => ControllerKind.Xbox, 1 or 4 => ControllerKind.Steam, _ => null };

    private void Meeting_Click(object sender, RoutedEventArgs e)
    {
        if (_meetingActive) { StopMeeting(); return; }
        if (_recordingWindow != null || _controllerConfigOpen || _speedCalibrationBusy || _faceWindow != null)
        { ShowStatus("Close recording and configuration windows before starting a meeting."); return; }
        try
        {
            _sceneSetupWindow?.Close();
            if (_sceneSetupWindow != null) return; // The user chose Keep Editing.
            var current = _urdfUndocked ? _head?.HeadView : EmbeddedHeadView;
            if (IsRunning) PausePlayback();
            var dialog = new MeetingWindow(ConfigRoot, PreviewMeetingWebcam, MeetingInputReadiness,
                new BackgroundLibraryItem { RobotModelFile = current.LoadedModelFile, Settings = CurrentBackground() },
                preview => preview.ConfigureMeetingPreview(current, ConfigRoot), preview => preview.MirrorMeetingPose(current)) { Owner = this };
            if (dialog.ShowDialog() != true) return;
            _meetingSettings = dialog.Settings;
            if (MeetingController is ControllerKind kind && (!_controllerConnections[kind].Connected || _controllerMappingErrors.ContainsKey(kind)))
                throw new InvalidOperationException("Connect the selected controller and resolve any mapping errors before starting a meeting.");
            if (MeetingUsesFace) _faceSettings.Validate();
            if ((dialog.SelectedScene == null && !current.CollisionModelAvailable) || current.PoseEditorActive) throw new InvalidOperationException("Load the URDF model and leave pose editing first.");
            StopPlayback(); StopRecordingReplay(); ResetControllerMotion();
            // Meeting output is explicitly virtual, including when Teams has focus.
            LiveDriveBtn.IsChecked = false;
            _meetingMicrophone = new TakeAudioCapture(); _meetingMicrophone.Monitor(dialog.DeviceIndex, bufferMilliseconds: 10);
            if (MeetingUsesFace && !_faceClient.Running)
            { _faceClient.Start(_faceSettings.Camera, _faceSettings.FrameRate, _faceSettings.MirrorVideo); _meetingStartedCamera = true; }
            var setup = dialog.SelectedScene ?? new BackgroundLibraryItem { RobotModelFile = current.LoadedModelFile, Settings = CurrentBackground() };
            _meetingSceneBaseline = CaptureSceneViews();
            var sourceState = _meetingSceneBaseline.First(s => ReferenceEquals(s.View, current));
            SetUrdfUndocked(true);
            // A newly created detached view must restore the visible editor scene, not its startup defaults.
            ForEachHeadView(v =>
            {
                if (!_meetingSceneBaseline.Any(s => ReferenceEquals(s.View, v)))
                    _meetingSceneBaseline.Add(sourceState with { View = v, Model = sourceState.Model == null ? null : UrdfScene.Load(sourceState.Path) });
            });
            ApplyMeetingScene(setup);
            _head.HeadView.SetUrdfDriveEnabled(true);
            HelpSystem.SetTopic(_head.HeadView, "meeting-mode");
            _head.HeadView.SetMeetingView(true);
            // Clean presentation must not replace the chosen shot.
            ForEachHeadView(v => v.ApplyFraming(setup.Settings.Framing));
            _meetingMouth.Reset(); _faceFiltered.Clear(); _faceLastPacket = -1;
            // Starting a meeting explicitly activates its selected controller.
            // Restore the previous enable preferences when the session ends.
            _meetingPreviousControllers = _controllerConnections.ToDictionary(p => p.Key, p => !p.Value.ManuallyDisabled);
            _meetingPreviousController = _enabledController;
            if (MeetingController is ControllerKind selectedController)
                _controllerConnections[selectedController].SetEnabled(true);
            _meetingActive = true; _enabledController = MeetingController;
            ForEachHeadView(v => { v.SetUrdfDriveEnabled(true); v.CalibratedMotionPaused = false; });
            RefreshControllerOutputPermissions();
            RefreshActiveControllerMapping();
            MeetingButton.Content = "Stop meeting"; MeetingButton.Background = Brushes.Red; MeetingButton.Foreground = Brushes.White;
            UrdfRenderLoop.Current.AddTargets(MeetingFrame);
            Closed -= MeetingClosed; Closed += MeetingClosed;
            ShowStatus("Meeting active — use OBS Virtual Camera in Teams. No timeline recording.");
        }
        catch (Exception ex) { StopMeeting(); ShowStatus("Meeting could not start: " + ex.Message); }
        finally
        {
            _meetingPreviewFace = false;
            if (!_meetingActive && _meetingStartedCamera) { _faceClient.Dispose(); _meetingStartedCamera = false; }
            if (!_meetingActive) { _controllerPreview.Clear(); _faceFiltered.Clear(); }
            RefreshControllerOutputPermissions();
        }
    }
    private void ApplyMeetingScene(BackgroundLibraryItem item)
    {
        item.Settings.Validate(ConfigRoot);
        if (string.IsNullOrWhiteSpace(item.RobotModelFile) || !System.IO.File.Exists(item.RobotModelFile))
            throw new System.IO.InvalidDataException("Choose an available robot model for the meeting.");
        if (item.Settings.Framing == null) throw new System.IO.InvalidDataException("Meeting scene framing is missing.");
        _meetingSceneBaseline ??= CaptureSceneViews();
        var states = CaptureSceneViews();
        // Prepare every model before changing a viewport. Meeting does not save editor defaults or timeline state.
        var prepared = states.Select(s => (State: s, Model: string.Equals(s.Path, item.RobotModelFile, StringComparison.OrdinalIgnoreCase) ? s.Model : UrdfScene.Load(item.RobotModelFile))).ToArray();
        foreach (var state in states) state.View.PrepareBackdrop(item.Settings.Background);
        foreach (var entry in prepared)
        {
            if (!ReferenceEquals(entry.State.Model, entry.Model)) entry.State.View.InstallModel(item.RobotModelFile, entry.Model, item.Settings.Framing);
            entry.State.View.SetTimelineBackground(item.Settings.Background);
            entry.State.View.ApplyFraming(item.Settings.Framing);
        }
    }
    private void RestoreMeetingScene()
    {
        var states = _meetingSceneBaseline;
        _meetingSceneBaseline = null;
        if (states == null) return;
        ForEachHeadView(v => v.SetMeetingView(false));
        RestoreSceneViews(states);
        foreach (var state in states) state.View.RememberSavedFraming();
    }
    private MeetingInputState MeetingInputReadiness(int source, bool useSavedScene)
    {
        var view = _urdfUndocked ? _head?.HeadView : EmbeddedHeadView;
        bool modelReady = (useSavedScene || view?.CollisionModelAvailable == true) && view?.PoseEditorActive != true;
        var model = new MeetingDeviceState(modelReady, modelReady ? "Preview ready" : view?.PoseEditorActive == true ? "Leave pose editing before starting." : "Load a robot in URDF Scene Setup.");
        var camera = new MeetingDeviceState(true, "Off", false);
        if (source >= 2)
        {
            bool ready = _faceClient.Running && _faceClient.Error == null;
            camera = new(ready, _faceClient.Error ?? (!_faceClient.Running ? "Camera unavailable. Check face-tracking settings." : $"Camera {_faceSettings.Camera} · " + (_faceClient.Fresh ? "Tracking face" : "Waiting for face")));
        }
        ControllerKind? kind = source switch { 0 or 3 => ControllerKind.Xbox, 1 or 4 => ControllerKind.Steam, _ => null };
        var controller = new MeetingDeviceState(true, "Not selected", false);
        if (kind.HasValue)
        {
            bool connected = _controllerConnections[kind.Value].Connected;
            bool valid = !_controllerMappingErrors.ContainsKey(kind.Value);
            controller = new(connected && valid, kind.Value + ": " + (!connected ? "Connect the controller." : !valid ? "Fix its controller mapping." : "Connected · Enabled on Start"));
        }
        return new(model, camera, controller);
    }
    private void PreviewMeetingWebcam(bool enabled)
    {
        _meetingPreviewFace = false;
        if (enabled)
        {
            _faceSettings.Validate();
            if (!_faceClient.Running) { _faceClient.Start(_faceSettings.Camera, _faceSettings.FrameRate, _faceSettings.MirrorVideo); _meetingStartedCamera = true; }
            ForEachHeadView(v => { v.SetUrdfDriveEnabled(true); v.CalibratedMotionPaused = false; });
            _meetingPreviewFace = true;
        }
        else if (_meetingStartedCamera) { _faceClient.Dispose(); _meetingStartedCamera = false; }
        _controllerPreview.Clear(); _faceFiltered.Clear(); _faceLastPacket = -1;
        RefreshControllerOutputPermissions();
    }
    private void MeetingFrame(TimeSpan frameTime)
    {
        if (!_meetingActive) return;
        if (_meetingMicrophone.Error != null) { string error = _meetingMicrophone.Error; StopMeeting(); ShowStatus("Meeting stopped: " + error); return; }
        _head?.HeadView.SetMouth(_meetingMouth.Advance(_meetingMicrophone.TakePeak(), _meetingSettings, frameTime));
    }
    private void MeetingClosed(object sender, EventArgs e) => StopMeeting();
    private void StopMeeting()
    {
        UrdfRenderLoop.Current.RemoveTargets(MeetingFrame);
        _meetingMouth.Reset();
        _meetingMicrophone?.Dispose(); _meetingMicrophone = null;
        if (_meetingStartedCamera) _faceClient.Dispose();
        _meetingStartedCamera = false;
        if (_meetingActive)
        {
            _meetingActive = false;
            _head?.HeadView.SetMouth(0); _head?.HeadView.SetMeetingView(false);
            HelpSystem.SetTopic(_head?.HeadView, "urdf-viewer");
            ForEachHeadView(v => v.CalibratedMotionPaused = true);
            _controllerPreview.Clear(); ResetControllerMotion();
            if (_meetingPreviousControllers != null)
                foreach (var previous in _meetingPreviousControllers) _controllerConnections[previous.Key].SetEnabled(previous.Value);
            _enabledController = _meetingPreviousController;
            _meetingPreviousControllers = null; _meetingPreviousController = null;
            _faceFiltered.Clear(); _faceLastPacket = -1;
            RefreshActiveControllerMapping();
            RefreshControllerOutputPermissions();
        }
        try { RestoreMeetingScene(); }
        catch (Exception ex) { ShowStatus("Restore meeting scene: " + ex.Message); }
        MeetingButton.Content = "Meeting"; MeetingButton.Background = new SolidColorBrush(Color.FromRgb(51, 70, 90));
    }
}
