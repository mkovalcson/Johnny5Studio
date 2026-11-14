using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using ServoAnimator;

internal static partial class Program
{
    private static void FaceSpeedLimitChecks()
    {
        var app = new App(); app.InitializeComponent(); DisableTestStartup(app);
        Check(!new FaceTrackingSettings().EnforceServoSpeeds, "Existing mappings keep speed-limited recording off by default");
        string temporary = Path.Combine(Path.GetTempPath(), "FaceSpeedChecks-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temporary);
        var saved = new FaceTrackingSettings { EnforceServoSpeeds = true }; saved.Save(temporary);
        Check(FaceTrackingSettings.Load(temporary).EnforceServoSpeeds, "Servo speed option persists with face mappings");
        File.WriteAllText(Path.Combine(temporary, "FaceTracking.json"), System.Text.Json.JsonSerializer.Serialize(new FaceTrackingSettings()).Replace("\"EnforceServoSpeeds\":false,", ""));
        Check(!FaceTrackingSettings.Load(temporary).EnforceServoSpeeds, "Old files without the option retain legacy behavior");
        File.WriteAllText(Path.Combine(temporary, "FaceTracking.json"), System.Text.Json.JsonSerializer.Serialize(new FaceTrackingSettings()).Replace("\"Speed\":\"Default\",", ""));
        Check(FaceTrackingSettings.Load(temporary).Mappings.All(m => m.Speed == ServoSpeed.NoChange), "Old mapping rows retain their previously active actuator profiles");
        saved.Mappings[0].Speed = ServoSpeed.Slow; saved.Mappings[3].Speed = ServoSpeed.Fast; saved.Mappings[0].Neutral = 1.123456789;
        saved.Save(temporary);
        var restored = FaceTrackingSettings.Load(temporary);
        Check(restored.Mappings[0].Speed == ServoSpeed.Slow && restored.Mappings[3].Speed == ServoSpeed.Fast && restored.Mappings[0].Neutral == 1.123456789, "Per-row profiles and full neutral precision survive save/load");
        var conflictingNeck = new FaceTrackingSettings(); conflictingNeck.Mappings[2].Speed = ServoSpeed.Crawl;
        bool rejected = false; try { conflictingNeck.Validate(); } catch (InvalidDataException ex) { rejected = ex.Message.Contains("same Speed profile"); }
        Check(rejected, "Shared neck rows explain why conflicting profiles cannot be applied");

        var configuration = ServoConfiguration.CreateDefault();
        foreach (var entry in configuration.Servos)
        {
            entry.MinPwm = 1000; entry.DefaultPwm = 1500; entry.MaxPwm = 2000; entry.Reversed = false;
            entry.Speeds = new[] { 4, 2, 8, 1 }; entry.Accels = new[] { 0, 0, 0, 0 };
        }
        var timing = new SpeedCalibrationData { UseInUrdf = false };
        var view = new RobotHeadView(); view.SetServoConfiguration(configuration); view.SetSpeedCalibration(timing); view.SetCollisionWarningsEnabled(false);
        view.CalibratedMotionAllowed = () => true;
        void Seed(ServoNames parent, RobotControls? child, double value)
        {
            view.SnapCalibratedMotion = true;
            if (child.HasValue) view.SetChildServo(parent, child.Value, value, preserveOtherNeck: true); else view.SetServo(parent, value);
            view.SnapCalibratedMotion = false;
        }
        var turnControls = new HashSet<RobotControls> { RobotControls.NeckTurn };
        view.SetFaceSpeedLimitedControls(turnControls); Seed(ServoNames.NeckTurn, null, 0);
        view.SetServo(ServoNames.NeckTurn, 100);
        Check(view.CapturePose().NeckTurn == 0, "An abrupt webcam target does not jump the preview even with general URDF calibration off");
        view.AdvanceCalibratedMotion(.25);
        var turn = view.CaptureCalibratedCommands(turnControls).Single();
        Check(turn.NumericValue == 5, "Default speed 4 yields 100 microseconds/sec, mapped through the actual PWM span");
        view.ConfigureCalibratedSpeed(ServoNames.NeckTurn, null, ServoSpeed.Slow); view.AdvanceCalibratedMotion(.1);
        Check(view.CaptureCalibratedCommands(turnControls).Single().NumericValue == 6, "Changing the active speed profile uses that output's configured Slow speed");
        view.SetServo(ServoNames.NoseBody, 40);
        Check(view.CapturePose().NoseBody == 40, "Face enforcement leaves unselected controls under the general calibration preference");
        configuration.Get(RobotControls.NeckTurn).Accels[0] = 1;
        view.SetSpeedCalibration(new SpeedCalibrationData { UseInUrdf = false }); view.ConfigureCalibratedSpeed(ServoNames.NeckTurn, null, ServoSpeed.Default);
        Seed(ServoNames.NeckTurn, null, 0); view.SetServo(ServoNames.NeckTurn, 100); view.AdvanceCalibratedMotion(.1);
        Check(view.CapturePose().NeckTurn > 0 && view.CapturePose().NeckTurn < 1, "Configured acceleration limits the beginning of a step response");
        configuration.Get(RobotControls.NeckTurn).Accels[0] = 0;
        configuration.Get(RobotControls.NeckTurn).Reversed = true;
        view.SetSpeedCalibration(new SpeedCalibrationData { UseInUrdf = false }); Seed(ServoNames.NeckTurn, null, 0);
        view.SetServo(ServoNames.NeckTurn, 100); view.AdvanceCalibratedMotion(.25);
        Check(view.CaptureCalibratedCommands(turnControls).Single().NumericValue == 5, "Physical reversal changes PWM direction without changing logical speed");

        var neckControls = new HashSet<RobotControls> { RobotControls.NeckTiltLeft, RobotControls.NeckTiltRight };
        configuration.Get(RobotControls.NeckTiltRight).Speeds[0] = 8;
        view.SetFaceSpeedLimitedControls(neckControls); Seed(ServoNames.NeckNodUp, RobotControls.NeckTiltLeft, 0); Seed(ServoNames.NeckNodUp, RobotControls.NeckTiltRight, 0);
        view.SetServo(ServoNames.NeckNodUp, 100); view.SetServo(ServoNames.NeckTiltRight, 50); view.AdvanceCalibratedMotion(.25);
        var neck = view.CaptureCalibratedCommands(neckControls).OrderBy(c => c.Control).ToArray();
        Check(neck.Length == 2 && neck.All(c => c.Servo == ServoNames.NeckNodUp && c.Control.HasValue), "Mixed nod/tilt is recorded as the two physical piston channels");
        Check(neck.Select(c => Math.Abs(c.NumericValue)).Order().SequenceEqual(new[] { 5, 10 }), "Mixed neck motion respects each piston's independent configured speed");
        Seed(ServoNames.NeckTiltRight, RobotControls.NeckTiltLeft, 25);
        var direct = view.CaptureCalibratedCommands(neckControls).Single(c => c.Control == RobotControls.NeckTiltLeft);
        Check(direct.Servo == ServoNames.NeckNodUp && direct.NumericValue == (int)NeckMix.ToNodUnits(configuration, ServoNames.NeckTiltRight, RobotControls.NeckTiltLeft, 25), "Direct piston aliases are normalized so both channels survive recording");

        var eyeControls = new HashSet<RobotControls> { RobotControls.LeftEyePop, RobotControls.RightEyePop };
        timing.Steppers[0].ExtendSeconds = 2; timing.Steppers[0].RetractSeconds = 4;
        timing.Steppers[1].ExtendSeconds = 1; timing.Steppers[1].RetractSeconds = .5;
        view.SetSpeedCalibration(timing); view.SetFaceSpeedLimitedControls(eyeControls);
        Seed(ServoNames.BothEyePop, null, 0); view.SetServo(ServoNames.BothEyePop, 2000); view.AdvanceCalibratedMotion(.1);
        var eyes = view.CaptureCalibratedCommands(eyeControls).ToDictionary(c => c.Control.Value, c => c.NumericValue);
        Check(eyes[RobotControls.LeftEyePop] == 100 && eyes[RobotControls.RightEyePop] == 200, "Each Eye Pop uses its own calibrated extend time");
        view.SetServo(ServoNames.BothEyePop, 0); view.AdvanceCalibratedMotion(.05);
        eyes = view.CaptureCalibratedCommands(eyeControls).ToDictionary(c => c.Control.Value, c => c.NumericValue);
        Check(eyes[RobotControls.LeftEyePop] == 75 && eyes[RobotControls.RightEyePop] == 0, "Eye Pop retraction has independent timing");

        var take = new ControllerRecording(0, new[] { ServoNames.NeckNodUp }); take.SpeedLimitedControls.UnionWith(neckControls);
        foreach (double t in new[] { 0, .033, .067, .1 })
        {
            foreach (var c in neck) { var copy = c.Clone(); copy.NumericValue = (int)Math.Round(c.NumericValue * t / .1); take.Accept(copy); }
            take.Sample(t);
        }
        var processed = take.Process(500, 10, new HashSet<ServoNames> { ServoNames.NeckNodUp });
        Check(processed.Count == 8 && processed.All(c => c.Control.HasValue), "Take processing retains all calibrated physical samples despite smoothing/reduction settings");
        Check(processed.Where(c => c.Control == RobotControls.NeckTiltLeft).Select(c => c.NumericValue).SequenceEqual(take.Frames.Select(f => f.Commands.Single(c => c.Control == RobotControls.NeckTiltLeft).NumericValue)), "Processing does not alter the modeled trajectory");

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var main = new MainWindow();
        void Set(string name, object value) => typeof(MainWindow).GetField(name, flags).SetValue(main, value);
        object Get(string name) => typeof(MainWindow).GetField(name, flags).GetValue(main);
        object Call(string name, params object[] args) => typeof(MainWindow).GetMethod(name, flags).Invoke(main, args);
        var head = (RobotHeadView)main.FindName("EmbeddedHeadView");
        var faceSettings = new FaceTrackingSettings { EnforceServoSpeeds = true, Mappings = new() { new FaceMapping { Enabled = true, SmoothingMs = 0, OutputMin = -100, OutputMax = 100 } } };
        var client = new FaceTrackingClient(() => true);
        client.AcceptPacket(new FacePacket { Detected = true, TimestampMs = 1, Values = new() { ["headYaw"] = 30 } });
        var recording = new ControllerRecordingWindow(0);
        ((ComboBox)typeof(ControllerRecordingWindow).GetField("_source", flags).GetValue(recording)).SelectedIndex = 2;
        Set("_faceSettings", faceSettings); Set("_faceClient", client); Set("_meetingPreviewFace", true);
        Set("_recordingWindow", recording); Set("_recordingCursor", 0d); Set("_focusControl", new FocusControlSettings { CollisionSafeguard = false });
        Set("_servoConfig", configuration);
        head.SetServoConfiguration(configuration); head.SetSpeedCalibration(timing); head.SetCollisionWarningsEnabled(false);
        Set("_speedCalibration", timing); Set("_speedCalibrationRoot", typeof(MainWindow).GetProperty("ConfigRoot", flags).GetValue(main));
        ((Slider)main.FindName("PlaybackSpeedSlider")).Value = .5;
        Call("StartControllerRecording", ControllerKind.Xbox, new HashSet<ServoNames> { ServoNames.NeckTurn }, 10d); Call("StopPlaybackRendering");
        Check(recording.Recording && ((Slider)main.FindName("PlaybackSpeedSlider")).Value == 1, "Enforced webcam capture starts at physical 1× timing");
        Call("FaceTrackingTick");
        var captured = (ControllerRecording)Get("_controllerTake");
        Check(captured.Current(ServoNames.NeckTurn, null) == 0, "Recorder does not accept the raw 100-unit webcam step");
        head.CalibratedMotionAllowed = () => true; head.AdvanceCalibratedMotion(.25); Call("SampleControllerRecording", .25d);
        Check(captured.Current(ServoNames.NeckTurn, null) == 5, "Recorder samples the same speed-limited position shown in the preview");
        Check(captured.Process(500, 10, new HashSet<ServoNames>()).Any(c => c.NumericValue == 5), "Modeled samples survive the real take processor");
        ((ComboBox)typeof(ControllerRecordingWindow).GetField("_source", flags).GetValue(recording)).SelectedIndex = 3;
        Set("_faceTrackingOverride", true); Set("_faceOverrideControls", new HashSet<RobotControls> { RobotControls.NeckTurn });
        Call("ApplyControllerPosition", "Servo:NeckTurn", 40d); Call("SampleControllerRecording", .5d);
        Check(captured.Current(ServoNames.NeckTurn, null) == 40 && !head.UsesCalibratedMotion, "Controller takeover keeps its ordinary recording and global calibration preference");
        Set("_faceTrackingOverride", false);
        client.AcceptPacket(new FacePacket { Detected = true, TimestampMs = 34, Values = new() { ["headYaw"] = 30 } }); Call("FaceTrackingTick");
        Check(head.UsesCalibratedMotion && captured.Current(ServoNames.NeckTurn, null) == 40, "Webcam ownership resumes from the controller's pose rather than jumping to its raw target");
        head.CalibratedMotionAllowed = () => true; head.AdvanceCalibratedMotion(.25); Call("SampleControllerRecording", .75d);
        Check(captured.Current(ServoNames.NeckTurn, null) == 45, "Resumed webcam capture obeys the same speed limit");
        Set("_controllerSpeed", ServoSpeed.Fast); faceSettings.Mappings[0].Speed = ServoSpeed.Slow;
        client.AcceptPacket(new FacePacket { Detected = true, TimestampMs = 67, Values = new() { ["headYaw"] = 30 } }); Call("FaceTrackingTick");
        Check(head.CalibratedSpeedFor(RobotControls.NeckTurn) == ServoSpeed.Slow, "Webcam row Speed takes precedence over the controller profile");
        head.CalibratedMotionAllowed = () => true; head.AdvanceCalibratedMotion(.2); Call("SampleControllerRecording", .95d);
        Check(captured.Current(ServoNames.NeckTurn, null) == 47, "The recorded webcam trajectory uses the row's selected Slow speed");
        faceSettings.Mappings[0].Speed = ServoSpeed.NoChange;
        client.AcceptPacket(new FacePacket { Detected = true, TimestampMs = 100, Values = new() { ["headYaw"] = 30 } }); Call("FaceTrackingTick");
        Check(head.CalibratedSpeedFor(RobotControls.NeckTurn) == ServoSpeed.Slow, "N/C retains the actuator's profile instead of inheriting the controller's Fast profile");
        Check(!main.IsLoaded && !((HardwareManager)Get("_hw")).Connected, "Webcam speed checks never connect or move hardware");
        Call("StopControllerRecording", "Synthetic speed take complete"); Call("StopRecordingReplay");
        Set("_recordingWindow", null); recording.Review(false); recording.Close();

        var faceWindow = new FaceTrackingWindow(new FaceTrackingClient(), saved);
        var checkbox = (CheckBox)typeof(FaceTrackingWindow).GetField("_servoSpeeds", flags).GetValue(faceWindow);
        Check(checkbox.IsChecked == true && checkbox.ToolTip.ToString().Contains("Acceleration"), "Configuration shows the saved option and explains acceleration");
        FaceTrackingSettings applied = null; faceWindow.ApplyRequested = settings => applied = settings;
        checkbox.IsChecked = false; typeof(FaceTrackingWindow).GetMethod("Apply", flags).Invoke(faceWindow, null);
        Check(applied?.EnforceServoSpeeds == false && !faceWindow.PreviewSettings.EnforceServoSpeeds, "Save/apply turns enforcement off for the active preview");
        checkbox.IsChecked = true; typeof(FaceTrackingWindow).GetMethod("Apply", flags).Invoke(faceWindow, null);
        Check(applied.EnforceServoSpeeds && faceWindow.PreviewSettings.EnforceServoSpeeds, "Save/apply enables the option for preview and subsequent recording");
        Check(applied.Mappings[0].Neutral == 1.123456789, "Applying a mapping does not round its Neutral value");
        var grid = (DataGrid)typeof(FaceTrackingWindow).GetField("_grid", flags).GetValue(faceWindow);
        Check(grid.Columns[3] is DataGridComboBoxColumn speedColumn && speedColumn.SelectedValueBinding is System.Windows.Data.Binding speedBinding && speedBinding.Path.Path == "Speed", "Speed is edited on each mapping row");
        var neutralColumn = grid.Columns.OfType<DataGridTemplateColumn>().Single(c => Equals(c.Header, "Neutral"));
        FrameworkElement Cell(DataTemplate template, FaceMapping mapping)
        {
            var presenter = new ContentPresenter { Content = mapping, ContentTemplate = template };
            presenter.Measure(new Size(240, 50)); presenter.Arrange(new Rect(0, 0, 240, 50)); presenter.UpdateLayout();
            return (FrameworkElement)System.Windows.Media.VisualTreeHelper.GetChild(presenter, 0);
        }
        var neutralDisplay = (TextBlock)Cell(neutralColumn.CellTemplate, applied.Mappings[0]);
        Check(neutralDisplay.Text == "1.12346", $"Neutral display uses five decimal places (displayed '{neutralDisplay.Text}')");
        var neutralEditor = (TextBox)Cell(neutralColumn.CellEditingTemplate, applied.Mappings[0]);
        Check(neutralEditor.Text == "1.123456789", "Editing Neutral exposes its full precision");
        var draft = (FaceTrackingSettings)typeof(FaceTrackingWindow).GetField("_draft", flags).GetValue(faceWindow);
        var count = draft.Mappings.Count; var removed = draft.Mappings[2]; grid.SelectedItem = draft.Mappings[0];
        var removeTemplate = ((DataGridTemplateColumn)grid.Columns.Last()).CellTemplate;
        var removeButton = (Button)Cell(removeTemplate, removed);
        removeButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Check(draft.Mappings.Count == count - 1 && !draft.Mappings.Contains(removed) && draft.Mappings[0].Neutral == 1.123456789, "Row X removes its own mapping rather than the selected row");
        var mappingScroll = ((DockPanel)faceWindow.Content).Children.OfType<ScrollViewer>().Single();
        var mappingPanel = ((StackPanel)mappingScroll.Content).Children.OfType<StackPanel>().Last(); var plus = (Button)mappingPanel.Children[1];
        plus.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Check(draft.Mappings.Count == count && !draft.Mappings.Last().Enabled && draft.Mappings.Last().Speed == ServoSpeed.Default && Equals(plus.Content, "+"), "The button below the mapping rows adds a disabled row with Default speed");
        typeof(FaceTrackingWindow).GetMethod("Apply", flags).Invoke(faceWindow, null);
        Check(applied.Mappings.Count == count && !applied.Mappings.Any(m => m.Signal == "headRoll"), "Save/apply commits row additions and removals");
        var previews = PreviewDirectory("face-previews"); Directory.CreateDirectory(previews);
        ((DockPanel)faceWindow.Content).Background = faceWindow.Background;
        RenderControl((FrameworkElement)faceWindow.Content, Path.Combine(previews, "FaceTrackingServoSpeeds.png"), 1200, 920);
        var rowSpeed = (ComboBox)grid.Columns[3].GetCellContent(draft.Mappings[0]);
        rowSpeed.SelectedValue = ServoSpeed.Crawl;
        typeof(FaceTrackingWindow).GetMethod("Apply", flags).Invoke(faceWindow, null);
        Check(applied.Mappings[0].Speed == ServoSpeed.Crawl && faceWindow.PreviewSettings.Mappings[0].Speed == ServoSpeed.Crawl, $"Selecting a row's Speed dropdown updates the saved and preview profiles (saved {applied.Mappings[0].Speed}, selected {rowSpeed.SelectedValue}, binding {rowSpeed.GetBindingExpression(System.Windows.Controls.Primitives.Selector.SelectedValueProperty)?.Status})");
        rowSpeed.SelectedValue = ServoSpeed.Slow;
        typeof(FaceTrackingWindow).GetMethod("Apply", flags).Invoke(faceWindow, null);
        mappingScroll.ScrollToEnd();
        RenderControl((FrameworkElement)faceWindow.Content, Path.Combine(previews, "FaceTrackingMappingsBottom.png"), 1160, 790);
        faceWindow.Close();
    }
}
