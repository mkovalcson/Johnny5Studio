using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ServoAnimator;

internal static partial class Program
{
    private static void MeetingChecks()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var app = new App(); app.InitializeComponent(); DisableTestStartup(app); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        typeof(Application).GetField("_startupUri", flags).SetValue(app, null);
        var window = new MainWindow(); // Never show: Loaded connects hardware.
        object Call(string name, params object[] args) => typeof(MainWindow).GetMethod(name, flags).Invoke(window, args);
        void Set(string name, object value) => typeof(MainWindow).GetField(name, flags).SetValue(window, value);
        var settings = new MeetingSettings(); Set("_meetingSettings", settings); Set("_meetingActive", true);
        Check((bool)Call("ControllerOutputAllowed", false) && !(bool)Call("ControllerOutputAllowed", true), "Meeting permits virtual output and blocks physical output");
        foreach (int source in Enumerable.Range(0, 5))
        {
            settings.Source = source;
            Check((bool)Call("ControllerFocusAllowed", ControllerKind.Xbox) == (source is 0 or 3), "Only selected Xbox input works in background");
            Check((bool)Call("ControllerFocusAllowed", ControllerKind.Steam) == (source is 1 or 4), "Only selected Steam input works in background");
        }
        var view = (RobotHeadView)window.FindName("EmbeddedHeadView");
        typeof(RobotHeadView).GetField("_hostIsDocked", flags).SetValue(view, false);
        var background = view.Background;
        var status = (FrameworkElement)typeof(RobotHeadView).GetField("_status", flags).GetValue(view);
        var visibility = status.Visibility;
        var shot = new UrdfFraming { HorizontalPercent = 18, VerticalPercent = 21, Yaw = .35, Pitch = .2, Distance = 2.1, SpinDegrees = 720 };
        view.ApplyCameraState(0, 0, 1.15); // Consume startup framing, as the loaded viewer does.
        view.ApplyFraming(shot);
        view.SetMeetingView(true, Brushes.Green);
        Check(status.Visibility == Visibility.Collapsed && view.Background == Brushes.Green, "Clean meeting view hides overlay and applies background");
        Check(view.CaptureCurrentFraming().Yaw == shot.Yaw && view.CaptureCurrentFraming().Distance == shot.Distance && view.CaptureCurrentFraming().HorizontalPercent == shot.HorizontalPercent, "Meeting clean view preserves camera and framing for non-3D backgrounds");
        view.SetMeetingView(false);
        Check(status.Visibility == visibility && view.Background == background, "Meeting restores prior view");
        Call("StopMeeting");
        Check((string)((Button)window.FindName("MeetingButton")).Content == "Meeting", "Stop restores button");

