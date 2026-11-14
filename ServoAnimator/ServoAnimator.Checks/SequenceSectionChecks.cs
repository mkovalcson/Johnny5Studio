using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using ServoAnimator;

internal static partial class Program
{
    private static void SequenceSectionChecks()
    {
        var app = new App(); app.InitializeComponent(); DisableTestStartup(app);
        foreach (var servo in Enum.GetValues<ServoNames>())
            Check(ServoIconProvider.For(servo) != null, "Control icon exists: " + servo);
        Check(ReferenceEquals(ServoIconProvider.For(ServoNames.B_ShoulderPitchLeft), ServoIconProvider.For(ServoNames.B_ShoulderRotateLeft)) &&
            ReferenceEquals(ServoIconProvider.For(ServoNames.B_ShoulderPitchRight), ServoIconProvider.For(ServoNames.B_ShoulderRotateRight)),
            "Legacy shoulder pitch names reuse the correct green shoulder artwork");
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var window = new MainWindow();
        object Call(string name, params object[] args) => typeof(MainWindow).GetMethod(name, flags).Invoke(window, args);
        var servos = new[] { ServoNames.NeckTurn, ServoNames.B_ShoulderRotateLeft, ServoNames.URDFZoom };
        var doc = new AnimationDocument { Commands = servos.Select((s, lane) => new ServoCommand { Servo = s, OffsetSeconds = lane + 1, NumericValue = 0 }).ToList() };
        typeof(MainWindow).GetField("_doc", flags).SetValue(window, doc);
        Call("RefreshAfterEdit"); Call("ApplySplineSettings", servos); Call("RebuildSplineData");
        string original = JsonSerializer.Serialize(doc);
        var root = (FrameworkElement)window.Content; NameScope.SetNameScope(root, NameScope.GetNameScope(window)); window.Content = null;
        using var source = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("Sequence section verification")
        { Width = 1400, Height = 900, WindowStyle = unchecked((int)0x80000000) }); source.RootVisual = root;
        var picker = (ComboBox)window.FindName("TimelineLayoutPicker");
        var wave = (WaveformView)window.FindName("Waveform"); var spline = (SplineView)window.FindName("Spline");
        Check(picker.Items.Count == 5 && picker.Height == 30 && picker.Items.OfType<ComboBoxItem>().All(i => i.Content is string text && !text.Contains('\n')),
            "Timeline choices include both existing modes and three single-line section modes");
        Check(picker.Items.OfType<ComboBoxItem>().All(item => new System.Windows.Media.FormattedText((string)item.Content,
            System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new System.Windows.Media.Typeface(picker.FontFamily, picker.FontStyle, picker.FontWeight, picker.FontStretch), picker.FontSize,
            System.Windows.Media.Brushes.Black, 1).Width <= picker.Width - 26 - picker.Padding.Left - picker.Padding.Right),
            "Every layout choice fits the selected field on one line without clipping");
        for (int index = 0; index < 5; index++)
        {
            picker.SelectedIndex = index;
            root.Measure(new Size(1400, 900)); root.Arrange(new Rect(0, 0, 1400, 900)); root.UpdateLayout();
            int lane = index < 2 ? -1 : index - 2;
            Check(wave.LaneFilter == lane && spline.LaneFilter == lane && wave.ThreeLanes == (index == 1), "Picker applies the selected section: " + index);
            var sectionLabels = (Border)window.FindName("TimelineSectionLabels");
            Check(sectionLabels.Visibility == (index == 1 ? Visibility.Visible : Visibility.Collapsed), "Section labels appear only in the three-section view: " + index);
            var markers = ((IEnumerable)typeof(WaveformView).GetMethod("BuildMarkerVisuals", flags).Invoke(wave, new object[] { (float)wave.ActualWidth }))
                .Cast<object>().Select(m => (double)m.GetType().GetProperty("Time").GetValue(m)).Distinct().ToArray();
            Check(markers.Order().SequenceEqual(lane < 0 ? new[] { 1d, 2, 3 } : new[] { lane + 1d }), "Only selected-section command markers are rendered: " + index);
            Check(servos.Select((servo, category) => wave.IncludesServo(servo) == (lane < 0 || lane == category) && spline.IncludesServo(servo) == (lane < 0 || lane == category)).All(v => v),
                "Hidden sections cannot be selected as marker or spline controls: " + index);
            Check(wave.ShowsAudio == (lane is -1 or 0) && wave.ShowsVideo == (lane is -1 or 2), "Audio and video belong to the correct individual sections: " + index);
            if (index is 1 or 4)
            {
                string folder = PreviewDirectory("layout-previews"); System.IO.Directory.CreateDirectory(folder);
                RenderControl(root, System.IO.Path.Combine(folder, index == 1 ? "Sequence-section-labels.png" : "Sequence-scene-only.png"), 1400, 900);
            }
        }
        picker.SelectedIndex = 0;
        Check(JsonSerializer.Serialize(doc) == original, "Changing timeline sections preserves all commands and spline settings");
    }
}
