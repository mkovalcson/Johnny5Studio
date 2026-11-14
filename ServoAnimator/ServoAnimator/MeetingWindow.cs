using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using NAudio.Wave;

namespace ServoAnimator;

internal sealed class MeetingSettings
{
    public int Version { get; set; } = 1;
    public int Source { get; set; } = 2;
    public bool UseSavedScene { get; set; }
    public string SceneFile { get; set; } = "";
    public string Microphone { get; set; }
    public double Gain { get; set; } = 4;
    public double Gate { get; set; } = .02;
    public double ReleaseMs { get; set; } = 180;
    internal BackgroundLibraryItem ReadScene(string root)
    {
        if (!UseSavedScene) return null;
        if (string.IsNullOrWhiteSpace(SceneFile)) throw new InvalidDataException("Choose a saved scene for the meeting.");
        return SavedSceneCommand.Read(root, SceneFile);
    }
    internal static int SourceFor(bool faceTracking, int controller) => faceTracking
        ? controller switch { 1 => 3, 2 => 4, _ => 2 }
        : controller switch { 1 => 0, 2 => 1, _ => -1 };
}
internal readonly record struct MeetingDeviceState(bool Ready, string Message, bool Active = true);
internal sealed record MeetingInputState(MeetingDeviceState Model, MeetingDeviceState Camera, MeetingDeviceState Controller);
internal interface IMeetingAudioInput : IDisposable
{
    string[] Devices();
    void Start(int device);
    float TakePeak();
    string Error { get; }
    bool Clipped { get; }
}
internal sealed class MeetingAudioInput : IMeetingAudioInput
{
    private readonly TakeAudioCapture _capture = new();
    public string[] Devices() => Enumerable.Range(0, WaveIn.DeviceCount).Select(i => WaveIn.GetCapabilities(i).ProductName).ToArray();
    public void Start(int device) => _capture.Monitor(device, bufferMilliseconds: 10);
    public float TakePeak() => _capture.TakePeak();
    public string Error => _capture.Error;
    public bool Clipped => _capture.Clipped;
    public void Dispose() => _capture.Dispose();
}

internal sealed partial class MeetingWindow : Window
{
    public MeetingSettings Settings { get; }
    internal BackgroundLibraryItem SelectedScene { get; private set; }
    public int DeviceIndex { get; private set; } = -1;
    internal RobotHeadView PreviewView { get; } = new(deferModelLoad: true);
    private readonly string _root, _settingsPath;
    private readonly BackgroundLibraryItem _currentSetup;
    private readonly Action<bool> _previewWebcam;
    private readonly Action<RobotHeadView> _previewPose;
    private readonly Func<int, bool, MeetingInputState> _inputReadiness;
    private readonly IMeetingAudioInput _audio;
    private readonly MeetingMouthEnvelope _mouth = new();
    private readonly DispatcherTimer _readinessTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly Dictionary<string, (long Size, long Modified, UrdfScene Scene)> _models = new(StringComparer.OrdinalIgnoreCase);
    private List<BackgroundLibraryItem> _sceneItems;
    private bool _started, _closed, _sceneReady;
    private int _previewRevision;
    private string _sceneError = "Preview opens when this window is shown.", _cameraError, _microphoneError;
    private TimeSpan _lastMeterFrame = TimeSpan.MinValue;
    private float _meterPeak;
    private int SelectedSource => MeetingSettings.SourceFor(_face.IsChecked == true, _controller.SelectedIndex);