        string folder = Path.Combine(Path.GetTempPath(), "MeetingChecks-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            var recorder = new ControllerRecordingWindow(0, folder, new[] { RobotControls.NeckTiltLeft });
            var source = (ComboBox)typeof(ControllerRecordingWindow).GetField("_source", flags).GetValue(recorder);
            source.SelectedIndex = 3;
            var parents = (Dictionary<ServoNames, CheckBox>)typeof(ControllerRecordingWindow).GetField("_controls", flags).GetValue(recorder);
            var children = (Dictionary<(ServoNames, RobotControls), CheckBox>)typeof(ControllerRecordingWindow).GetField("_children", flags).GetValue(recorder);
            Check(((StackPanel)parents[ServoNames.NeckNodUp].Content).Children.OfType<Image>().Last().Visibility == Visibility.Visible, "Shared parent receives webcam icon");
            Check(((StackPanel)children[(ServoNames.NeckNodUp, RobotControls.NeckTiltRight)].Content).Children.OfType<Image>().Last().Visibility == Visibility.Collapsed, "Unshared child has no webcam icon");
            typeof(ControllerRecordingWindow).GetMethod("SaveRecordingSettings", flags).Invoke(recorder, null);
            var restored = new ControllerRecordingWindow(0, folder);
            Check(((ComboBox)typeof(ControllerRecordingWindow).GetField("_source", flags).GetValue(restored)).SelectedIndex == 3, "Recording source persists");
            Set("_folders", new FolderSettings { ConfigFolder = folder });
            string robot = Path.Combine(folder, "editor.urdf"), otherRobot = Path.Combine(folder, "meeting.urdf");
            const string model = "<robot name='test'><link name='base'><visual><geometry><box size='0.2 0.2 0.2'/></geometry></visual></link></robot>";
            File.WriteAllText(robot, model); File.WriteAllText(otherRobot, model);
            var editorScene = new BackgroundLibraryItem { RobotModelFile = robot, Settings = new BackdropCommand { Background = new UrdfPresentationSettings { BackgroundMode = UrdfBackgroundMode.Color, BackgroundColor = "#112233" }, Framing = shot } };
            Call("ApplyLibraryScene", editorScene);
            var savedScene = new BackgroundLibraryItem { Name = "Meeting scene", RobotModelFile = otherRobot, Settings = new BackdropCommand { Background = new UrdfPresentationSettings { BackgroundMode = UrdfBackgroundMode.Color, BackgroundColor = "#334455" }, Framing = new UrdfFraming { HorizontalPercent = -31, Distance = 1.7, Yaw = -.4, Pitch = .1 } } };
            BackgroundLibrary.Save(folder, savedScene, true);
            var defaults = File.ReadAllText(Path.Combine(folder, "UrdfModelSettings.json"));
            var presentation = File.ReadAllText(Path.Combine(folder, "URDFconfig.json"));
            var sceneFile = File.ReadAllText(Path.Combine(folder, savedScene.FileName));
            settings.UseSavedScene = true; settings.SceneFile = savedScene.FileName;
            Check(settings.ReadScene(folder).RobotModelFile == otherRobot, "Saved meeting scene resolves full scene assets");
            settings.UseSavedScene = false; settings.SceneFile = "missing.json";
            Check(settings.ReadScene(folder) == null, "Current setup ignores a stale saved-scene selection");
            settings.UseSavedScene = true;
            bool missingRejected = false;
            try { settings.ReadScene(folder); } catch (FileNotFoundException) { missingRejected = true; }
            Check(missingRejected, "Missing saved meeting scene is rejected rather than silently replaced");
            Call("ApplyMeetingScene", savedScene); Set("_meetingActive", true);
            Check(view.LoadedModelFile == otherRobot && view.CaptureCurrentFraming().HorizontalPercent == -31 && view.CaptureBackground().BackgroundColor == "#334455", "Meeting temporarily installs the complete saved scene");
            Call("ApplyUrdfTimelinePosition");
            Check(view.LoadedModelFile == otherRobot && view.CaptureCurrentFraming().HorizontalPercent == -31, "Timeline updates cannot replace the meeting scene");
            Call("StopMeeting");
            Check(view.LoadedModelFile == robot && view.CaptureCurrentFraming().HorizontalPercent == shot.HorizontalPercent && view.CaptureCurrentFraming().Distance == shot.Distance && view.CaptureBackground().BackgroundColor == "#112233", "Stopping restores editor robot, background and camera");
            Check(File.ReadAllText(Path.Combine(folder, "UrdfModelSettings.json")) == defaults && File.ReadAllText(Path.Combine(folder, "URDFconfig.json")) == presentation && File.ReadAllText(Path.Combine(folder, savedScene.FileName)) == sceneFile, "Meeting does not overwrite editor defaults or saved scene files");
            Call("ApplyMeetingScene", editorScene); Set("_meetingActive", true);
            view.ApplyUrdfCommand(ServoNames.URDFSlide, 80); Call("StopMeeting");
            Check(view.CaptureCurrentFraming().HorizontalPercent == shot.HorizontalPercent, "Current-setup meeting restores framing after live movement");
            string room = Path.Combine(folder, "room.urdf"); File.WriteAllText(room, model);
            editorScene.Settings.Background.BackgroundMode = UrdfBackgroundMode.Urdf3D;
            editorScene.Settings.Background.Stage.ModelFile = room;
            editorScene.Settings.Background.Stage.RobotFraming = shot.Clone();
            Call("ApplyLibraryScene", editorScene);
            view.ApplyStageMotion(7, 3, 35);
            var stageCamera = view.CaptureStage(); stageCamera.ViewZoomPercent = 125; view.ApplyStageCameraSettings(stageCamera);
            Call("ApplyMeetingScene", savedScene); Set("_meetingActive", true); Call("StopMeeting");
            var restoredStage = view.CaptureStage();
            Check(restoredStage != null && restoredStage.RobotX == 7 && restoredStage.RobotY == 3 && restoredStage.HeadingDegrees == 35 && restoredStage.ViewZoomPercent == 125, "Meeting restores evaluated room placement and background camera");
            string brokenRobot = Path.Combine(folder, "broken.urdf"); File.WriteAllText(brokenRobot, "<invalid");
            bool brokenRejected = false;
            try { Call("ApplyMeetingScene", new BackgroundLibraryItem { RobotModelFile = brokenRobot, Settings = savedScene.Settings }); }
            catch (TargetInvocationException) { brokenRejected = true; }
            Call("StopMeeting");
            Check(brokenRejected && view.LoadedModelFile == robot && view.CaptureStage()?.RobotX == 7, "Failed meeting model load preserves the editor scene");
            var meeting = new MeetingWindow(folder);
            var choices = SceneDescendants(meeting).OfType<RadioButton>().ToArray();
            Check(choices.Length == 2 && choices.Single(c => Equals(c.Content, "Use current editor setup")).IsChecked == true, "Meeting defaults to current setup and offers a saved scene");
            choices.Single(c => Equals(c.Content, "Use saved scene")).IsChecked = true;
            var sceneChoice = SceneDescendants(meeting).OfType<ComboBox>().Single(c => c.Name == "SceneChoice"); sceneChoice.SelectedItem = sceneChoice.Items[0];
            SceneDescendants(meeting).OfType<Button>().Single(b => Equals(b.Content, "Save settings")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var persisted = System.Text.Json.JsonSerializer.Deserialize<MeetingSettings>(File.ReadAllText(Path.Combine(folder, "MeetingSettings.json")));
            Check(persisted.UseSavedScene && persisted.SceneFile == savedScene.FileName, "Meeting scene choice persists without starting devices");
            var restoredMeeting = new MeetingWindow(folder);
            Check(SceneDescendants(restoredMeeting).OfType<RadioButton>().Single(c => Equals(c.Content, "Use saved scene")).IsChecked == true, "Meeting restores its saved scene selection");
            string previews = PreviewDirectory("meeting-previews"); Directory.CreateDirectory(previews);
            restoredMeeting.RefreshScenePreview();
            RenderControl((FrameworkElement)restoredMeeting.Content, Path.Combine(previews, "MeetingConfiguration.png"), 1044, 724);
            MeetingLayoutChecks(folder, editorScene, savedScene, window);
            Check(!window.IsLoaded && !meeting.IsLoaded && !recorder.IsLoaded, "Checks never open hardware-owning windows");
        }
        finally
        {
            string cleanupPath = Path.GetFullPath(folder);
            if (!string.Equals(Path.GetDirectoryName(cleanupPath), Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(cleanupPath).StartsWith("MeetingChecks-", StringComparison.Ordinal))
                throw new InvalidOperationException("Refusing to clean a directory outside the generated Meeting test folder.");
            Directory.Delete(cleanupPath, recursive: true);
        }
    }
}
