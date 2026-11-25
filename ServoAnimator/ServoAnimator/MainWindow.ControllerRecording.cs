using System.Windows;
using System.Windows.Threading;

namespace ServoAnimator;

public partial class MainWindow
{
    private ControllerRecordingWindow _recordingWindow;
    private ControllerRecording _controllerTake;
    private List<ServoCommand> _processedTake;
    private double _recordingLimit, _recordingCursor;
    private int _recordingProcessingVersion;
    private long _recordingStatusAt;
    private readonly DispatcherTimer _recordingTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly DispatcherTimer _recordingProcessingTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private bool RecordingController => _recordingWindow?.Recording == true;
    private double RecordingTime => Math.Min(_recordingLimit, Math.Max(_controllerTake?.End ?? _recordingCursor,
        Math.Max(_cursorTime, IsRunning ? _playbackClock.EstimatedTime : _cursorTime)));
    private bool RecordingOwns(ServoNames servo, RobotControls? child = null) => RecordingController && _controllerTake?.Owns(servo, child) == true;
    private bool IsFloatingControllerView(Window window) => _floatingControllers.Values.Any(entry => entry.Window == window);
    private bool ControllerDialogBlocks(Window window) => window.IsVisible && window != _head && window != _webcamOverdubView &&
        !IsFloatingControllerView(window) && !(window == _recordingWindow && (RecordingController || _recordingWindow.CanStart || _recordingReplayOutput));
    private bool ControllerWindowActive => IsActive || _head?.IsActive == true || _recordingWindow?.IsActive == true ||
        _webcamOverdubView?.IsActive == true || _floatingControllers.Values.Any(entry => entry.Window.IsActive);
    private void UpdateRecordingButton() => SequenceRecordButton.Background = new System.Windows.Media.SolidColorBrush(
        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(RecordingController ? "#E02030" : "#701B24"));

    private void RecordController_Click(object sender, RoutedEventArgs e)
    {
        if (_meetingActive) StopMeeting();
        if (_recordingWindow != null)
        {
            if (_webcamOverdubView?.IsVisible == true) _webcamOverdubView.Activate();
            else _recordingWindow.ShowConfiguration();
            return;
        }
        if (_speedCalibrationBusy || _controllerConfigOpen || !ApplyOpenCommandEditor()) return;
        if (OwnedWindows.Cast<Window>().Any(ControllerDialogBlocks))
        { ShowStatus("Close other configuration or library windows before recording a controller performance."); return; }
        CancelApiLibraryPicker(); StopPlayback(); ResetControllerMotion(); EndMovieBackgroundControl(); EndArrowPrompt();
        _recordingCursor = ServoCommand.TimeKey(_cursorTime);
        _recordingWindow = new ControllerRecordingWindow(_recordingCursor, ConfigRoot, _faceSettings.OwnedControls) { Owner = this };
        _recordingWindow.StartRequested += StartControllerRecording;
        _recordingWindow.ReadyRequested += () =>
        {
            _faceOverrideControls = _recordingWindow.FaceOverrideControls;
            if (_recordingWindow.RecordAudio && string.IsNullOrEmpty(_jsonPath) && !SaveProjectAsInteractive())
            { _recordingWindow.ShowConfiguration(); return; }
            if (!PrepareWebcamOverdub()) { _recordingWindow.ShowConfiguration(); return; }
            ResetControllerMotion(); _recordingWindow.Hide();
            if (_webcamOverdubView != null) _webcamOverdubView.Show(); else Activate();
            RefreshControllerOutputPermissions();
            ShowStatus("Ready to record — controller REC starts/stops; webcam takes also use F9. Esc returns to configuration.");
        };
        _recordingWindow.ReplayRequested += ReplayControllerRecording;
        _recordingWindow.StopRequested += () => StopControllerRecording("Recording stopped.");
        _recordingWindow.ProcessingChanged += QueueRecordingProcessing;
        _recordingWindow.KeepRequested += KeepControllerRecording;
        _recordingTimer.Tick += RecordingTimer_Tick;
        _recordingProcessingTimer.Tick += RecordingProcessing_Tick;
        _recordingWindow.Closed += (_, _) =>
        {
            CloseWebcamOverdubView();
            _webcamOverdub = null;
            StopRecordingReplay();
            DiscardRecordingAudio();
            PlaybackSpeedSlider.Value = _recordingOriginalRate ?? PlaybackSpeedSlider.Value; _recordingOriginalRate = null;
            _recordingTimer.Stop(); _recordingProcessingTimer.Stop();
            _recordingTimer.Tick -= RecordingTimer_Tick; _recordingProcessingTimer.Tick -= RecordingProcessing_Tick;
            ++_recordingProcessingVersion; _recordingWindow = null; _controllerTake = null; _processedTake = null;
            UpdateRecordingButton();
            MainLayoutRoot.IsEnabled = true; ClearManualPoseOverrides(); ResetControllerMotion(); SetPlaybackControlSource(null);
            if (_head?.HeadView != null) _head.HeadView.IsEnabled = true;
            Waveform.Duration = TimelineDuration; SyncScrollBar();
            SetCursor(_recordingCursor); RefreshControllerOutputPermissions();
        };
        MainLayoutRoot.IsEnabled = false;
        if (_head?.HeadView != null) _head.HeadView.IsEnabled = false;
        _recordingWindow.Show(); RefreshControllerOutputPermissions();
    }

