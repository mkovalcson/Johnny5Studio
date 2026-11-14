using System.Windows.Controls;

namespace ServoAnimator
{
    public partial class MainWindow
    {
        private TimelineLayoutController _timelineLayout;
        internal static readonly TimelineLayoutMode[] TimelinePickerModes = { TimelineLayoutMode.Combined, TimelineLayoutMode.HeadBodyUrdf,
            TimelineLayoutMode.HeadOnly, TimelineLayoutMode.BodyOnly, TimelineLayoutMode.SceneOnly };
        private void InitializeTimelineLayout()
        {
            _timelineLayout = new TimelineLayoutController(EditorTimelineGrid, AudioTimelineArea, AudioPlotHost,
                SplinePlotHost, SplineAreaGrid, SplineLegendStrip, SplineArea, Spline, AudioTimelineRow, SplineTimelineRow, firstTimelineRow: 4);
            ApplyTimelineLayout();
        }
        private void TimelineLayout_Changed(object sender, SelectionChangedEventArgs e) => ApplyTimelineLayout();
        private void ApplyTimelineLayout()
        {
            if (_timelineLayout == null) return;
            int index = TimelineLayoutPicker.SelectedIndex;
            var mode = index >= 0 && index < TimelinePickerModes.Length ? TimelinePickerModes[index] : TimelineLayoutMode.Combined;
            _timelineLayout.LastSplineHeight = _lastSplineTimelineHeight;
            if (_timelineLayout.Mode != mode)
            { Waveform.SetMarkerSelection(Array.Empty<double>()); Waveform.ActiveLane = -1; }
            _timelineLayout.Apply(mode, SplineServosEnabled().Count > 0);
            _lastSplineTimelineHeight = _timelineLayout.LastSplineHeight;
            RefreshTimelineLegend();
            SyncSplineView();
        }
        private void RefreshTimelineLegend()
        {
            _legend.Clear();
            foreach (var servo in SplineServosEnabled().Where(s => Waveform.LaneFilter < 0 || TimelineLanes.For(s) == Waveform.LaneFilter))
                _legend.Add(new SplineLegendItem { Servo = servo, Name = servo.ToString(), Brush = BrushFor(servo),
                    Visible = !_lineVisible.TryGetValue(servo, out bool visible) || visible });
            SplineShowAllButton.Visibility = _legend.Any(item => !item.Visible) ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        }
    }
}
