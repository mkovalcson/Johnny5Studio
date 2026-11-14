using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using ServoAnimator;

internal static partial class Program
{
    private static void FaceTrackingChecks()
    {
        var settings = new FaceTrackingSettings(); settings.Validate();
        Check(settings.Mappings.Where(m => m.Enabled).Select(m => m.Signal).ToHashSet().SetEquals(new[] { "headYaw", "headPitch", "headRoll", "squint" }), "Head orientation and squint are enabled by default");
        var map = settings.Mappings[0];
        Check(map.Map(-30) == -50 && map.Map(30) == 50 && map.Map(0) == 0, "Neutral and calibrated endpoints map correctly");
        Check(map.Map(999) == 50 && map.Map(-999) == -50, "Input is clamped to output range");
        map.Neutral = 10; Check(map.Map(10) == 0, "Neutral offset is removed");
        map.Reverse = true; Check(map.Map(40) == -50, "Reverse maps direction correctly");
        map.InputMax = map.InputMin; bool rejected = false;
        try { settings.Validate(); } catch { rejected = true; }
        Check(rejected, "Zero input span is rejected");
        settings = new(); settings.Mappings.Add(new FaceMapping { Enabled = true }); rejected = false;
        try { settings.Validate(); } catch { rejected = true; }
        Check(rejected, "Duplicate actuator mappings are rejected");
        var app = new App(); app.InitializeComponent(); DisableTestStartup(app); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        FaceFrameRateChecks();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(Application).GetField("_startupUri", flags).SetValue(app, null);
        var recorder = new ControllerRecordingWindow(0);
        var source = (ComboBox)typeof(ControllerRecordingWindow).GetField("_source", flags).GetValue(recorder);
        source.SelectedIndex = 2; Check(recorder.UsesFace && !recorder.UsesController, "Webcam-only recording needs no controller");
        source.SelectedIndex = 3; Check(recorder.UsesFace && recorder.UsesController && recorder.Source == ControllerKind.Xbox, "Mixed Xbox capture uses both sources");
        source.SelectedIndex = 4; Check(recorder.UsesFace && recorder.UsesController && recorder.Source == ControllerKind.Steam, "Mixed Steam capture uses both sources");
        var window = new MainWindow();
        var client = new FaceTrackingClient();
        var faceWindow = new FaceTrackingWindow(client, new FaceTrackingSettings());
        Check(!faceWindow.PreviewEnabled && !client.Fresh && !window.IsLoaded, "Configuration does not start camera or hardware");
        string previews = PreviewDirectory("face-previews");
        System.IO.Directory.CreateDirectory(previews);
        RenderControl((FrameworkElement)faceWindow.Content, System.IO.Path.Combine(previews, "FaceTrackingConfiguration.png"), 1160, 790);
        void Set(string name, object value) => typeof(MainWindow).GetField(name, flags).SetValue(window, value);
        object Get(string name) => typeof(MainWindow).GetField(name, flags).GetValue(window);
        object Call(string name, params object[] args) => typeof(MainWindow).GetMethod(name, flags).Invoke(window, args);
        var synthetic = new FaceTrackingClient(() => true);
        synthetic.AcceptPacket(new FacePacket { Detected = true, TimestampMs = 1, Values = new() { ["headYaw"] = 15 } });
        Set("_faceClient", synthetic); Set("_recordingWindow", recorder); Set("_recordingCursor", 0.0);
        Set("_focusControl", new FocusControlSettings { CollisionSafeguard = false });
        var doc = new AnimationDocument(); Set("_doc", doc); Call("RefreshAfterEdit");
        var head = (RobotHeadView)window.FindName("EmbeddedHeadView");
        var calibration = new SpeedCalibrationData { UseInUrdf = false };
        Set("_speedCalibration", calibration); Set("_speedCalibrationRoot", typeof(MainWindow).GetProperty("ConfigRoot", flags).GetValue(window));
        head.SetSpeedCalibration(calibration); head.SetCollisionWarningsEnabled(false);
        source.SelectedIndex = 2;
        Call("StartControllerRecording", ControllerKind.Xbox, new HashSet<ServoNames> { ServoNames.NeckTurn }, 10.0);
        Call("StopPlaybackRendering");
        Check(recorder.Recording, "Webcam recording starts without an enabled controller");
        Set("_faceDriving", true); Call("ApplyControllerPosition", "Servo:NeckTurn", 25.0); Set("_faceDriving", false);
        var take = (ControllerRecording)Get("_controllerTake");
        Set("_cursorTime", 1.0); take.Sample(1);
        Check(take.Current(ServoNames.NeckTurn, null) == 25, "Face values enter the existing recorder");
        Call("StopControllerRecording", "Synthetic take complete");
        Set("_processedTake", take.Process(0, 0, new HashSet<ServoNames>()));
        Call("KeepControllerRecording"); Set("_recordingWindow", null);
        Check(doc.Commands.Any(c => c.Servo == ServoNames.NeckTurn && c.NumericValue == 25 && !string.IsNullOrEmpty(c.GroupId)), "Face take keeps editable grouped commands");
        var mixed = new ControllerRecordingWindow(2);
        ((ComboBox)typeof(ControllerRecordingWindow).GetField("_source", flags).GetValue(mixed)).SelectedIndex = 3;
        Set("_recordingWindow", mixed); Set("_recordingCursor", 2.0);
        ((Dictionary<ControllerKind, ControlConnectionState>)Get("_controllerConnections"))[ControllerKind.Xbox].Connected = true;
        synthetic.AcceptPacket(new FacePacket { Detected = true, TimestampMs = 2 });
        Call("StartControllerRecording", ControllerKind.Xbox, new HashSet<ServoNames> { ServoNames.NeckTurn, ServoNames.NoseBasket }, 10.0);
        Call("StopPlaybackRendering");
        Set("_faceDriving", true); Call("ApplyControllerPosition", "Servo:NeckTurn", 30.0); Set("_faceDriving", false);
        Call("HandleRecordingIntent", ControllerKind.Xbox, new ControllerIntent("Servo:NeckTurn", 90));
        Call("HandleRecordingIntent", ControllerKind.Xbox, new ControllerIntent("Servo:NoseBasket", 40));
        take = (ControllerRecording)Get("_controllerTake");
        Check(take.Current(ServoNames.NeckTurn, null) == 30 && take.Current(ServoNames.NoseBasket, null) == 40, "Mixed sources record independently and controller cannot overwrite face-owned actuator");
        synthetic.AcceptPacket(new FacePacket { Detected = false, TimestampMs = 3 });
        Set("_faceLastGood", Environment.TickCount64 - 2000); Call("FaceTrackingTick");
        Check(!mixed.Recording, "Tracking loss stops the take for review");
        mixed.Review(false); mixed.Close(); Set("_recordingWindow", null);
        var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FaceMapping-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(folder);
        try
        {
            settings = new(); settings.Mappings[0].Neutral = 12; settings.Save(folder);
            Check(FaceTrackingSettings.Load(folder).Mappings[0].Neutral == 12, "Mapping calibration survives save/load");
        }
        finally { System.IO.File.Delete(System.IO.Path.Combine(folder, "FaceTracking.json")); System.IO.Directory.Delete(folder); }
    }
}