    private void StartControllerRecording(ControllerKind source, HashSet<ServoNames> selected, double seconds)
    {
        if (_recordingWindow.UsesController && (!_controllerConnections.TryGetValue(source, out var connection) || !connection.Enabled || _controllerMappingErrors.ContainsKey(source)))
        { CloseWebcamOverdubView(); _recordingWindow.Review(false); _recordingWindow.SetStatus("Connect and enable the selected controller first. Close this window to check its mappings."); _recordingWindow.ShowReview(); return; }
        try
        {
            if (_recordingWindow.UsesFace)
            {
                _faceSettings.Validate();
                if (!_faceClient.Fresh) throw new InvalidOperationException("Start face tracking and face the camera before recording.");
                if (!_faceSettings.Mappings.Any(m => m.Enabled)) throw new InvalidOperationException("Enable at least one face mapping.");
                _faceLastGood = Environment.TickCount64; _faceFiltered.Clear();
            }
            _webcamNeckTargets.Clear();
            var children = _recordingWindow.ArmedChildren.ToHashSet();
            if (_webcamOverdub != null)
                _webcamOverdub.RestrictRecordingControls(selected, children, _faceSettings, !_recordingWindow.UsesController);
            _controllerTake = new ControllerRecording(_recordingCursor, selected, children, _webcamOverdub?.LogicalNeckAxes);
            if (_controllerTake.Armed.Count == 0)
                throw new InvalidOperationException("Arm at least one enabled webcam mapping or controller control.");
            if (_recordingWindow.UsesFace && _faceSettings.EnforceServoSpeeds)
                _controllerTake.SpeedLimitedControls.UnionWith(_faceSettings.OwnedControls.Where(_controllerTake.OwnsControl));
            var view = (_urdfUndocked ? _head?.HeadView : EmbeddedHeadView) ?? EmbeddedHeadView;
            if (view == null || !view.CollisionModelAvailable || !view.UrdfDriveEnabled || view.PoseEditorActive)
                throw new InvalidOperationException("Load the URDF model, enable URDF Drive, and leave pose editing before recording.");
            _controllerPreview.Clear(); ResetControllerMotion();
            foreach (var command in BuildCommandsFromPose(view.CapturePose(), _recordingCursor))
            {
                foreach (var control in command.Control.HasValue ? new[] { command.Control.Value } : ServoConfiguration.ControlsFor(command.Servo))
                {
                    var seed = command.Clone(); seed.Control = control;
                    seed.Speed = view.CalibratedSpeedFor(control);
                    _controllerTake.Accept(seed);
                }
            }
            SeedWebcamNeckTargets(view.CapturePose());
            if (_controllerTake.SpeedLimitedControls.Any(NeckMix.IsPiston))
            {
                var pose = view.CapturePose(); var pistons = NeckMix.Pistons(_servoConfig, pose.NeckNod, pose.NeckTilt);
                foreach (var (control, value) in new[] { (RobotControls.NeckTiltLeft, pistons.Left), (RobotControls.NeckTiltRight, pistons.Right) })
                    _controllerTake.Accept(new ServoCommand { Servo = ServoNames.NeckNodUp, Control = control,
                        NumericValue = (int)Math.Round(value), Speed = view.CalibratedSpeedFor(control) });
            }
            _controllerTake.Sample(_recordingCursor);
            _recordingLimit = _recordingCursor + seconds;
            _enabledController = _recordingWindow.UsesController ? source : null; _recordingWindow.Begin();
            if (_recordingWindow.IsVisible) { _recordingWindow.Hide(); Activate(); }
            _webcamOverdubView?.SetRecording(true);
            UpdateRecordingButton();
            SetPlaybackControlSource(_recordingWindow.UsesController ? source : null);
            if (_controllerTake.SpeedLimitedControls.Count > 0)
            { _recordingOriginalRate ??= _playbackClock.Rate; PlaybackSpeedSlider.Value = 1; }
            if (_recordingWindow.RecordAudio)
            {
                if (string.IsNullOrEmpty(_jsonPath)) throw new InvalidOperationException("Save and name the sequence before recording audio.");
                _recordingOriginalRate ??= _playbackClock.Rate;
                PlaybackSpeedSlider.Value = 1;
                _recordingAudioPath = _recordingWindow.BeginAudio(System.IO.Path.GetDirectoryName(_jsonPath), System.IO.Path.GetFileNameWithoutExtension(_jsonPath));
            }
            StartPlaybackAt(_recordingCursor);
            if (_recordingAudioPath != null)
            {
                // Keep this provisional command out of the current playback indexes:
                // the WAV is still being written and must not play into the microphone.
                _recordingAudioCommand = new ServoCommand { Servo = ServoNames.Play, OffsetSeconds = _recordingCursor,
                    AudioTakeId = Guid.NewGuid().ToString("N"), AudioTrim = new AudioTrimSettings { CommandsLinked = true },
                    TextValue = ConfigPathService.ToRelative(ConfigRoot, _recordingAudioPath), Speed = ServoSpeed.NoChange,
                    Reason = System.IO.Path.GetFileName(_recordingAudioPath) };
                _doc.Commands.Add(_recordingAudioCommand);
            }
            Waveform.Duration = Math.Max(TimelineDuration, _recordingLimit);
            _recordingTimer.Start();
            _recordingWindow.SetStatus("Recording… Use the controller to perform. Stop ends this take; it does not keep it automatically.");
        }
        catch (Exception ex)
        {
            if (RecordingController) StopControllerRecording("Recording could not start: " + ex.Message);
            else { CloseWebcamOverdubView(); DiscardRecordingAudio(); _recordingWindow.Review(false); _recordingWindow.SetStatus("Recording could not start: " + ex.Message); _recordingWindow.ShowReview(); }
        }
    }