    public MeetingWindow(string root, Action<bool> previewWebcam = null,
        Func<int, bool, MeetingInputState> inputReadiness = null, BackgroundLibraryItem currentSetup = null,
        Action<RobotHeadView> configurePreview = null, Action<RobotHeadView> previewPose = null, IMeetingAudioInput audio = null)
    {
        _root = root; _settingsPath = Path.Combine(root, "MeetingSettings.json");
        Settings = File.Exists(_settingsPath) ? JsonSerializer.Deserialize<MeetingSettings>(File.ReadAllText(_settingsPath)) ?? new() : new();
        if (Settings.Version != 1) throw new InvalidDataException("Unsupported meeting settings version.");
        _previewWebcam = previewWebcam; _inputReadiness = inputReadiness; _previewPose = previewPose; _audio = audio ?? new MeetingAudioInput();
        _currentSetup = currentSetup ?? DefaultSetup(root);
        HelpSystem.EnableContextHelp(this, "meeting-mode");
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/AnimationEditorPlayer;component/MeetingResources.xaml", UriKind.Absolute) });
        Title = "Meeting Configuration"; Width = 1080; Height = 800; MinWidth = 900; MinHeight = 660; FontSize = 14; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "AppBackground"); SetResourceReference(ForegroundProperty, "PrimaryText");
        PreviewView.ConfigureMeetingPreview(null, root); configurePreview?.Invoke(PreviewView);
        BuildLayout();
        _face.IsChecked = Settings.Source >= 2;
        _controller.SelectedIndex = Settings.Source switch { 0 or 3 => 1, 1 or 4 => 2, _ => 0 };
        _useCurrent.IsChecked = !Settings.UseSavedScene; _useSaved.IsChecked = Settings.UseSavedScene;
        _sceneItems = BackgroundLibrary.List(root, out string sceneErrors, scenes: true);
        _scenes.ItemsSource = _sceneItems;
        _scenes.SelectedItem = _sceneItems.FirstOrDefault(i => i.FileName == Settings.SceneFile);
        if (_scenes.SelectedItem == null && !string.IsNullOrWhiteSpace(Settings.SceneFile))
            try { var scene = SavedSceneCommand.Read(root, Settings.SceneFile); _sceneItems.Add(scene); _scenes.Items.Refresh(); _scenes.SelectedItem = scene; }
            catch (Exception ex) { _sceneDetails.Text = ex.Message; }
        if (!string.IsNullOrEmpty(sceneErrors)) _sceneDetails.ToolTip = sceneErrors;
        _scenePicker.IsEnabled = Settings.UseSavedScene;
        _currentHint.Visibility = Settings.UseSavedScene ? Visibility.Collapsed : Visibility.Visible;
        _sceneDetails.Visibility = string.IsNullOrWhiteSpace(_sceneDetails.Text) ? Visibility.Collapsed : Visibility.Visible;
        _useCurrent.Checked += (_, _) => SceneChanged(); _useSaved.Checked += (_, _) => SceneChanged();
        _scenes.SelectionChanged += (_, _) => SceneChanged();
        _face.Checked += (_, _) => SourceChanged(); _face.Unchecked += (_, _) => SourceChanged(); _controller.SelectionChanged += (_, _) => SourceChanged();
        _microphone.SelectionChanged += (_, _) => StartMicrophone();
        _readinessTimer.Tick += (_, _) => UpdateReadiness();
        Loaded += (_, _) =>
        {
            _started = true; PreviewSource(); RefreshMicrophones(); QueueScenePreview();
            UrdfRenderLoop.Current.AddTargets(PreviewFrame); _readinessTimer.Start();
        };
        Closed += (_, _) =>
        {
            _closed = true; _previewRevision++; _readinessTimer.Stop(); UrdfRenderLoop.Current.RemoveTargets(PreviewFrame);
            _audio.Dispose(); _mouth.Reset(); PreviewView.ReleasePackageMedia();
        };
        UpdateReadiness();
    }
    private static BackgroundLibraryItem DefaultSetup(string root)
    {
        var model = UrdfModelSettings.Load(root); string config = Path.Combine(root, "URDFconfig.json");
        return new() { Name = "Current editor setup", RobotModelFile = model.SelectedFile, Settings = new()
        { Background = File.Exists(config) ? UrdfConfiguration.Load(config).Presentation.Clone() : new(), Framing = model.Framing.GetValueOrDefault(model.SelectedFile)?.Clone() ?? new() } };
    }
    private void SceneChanged()
    {
        _scenePicker.IsEnabled = _useSaved.IsChecked == true;
        _currentHint.Visibility = _useCurrent.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        _sceneDetails.Text = (_scenes.SelectedItem as BackgroundLibraryItem)?.Description ?? "Choose a saved scene to preview.";
        _sceneDetails.Visibility = string.IsNullOrWhiteSpace(_sceneDetails.Text) ? Visibility.Collapsed : Visibility.Visible;
        _sceneReady = false;
        if (_started) QueueScenePreview();
        UpdateReadiness();
    }
    private void QueueScenePreview()
    {
        int revision = ++_previewRevision; _sceneReady = false; _sceneError = "Loading scene preview…"; _previewNotice.Text = _sceneError;
        UpdateReadiness();
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { if (!_closed && revision == _previewRevision) RefreshScenePreview(); }));
    }
    internal void RefreshScenePreview()
    {
        _sceneReady = false;
        try
        {
            var scene = _useSaved.IsChecked == true
                ? _scenes.SelectedItem is BackgroundLibraryItem selected ? SavedSceneCommand.Read(_root, selected.FileName) : throw new InvalidDataException("Choose a saved scene.")
                : _currentSetup;
            if (string.IsNullOrWhiteSpace(scene.RobotModelFile) || !File.Exists(scene.RobotModelFile)) throw new FileNotFoundException("Choose an available robot in URDF Scene Setup.");
            if (scene.Settings?.Framing == null) throw new InvalidDataException("This scene has no saved camera view. Save a complete scene in URDF Scene Setup.");
            scene.Settings.Validate(_root);
            var file = new FileInfo(scene.RobotModelFile);
            if (!_models.TryGetValue(file.FullName, out var cached) || cached.Size != file.Length || cached.Modified != file.LastWriteTimeUtc.Ticks)
            { cached = (file.Length, file.LastWriteTimeUtc.Ticks, UrdfScene.Load(file.FullName)); _models[file.FullName] = cached; }
            PreviewView.PrepareBackdrop(scene.Settings.Background);
            if (!ReferenceEquals(PreviewView.CurrentModelScene, cached.Scene)) PreviewView.InstallModel(file.FullName, cached.Scene, scene.Settings.Framing);
            PreviewView.SetTimelineBackground(scene.Settings.Background);
            PreviewView.ApplyFraming(scene.Settings.Framing); PreviewView.RememberSavedFraming();
            _previewPose?.Invoke(PreviewView);
            _sceneName.Text = _useSaved.IsChecked == true ? scene.Name : "Current editor setup";
            _robotName.Text = "Robot: " + Path.GetFileNameWithoutExtension(file.Name); _robotName.ToolTip = ConfigPathService.Reference(_root, file.FullName);
            _backgroundName.Text = "Background: " + DescribeBackground(scene.Settings.Background);
            _cameraName.Text = _useSaved.IsChecked == true ? "Camera: saved scene view" : "Camera: current editor view";
            _sceneName.ToolTip = _robotName.Text + "\n" + _backgroundName.Text + "\n" + _cameraName.Text;
            _sceneReady = true; _sceneError = null; _previewNotice.Text = "Stopping restores your editor setup.";
            PreviewView.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            _sceneError = ex.Message; _previewNotice.Text = ex.Message; PreviewView.Visibility = Visibility.Hidden;
            _sceneName.Text = "Scene needs attention"; _robotName.Text = ""; _backgroundName.Text = ""; _cameraName.Text = "";
        }
        UpdateReadiness();
    }
    private static string DescribeBackground(UrdfPresentationSettings settings) => settings.BackgroundMode switch
    {
        UrdfBackgroundMode.Urdf3D => "3D scenery · " + Path.GetFileNameWithoutExtension(settings.Stage.ModelFile),
        UrdfBackgroundMode.Image => "Image · " + Path.GetFileName(settings.BackgroundImage),
        UrdfBackgroundMode.MovingImage => "Moving image · " + Path.GetFileName(settings.BackgroundImage),
        UrdfBackgroundMode.Color => "Solid color · " + settings.BackgroundColor,
        _ => "Studio"
    };
    private void SourceChanged() { if (_started) PreviewSource(); UpdateReadiness(); }
    private void PreviewSource()
    {
        _cameraError = null;
        try { _previewWebcam?.Invoke(_face.IsChecked == true); }
        catch (Exception ex) { _cameraError = ex.Message; }
        UpdateReadiness();
    }
    internal void RefreshMicrophones()
    {
        string preferred = _microphone.SelectedItem as string ?? Settings.Microphone;
        try
        {
            var devices = _audio.Devices(); _microphoneError = null;
            _microphone.ItemsSource = devices; _microphone.SelectedIndex = Array.IndexOf(devices, preferred);
            if (_microphone.SelectedIndex < 0 && string.IsNullOrWhiteSpace(preferred) && devices.Length == 1) _microphone.SelectedIndex = 0;
            if (_microphone.SelectedIndex < 0) _microphoneError = devices.Length == 0 ? "No microphone found. Connect one and select Refresh." : string.IsNullOrWhiteSpace(preferred) ? "Choose a microphone." : "Saved microphone unavailable. Choose another device or Refresh.";
        }
        catch (Exception ex) { _audio.Dispose(); DeviceIndex = -1; _microphoneError = ex.Message; }
        UpdateReadiness();
    }
    private void StartMicrophone()
    {
        DeviceIndex = -1; _audio.Dispose(); _mouth.Reset(); _meterPeak = 0; _lastMeterFrame = TimeSpan.MinValue; _level.Value = 0;
        PreviewView.PreviewMeetingMouth(0); _microphoneError = null;
        if (_microphone.SelectedIndex >= 0)
            try { _audio.Start(_microphone.SelectedIndex); DeviceIndex = _microphone.SelectedIndex; }
            catch (Exception ex) { _microphoneError = ex.Message; }
        _meterText.Text = "Speak to test your mouth lights."; UpdateReadiness();
    }
    private void PreviewFrame(TimeSpan frameTime)
    {
        if (_closed) return;
        if (_sceneReady) _previewPose?.Invoke(PreviewView);
        float peak = DeviceIndex >= 0 ? _audio.TakePeak() : 0;
        bool working = DeviceIndex >= 0 && _microphoneError == null && _audio.Error == null;
        if (!working) _mouth.Reset();
        PreviewView.PreviewMeetingMouth(working ? _mouth.Advance(peak, Settings, frameTime) : 0);
        _meterPeak = Math.Max(_meterPeak, peak);
        if (_lastMeterFrame == TimeSpan.MinValue || (frameTime - _lastMeterFrame).TotalMilliseconds >= 80)
        {
            _level.Value = _meterPeak; _level.Foreground = _audio.Clipped ? Brushes.OrangeRed : Brushes.LimeGreen;
            _meterText.Text = !working ? "Select an available microphone." : $"Peak {(_meterPeak > 0 ? (20 * Math.Log10(_meterPeak)).ToString("0.0") : "−∞")} dBFS" + (_audio.Clipped ? " · CLIPPING — lower your microphone input level" : " · Speak to test");
            _meterPeak = 0; _lastMeterFrame = frameTime;
        }
    }
    internal void UpdateReadiness()
    {
        var state = _inputReadiness?.Invoke(SelectedSource, _useSaved.IsChecked == true)
            ?? new MeetingInputState(new(true, "Ready"), new(true, _face.IsChecked == true ? "Camera preview starts when opened" : "Off", _face.IsChecked == true),
                new(true, _controller.SelectedIndex == 0 ? "Not selected" : "Ready", _controller.SelectedIndex != 0));
        var scene = new MeetingDeviceState(_sceneReady && state.Model.Ready, _sceneError ?? (_sceneReady ? state.Model.Message : "Choose a scene to preview."));
        var camera = _cameraError == null ? state.Camera : new MeetingDeviceState(false, _cameraError);
        var microphone = new MeetingDeviceState(DeviceIndex >= 0 && _microphoneError == null && _audio.Error == null,
            _microphoneError ?? _audio.Error ?? (DeviceIndex >= 0 ? "Connected · Speak to test" : "Choose a microphone."));
        DisplayState(_sceneState, scene); DisplayState(_cameraState, camera); DisplayState(_controllerState, state.Controller); DisplayState(_microphoneState, microphone);
        _movementMessage.Text = SelectedSource < 0 ? "Turn on Face tracking or choose a controller." : "Uses your saved face-tracking and controller mappings.";
        _movementMessage.Foreground = SelectedSource < 0 ? Brushes.Orange : (Brush)FindResource("SecondaryText");
        _start.IsEnabled = SelectedSource >= 0 && scene.Ready && camera.Ready && state.Controller.Ready && microphone.Ready;
        _start.ToolTip = _start.IsEnabled ? "Start the meeting and save these settings." : SelectedSource < 0 ? _movementMessage.Text : new[] { scene, camera, state.Controller, microphone }.First(s => !s.Ready).Message;
        _readySummary.Text = _start.IsEnabled ? "Ready to start" : "Complete the items above to start";
    }
    private static void DisplayState(TextBlock text, MeetingDeviceState state)
    { text.Text = state.Message; text.Foreground = !state.Active ? Brushes.LightSlateGray : state.Ready ? Brushes.LightGreen : Brushes.Orange; }
    private void SaveSettings()
    {
        if (SelectedSource < 0) throw new InvalidDataException("Turn on Face tracking or choose a controller before saving.");
        Settings.Source = SelectedSource; Settings.Microphone = _microphone.SelectedItem as string ?? Settings.Microphone;
        Settings.UseSavedScene = _useSaved.IsChecked == true;
        if (_scenes.SelectedItem is BackgroundLibraryItem scene) Settings.SceneFile = scene.FileName;
        SelectedScene = Settings.ReadScene(_root);
        File.WriteAllText(_settingsPath + ".tmp", ConfigPathService.Serialize(Settings, _root, new JsonSerializerOptions { WriteIndented = true })); File.Move(_settingsPath + ".tmp", _settingsPath, true);
    }
    private void SaveClicked(object sender, RoutedEventArgs e)
    {
        try { if (!CommitNumbers()) return; SaveSettings(); ShowMessage("Meeting settings saved.", false); }
        catch (Exception ex) { ShowMessage(ex.Message, true); }
    }
    private void StartClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!CommitNumbers()) return;
            UpdateReadiness(); if (!_start.IsEnabled) throw new InvalidOperationException(_start.ToolTip?.ToString());
            SaveSettings(); DialogResult = true;
        }
        catch (Exception ex) { ShowMessage(ex.Message, true); }
    }
    private void ShowMessage(string text, bool error) { _message.Text = text; _message.Foreground = error ? Brushes.Orange : Brushes.LightGreen; }
}
