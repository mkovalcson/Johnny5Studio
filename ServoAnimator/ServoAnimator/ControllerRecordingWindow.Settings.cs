using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ServoAnimator;

internal sealed class RecordingWindowSettings
{
    public int Version { get; set; } = 1;
    public int Source { get; set; }
    public bool Overdub { get; set; } = true;
    public bool Group { get; set; } = true;
    public bool RecordAudio { get; set; }
    public bool AutomaticallyConvertVoice { get; set; }
    public string MicrophoneName { get; set; }
    public int MicrophoneOccurrence { get; set; }
    public string Duration { get; set; } = "60";
    public double Smoothing { get; set; } = 60;
    public double Tolerance { get; set; } = 1;
    public bool ShowChildren { get; set; }
    public List<string> Controls { get; set; } = new();
    public List<string> Children { get; set; } = new();
    public List<string> FaceOverrideTargets { get; set; } = new();
}

internal sealed partial class ControllerRecordingWindow
{
    private readonly string _settingsPath;
    private readonly Dictionary<string, CheckBox> _overrideChecks = new();
    internal HashSet<RobotControls> FaceOverrideControls => _overrideChecks.Where(p => p.Value.IsChecked == true)
        .SelectMany(p => ControllerTargets.TryServo(p.Key, out var servo, out var child)
            ? child.HasValue ? new[] { child.Value } : ServoConfiguration.ControlsFor(servo) : Array.Empty<RobotControls>()).ToHashSet();
    private FrameworkElement WithOverride(CheckBox selection, ServoNames servo, RobotControls? child = null)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        string key = child.HasValue ? $"Child:{servo}:{child}" : $"Servo:{servo}";
        var check = new CheckBox { Width = 26, VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Allow the controller to take this control from the webcam while Face Tracking Override is on", Visibility = Visibility.Collapsed };
        _overrideChecks[key] = check;
        row.Children.Add(check); row.Children.Add(selection); return row;
    }
    private readonly HashSet<RobotControls> _faceOwned;
    private readonly List<StackPanel> _childPanels = new();
    private readonly List<(ServoNames Servo, RobotControls? Child, TextBlock Text, Image Icon)> _webcamLabels = new();
    private readonly TextBlock _webcamPrecedence = new()
    {
        Text = "Left checkbox: controller takeover when Face Tracking Override is on. Right checkbox: record this control. Otherwise webcam takes precedence for shared controls.",
        TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 8), Foreground = Brushes.Gold
    };
    private string _preferredMicrophone;
    private int _preferredMicrophoneOccurrence;

    private static ImageSource WebcamIcon()
    {
        var drawing = new DrawingGroup();
        var pen = new Pen(Brushes.Gold, 1.5);
        drawing.Children.Add(new GeometryDrawing(Brushes.DarkSlateGray, pen, new RectangleGeometry(new Rect(1, 1, 14, 10), 3, 3)));
        drawing.Children.Add(new GeometryDrawing(Brushes.LightSkyBlue, null, new EllipseGeometry(new Point(8, 6), 3, 3)));
        drawing.Children.Add(new GeometryDrawing(null, pen, Geometry.Parse("M8,11 L8,14 M4,14 L12,14")));
        drawing.Freeze(); return new DrawingImage(drawing);
    }
    private void UpdateWebcamLabels()
    {
        bool mixed = _source.SelectedIndex is 3 or 4;
        foreach (var check in _overrideChecks.Values) check.Visibility = mixed ? Visibility.Visible : Visibility.Collapsed;
        _webcamPrecedence.Visibility = mixed ? Visibility.Visible : Visibility.Collapsed;
        foreach (var label in _webcamLabels)
        {
            bool overlap = mixed && (label.Child.HasValue ? _faceOwned.Contains(label.Child.Value)
                : ServoConfiguration.ControlsFor(label.Servo).Any(_faceOwned.Contains));
            label.Icon.Visibility = overlap ? Visibility.Visible : Visibility.Collapsed;
            label.Text.FontWeight = overlap ? FontWeights.Bold : FontWeights.Normal;
            if (overlap) label.Text.Foreground = Brushes.Gold;
            else label.Text.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryText");
        }
    }
    private void LoadRecordingSettings()
    {
        if (_settingsPath == null || !File.Exists(_settingsPath)) return;
        try
        {
            var settings = JsonSerializer.Deserialize<RecordingWindowSettings>(File.ReadAllText(_settingsPath));
            if (settings == null || settings.Version != 1) throw new InvalidDataException("Unsupported recording settings version.");
            _source.SelectedIndex = Math.Clamp(settings.Source, 0, 4);
            _overdub.IsChecked = settings.Overdub; _groupRecorded.IsChecked = settings.Group;
            _recordAudio.IsChecked = settings.RecordAudio; _duration.Text = settings.Duration ?? "60";
            _convertVoice.IsChecked = settings.AutomaticallyConvertVoice;
            if (double.IsFinite(settings.Smoothing)) _smooth.Value = Math.Clamp(settings.Smoothing, 0, 300);
            if (double.IsFinite(settings.Tolerance)) _tolerance.Value = Math.Clamp(settings.Tolerance, 0, 5);
            foreach (var pair in _controls) pair.Value.IsChecked = settings.Controls?.Contains(pair.Key.ToString()) == true;
            foreach (var pair in _children) pair.Value.IsChecked = settings.Children?.Contains($"{pair.Key.Servo}:{pair.Key.Control}") == true;
            foreach (var pair in _overrideChecks) pair.Value.IsChecked = settings.FaceOverrideTargets?.Contains(pair.Key) == true;
            foreach (var panel in _childPanels) panel.Visibility = settings.ShowChildren ? Visibility.Visible : Visibility.Collapsed;
            _preferredMicrophone = settings.MicrophoneName; _preferredMicrophoneOccurrence = settings.MicrophoneOccurrence;
        }
        catch (Exception ex) { SetStatus("Could not load recording settings: " + ex.Message); }
    }
    private bool SaveRecordingSettings()
    {
        if (_settingsPath == null) return true;
        try
        {
            var settings = new RecordingWindowSettings
            {
                Source = _source.SelectedIndex, Overdub = _overdub.IsChecked == true, Group = GroupRecordedCommands,
                RecordAudio = RecordAudio, Duration = _duration.Text, Smoothing = Smoothing, Tolerance = Tolerance,
                AutomaticallyConvertVoice = AutomaticallyConvertVoice,
                ShowChildren = _childPanels.Any(p => p.Visibility == Visibility.Visible),
                MicrophoneName = _preferredMicrophone, MicrophoneOccurrence = _preferredMicrophoneOccurrence,
                Controls = _controls.Where(p => p.Value.IsChecked == true).Select(p => p.Key.ToString()).ToList(),
                Children = _children.Where(p => p.Value.IsChecked == true).Select(p => $"{p.Key.Servo}:{p.Key.Control}").ToList(),
                FaceOverrideTargets = _overrideChecks.Where(p => p.Value.IsChecked == true).Select(p => p.Key).ToList()
            };
            string temporary = _settingsPath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, _settingsPath, overwrite: true);
            return true;
        }
        catch (Exception ex) { SetStatus("Could not save recording settings: " + ex.Message); return false; }
    }
}