    private void RecordingTimer_Tick(object sender, EventArgs e)
    {
        if (!RecordingController) return;
        if (_recordingWindow.RecordAudio && _recordingWindow.AudioError != null) { StopControllerRecording("Microphone stopped: " + _recordingWindow.AudioError); return; }
        var source = _recordingWindow.Source;
        if (!IsRunning || (_recordingWindow.UsesController && (!_controllerConnections[source].Enabled || !ControllerFocusAllowed(source))))
        { StopControllerRecording("Recording stopped: playback stopped, controller disconnected, or controller focus was lost."); return; }
        if (RecordingTime - _controllerTake.End >= 1.0 / 30) SampleControllerRecording(RecordingTime);
        if (RecordingTime >= _recordingLimit) { StopControllerRecording("Maximum take length reached."); return; }
        if (Environment.TickCount64 - _recordingStatusAt >= 200)
        {
            _recordingStatusAt = Environment.TickCount64;
            _recordingWindow.SetStatus($"● Recording  {_controllerTake.End - _controllerTake.Start:F1} / {_recordingLimit - _controllerTake.Start:F0} s · {_controllerTake.Armed.Count} armed controls");
        }
    }

    private void StopControllerRecording(string message)
    {
        if (!RecordingController) return;
        _recordingTimer.Stop();
        try { _recordingWindow.EndAudio(); } catch (Exception ex) { message += " Audio: " + ex.Message; }
        SampleControllerRecording(RecordingTime);
        _cursorTime = _controllerTake.End;
        Waveform.CursorTime = _cursorTime; Waveform.InvalidateCursor(); UpdateTimeText();
        bool hasTake = _controllerTake.Frames.Count > 1 && _controllerTake.End > _controllerTake.Start;
        _recordingWindow.Review(hasTake);
        CloseWebcamOverdubView();
        UpdateRecordingButton();
        StopPlayback(); ResetControllerMotion(); ForEachHeadView(v => v.CalibratedMotionPaused = true);
        RefreshControllerOutputPermissions();
        _recordingWindow.SetStatus(message + (hasTake ? " Preparing editable commands…" : " No movement time was recorded; try again."));
        if (hasTake) QueueRecordingProcessing();
        else DiscardRecordingAudio();
        _recordingWindow.ShowReview();
    }

    private void QueueRecordingProcessing()
    {
        StopRecordingReplay();
        ++_recordingProcessingVersion; _processedTake = null;
        _recordingWindow?.SetProcessing(true);
        _recordingProcessingTimer.Stop(); _recordingProcessingTimer.Start();
    }

