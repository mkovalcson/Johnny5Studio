using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using ServoAnimator;

internal static partial class Program
{
    private sealed class MeetingTestAudio : IMeetingAudioInput
    {
        public string[] Available = { "Test microphone" };
        public string Failure;
        public bool IsClipped;
        public float Peak;
        public int Starts, Stops;
        public string[] Devices() => Available.ToArray();
        public void Start(int device) { Starts++; Failure = null; }
        public float TakePeak() { float p = Peak; Peak = 0; return p; }
        public string Error => Failure;
        public bool Clipped => IsClipped;
        public void Dispose() => Stops++;
    }
    private static void MeetingLayoutChecks(string root, BackgroundLibraryItem current, BackgroundLibraryItem saved, MainWindow main)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        string settingsPath = Path.Combine(root, "MeetingSettings.json");
        T Control<T>(MeetingWindow window, string name) where T : FrameworkElement => SceneDescendants(window).OfType<T>().Single(c => c.Name == name);
        void Click(MeetingWindow w, string text) => SceneDescendants(w).OfType<Button>().Single(b => Equals(b.Content, text)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var camera = new MeetingDeviceState(true, "Camera 0 · Tracking face");
        var controller = new MeetingDeviceState(true, "Xbox: Connected · Enabled on Start");
        var audio = new MeetingTestAudio();
        bool? cameraPreview = null;
        var editor = (RobotHeadView)main.FindName("EmbeddedHeadView"); var originalModel = editor.CurrentModelScene; var originalFrame = editor.CaptureCurrentFraming();
        string defaultsBefore = File.ReadAllText(Path.Combine(root, "UrdfModelSettings.json")), sceneBefore = File.ReadAllText(Path.Combine(root, saved.FileName));
        File.WriteAllText(settingsPath, JsonSerializer.Serialize(new MeetingSettings()));
        var meeting = new MeetingWindow(root, enabled => cameraPreview = enabled,
            (source, _) => new(new(true, "Preview ready"), source >= 2 ? camera : new(true, "Off", false), source is 0 or 1 or 3 or 4 ? controller with { Message = controller.Message.Replace("Xbox", source is 1 or 4 ? "Steam" : "Xbox") } : new(true, "Not selected", false)), currentSetup: current, audio: audio);
        var face = Control<CheckBox>(meeting, "FaceTracking"); var choice = Control<ComboBox>(meeting, "ControllerChoice"); var start = Control<Button>(meeting, "StartMeeting");
        Check(face.IsChecked == true && choice.SelectedIndex == 0, "Original webcam-only preference becomes Face tracking on with no controller");
        Check(!Control<Expander>(meeting, "MouthAdjustments").IsExpanded, "Detailed mouth settings start collapsed");
        Check(SceneDescendants(meeting).OfType<Expander>().Any(e => Equals(e.Header, "Connect to OBS / Teams…")), "OBS and Teams guidance is available without filling the main form");
        meeting.RefreshScenePreview(); meeting.RefreshMicrophones();
        Check(start.IsEnabled && audio.Starts == 1 && meeting.DeviceIndex == 0, "One available microphone is selected and monitored before starting");
        Check(meeting.PreviewView.LoadedModelFile == current.RobotModelFile && !ReferenceEquals(meeting.PreviewView.CurrentModelScene, originalModel), "Scene preview owns an independent robot model");
        Check(meeting.PreviewView.CaptureCurrentFraming().HorizontalPercent == current.Settings.Framing.HorizontalPercent && meeting.PreviewView.CaptureStage()?.RobotX == current.Settings.Background.Stage.RobotX,
            "Current-setup preview retains camera and evaluated room placement");
        Control<RadioButton>(meeting, "UseSavedScene").IsChecked = true;
        var scenes = Control<ComboBox>(meeting, "SceneChoice"); scenes.SelectedItem = scenes.Items.Cast<BackgroundLibraryItem>().Single(s => s.FileName == saved.FileName);
        meeting.RefreshScenePreview();
        Check(meeting.PreviewView.LoadedModelFile == saved.RobotModelFile && meeting.PreviewView.CaptureCurrentFraming().HorizontalPercent == saved.Settings.Framing.HorizontalPercent && meeting.PreviewView.CaptureBackground().BackgroundColor == saved.Settings.Background.BackgroundColor,
            "Selecting a saved scene previews its robot, background and camera before starting");
        Check(ReferenceEquals(editor.CurrentModelScene, originalModel) && editor.CaptureCurrentFraming().HorizontalPercent == originalFrame.HorizontalPercent &&
            File.ReadAllText(Path.Combine(root, "UrdfModelSettings.json")) == defaultsBefore && File.ReadAllText(Path.Combine(root, saved.FileName)) == sceneBefore,
            "Scene preview changes neither the editor view nor saved scene/default files");
        camera = new(false, "Camera disconnected"); meeting.UpdateReadiness();
        Check(!start.IsEnabled && Control<TextBlock>(meeting, "CameraReadiness").Text == "Camera disconnected", "Camera failure is shown in its own readiness row and blocks Start");
        camera = new(true, "Camera 0 · Tracking face"); controller = new(false, "Xbox: Connect the controller."); choice.SelectedIndex = 1; meeting.UpdateReadiness();
        Check(!start.IsEnabled && Control<TextBlock>(meeting, "ControllerReadiness").Text.Contains("Connect"), "Selected controller must be ready and its corrective action is visible");
        controller = new(true, "Xbox: Connected · Enabled on Start"); meeting.UpdateReadiness(); Check(start.IsEnabled, "Start becomes available once the selected controller connects");
        face.IsChecked = false; choice.SelectedIndex = 0; meeting.UpdateReadiness(); Check(!start.IsEnabled, "Turning off both movement inputs blocks Start");
        face.IsChecked = true;
        typeof(MeetingWindow).GetMethod("PreviewSource", flags).Invoke(meeting, null); Check(cameraPreview == true, "Face tracking switch requests camera preview");
        audio.Failure = "Microphone disconnected"; meeting.UpdateReadiness(); Check(!start.IsEnabled && Control<TextBlock>(meeting, "MicrophoneReadiness").Text == audio.Failure, "Microphone capture errors have an actionable readiness row");
        audio.Failure = null; meeting.UpdateReadiness();
        audio.Peak = .5f; audio.IsClipped = true; typeof(MeetingWindow).GetMethod("PreviewFrame", flags).Invoke(meeting, new object[] { TimeSpan.Zero });
        Check(Control<ProgressBar>(meeting, "MicrophoneLevel").Value == .5 && SceneDescendants(meeting).OfType<TextBlock>().Any(t => t.Text.Contains("CLIPPING")), "Live meter displays incoming audio peaks and clipping");
        var numbers = (List<(Slider Slider, TextBox Number, string Title)>)typeof(MeetingWindow).GetField("_numbers", flags).GetValue(meeting);
        numbers[0].Number.Text = "7.25"; numbers[1].Number.Text = ".035"; numbers[2].Number.Text = "240"; Click(meeting, "Save settings");
        var stored = JsonSerializer.Deserialize<MeetingSettings>(File.ReadAllText(settingsPath));
        Check(stored.Gain == 7.25 && stored.Gate == .035 && stored.ReleaseMs == 240, "Numeric mouth settings save precise values alongside sliders");
        string beforeBad = File.ReadAllText(settingsPath); numbers[0].Number.Text = "NaN"; Click(meeting, "Save settings");
        Check(File.ReadAllText(settingsPath) == beforeBad && Control<TextBlock>(meeting, "MeetingMessage").Text.Contains("enter a number"), "Invalid mouth input is explained and does not overwrite saved settings");
        Click(meeting, "Restore defaults"); Click(meeting, "Save settings");
        Check(meeting.Settings.Gain == 4 && meeting.Settings.Gate == .02 && meeting.Settings.ReleaseMs == 180, "Restore defaults resets all mouth sliders and numeric fields");
        for (int source = 0; source < 5; source++)
        {
            face.IsChecked = source >= 2; choice.SelectedIndex = source switch { 0 or 3 => 1, 1 or 4 => 2, _ => 0 }; Click(meeting, "Save settings");
            Check(JsonSerializer.Deserialize<MeetingSettings>(File.ReadAllText(settingsPath)).Source == source, "Separate inputs preserve original source mode " + source);
            var reopened = new MeetingWindow(root, currentSetup: current, audio: new MeetingTestAudio());
            Check(Control<CheckBox>(reopened, "FaceTracking").IsChecked == (source >= 2) && Control<ComboBox>(reopened, "ControllerChoice").SelectedIndex == choice.SelectedIndex,
                "Original source mode restores separate controls " + source); reopened.Close();
        }
        audio.Available = Array.Empty<string>(); meeting.RefreshMicrophones();
        Check(meeting.DeviceIndex == -1 && !start.IsEnabled && Control<TextBlock>(meeting, "MicrophoneReadiness").Text.Contains("No microphone"), "Refreshing after unplugging the microphone releases it and blocks Start");
        audio.Available = new[] { "Different microphone" }; meeting.RefreshMicrophones();
        Check(meeting.DeviceIndex == -1 && Control<TextBlock>(meeting, "MicrophoneReadiness").Text.Contains("Saved microphone unavailable"), "Missing preferred microphone does not silently switch to another input");
        Control<ComboBox>(meeting, "MicrophoneChoice").SelectedIndex = 0; meeting.UpdateReadiness(); Check(start.IsEnabled, "Choosing a replacement microphone restores readiness");
        Click(meeting, "Save settings"); string savedMessage = Control<TextBlock>(meeting, "MeetingMessage").Text;
        typeof(MeetingWindow).GetMethod("PreviewFrame", flags).Invoke(meeting, new object[] { TimeSpan.FromSeconds(1) });
        Check(Control<TextBlock>(meeting, "MeetingMessage").Text == savedMessage, "Meter updates do not erase save/error feedback");
        string previewFolder = PreviewDirectory("meeting-previews");
        RenderControl((FrameworkElement)meeting.Content, Path.Combine(previewFolder, "MeetingConfigurationReady.png"), 1044, 724);
        RenderControl((FrameworkElement)meeting.Content, Path.Combine(previewFolder, "MeetingConfigurationCompact.png"), 864, 584);
        var footerButton = Control<Button>(meeting, "StartMeeting");
        Check(footerButton.TranslatePoint(new Point(), (UIElement)meeting.Content).Y + footerButton.ActualHeight <= 584, "Meeting actions remain visible at minimum window height");
        string lastSave = File.ReadAllText(settingsPath); numbers[0].Slider.Value = 9; int stopped = audio.Stops; meeting.Close();
        Check(audio.Stops > stopped && File.ReadAllText(settingsPath) == lastSave && !main.IsLoaded, "Closing releases audio without saving pending changes or starting hardware");
        var modalAudio = new MeetingTestAudio { Available = new[] { "Different microphone" } };
        var modal = new MeetingWindow(root, inputReadiness: (_, _) => new(new(true, "Ready"), new(true, "Tracking face"), new(true, "Connected")), currentSetup: current, audio: modalAudio);
        bool enabledOnLoad = false;
        modal.Loaded += (_, _) => modal.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            enabledOnLoad = Control<Button>(modal, "StartMeeting").IsEnabled;
            Click(modal, "Start Meeting");
            if (modal.IsVisible) modal.Close();
        }));
        bool? accepted = modal.ShowDialog();
        Check(accepted == true && enabledOnLoad && modalAudio.Starts > 0 && modalAudio.Stops > 0 && modal.SelectedScene?.RobotModelFile == saved.RobotModelFile,
            "Real modal Start validates loaded preview/devices, saves the selected scene and closes its microphone monitor");
        Check(!main.IsLoaded, "Meeting configuration lifecycle checks never load the hardware-owning main window");
    }
}