    private async void RecordingProcessing_Tick(object sender, EventArgs e)
    {
        _recordingProcessingTimer.Stop();
        var window = _recordingWindow; var take = _controllerTake; int version = _recordingProcessingVersion;
        if (window == null || take == null || RecordingController) return;
        double smoothing = window.Smoothing, tolerance = window.Tolerance; var splines = SplineServosEnabled().ToHashSet();
        try
        {
            var processed = await Task.Run(() => take.Process(smoothing, tolerance, splines));
            if (version != _recordingProcessingVersion || window != _recordingWindow) return;
            _processedTake = processed; window.SetProcessing(false);
            int raw = take.Frames.Sum(f => f.Commands.Length);
            int replaced = _doc.Commands.Count(c => take.OwnsTimelineCommand(c) && ServoCommand.TimeKey(c.OffsetSeconds) >= take.Start && ServoCommand.TimeKey(c.OffsetSeconds) <= take.End);
            window.SetStatus($"Take: {take.Start:F3}–{take.End:F3} s\n{raw:N0} sampled values → {processed.Count:N0} editable commands. Keep replaces {replaced:N0} existing commands in armed controls. One Undo restores the previous timeline.");
        }
        catch (Exception ex) { if (window == _recordingWindow) window.SetStatus("Could not process the take: " + ex.Message); }
    }

    private void KeepControllerRecording()
    {
        if (RecordingController || _processedTake == null || _processedTake.Count == 0) return;
        StopRecordingReplay();
        // The provisional Play command must not be included in the pre-take undo snapshot.
        if (_recordingAudioCommand != null) _doc.Commands.Remove(_recordingAudioCommand);
        PushUndo("Record controller performance");
        if (_recordingAudioCommand != null) _doc.Commands.Add(_recordingAudioCommand);
        string groupId = _recordingWindow.GroupRecordedCommands ? Guid.NewGuid().ToString("N") : "";
        var commands = _processedTake.Select(command =>
        {
            var copy = command.Clone(); copy.GroupId = groupId;
            if (_recordingAudioCommand != null) { copy.AudioTakeId = _recordingAudioCommand.AudioTakeId; copy.AudioSourceSeconds = copy.OffsetSeconds - _recordingCursor; }
            return copy;
        }).ToList();
        _doc.Commands = _controllerTake.Merge(_doc.Commands, commands, SplineServosEnabled().ToHashSet());
        if (!RefreshAfterEdit()) return;
        int count = _processedTake.Count;
        string audioPath = _recordingAudioPath;
        // Merge clones retained commands, so capture the actual accepted Play command.
        var audioCommand = _recordingAudioCommand == null ? null : _doc.Commands.FirstOrDefault(c =>
            c.Servo == ServoNames.Play && c.OffsetSeconds == _recordingAudioCommand.OffsetSeconds &&
            c.TextValue == _recordingAudioCommand.TextValue);
        bool convertVoice = _recordingWindow.AutomaticallyConvertVoice && audioPath != null && audioCommand != null;
        _recordingAudioPath = null; _recordingAudioCommand = null;
        _recordingWindow.AcceptAndClose();
        ShowStatus($"Recorded controller performance: {count:N0} editable commands. Undo restores the previous take.");
        if (convertVoice) _ = ConvertKeptRecordingVoiceAsync(_doc, audioCommand, audioPath, ConfigRoot);
    }

    private bool HandleRecordingIntent(ControllerKind source, ControllerIntent intent)
    {
        if (intent.Target == ControllerCatalog.RecordingTarget)
        {
            if (RecordingController)
            {
                if (source == _recordingWindow.Source) StopControllerRecording("Recording stopped from the controller.");
            }
            else
            {
                if (_recordingWindow?.Armed == true) _recordingWindow.StartFromController(source);
                else ShowStatus("Open Record and choose Ready to Record before using the controller REC button.");
            }
            return true;
        }
        if (_recordingWindow == null) return false;
        if (_recordingReplayOutput && intent.Target is "Action:Stop" or "Action:Disable servos")
        {
            StopRecordingReplay();
            if (intent.Target == "Action:Disable servos") DisableAll_Click(this, new RoutedEventArgs());
            return true;
        }
        if (!RecordingController || !_recordingWindow.UsesController || source != _recordingWindow.Source) return true;
        if (intent.Target is "Action:Stop" or "Action:Disable servos")
        {
            StopControllerRecording("Recording stopped from the controller.");
            if (intent.Target == "Action:Disable servos") { DisableControllerInput(); DisableAll_Click(this, new RoutedEventArgs()); }
            return true;
        }
        if (intent.Target.StartsWith("Action:Speed ", StringComparison.Ordinal))
        { if (Enum.TryParse<ServoSpeed>(intent.Target[13..], out var speed)) _controllerSpeed = speed; return true; }
        if (ControllerTargets.TryServo(intent.Target, out var servo, out _) && _controllerTake.Touches(servo))
            ApplyControllerPosition(intent.Target, intent.Value);
        return true;
    }
}
