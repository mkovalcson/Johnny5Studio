using System.Windows;
using System.Windows.Controls;

namespace ServoAnimator
{
    internal enum TimelineLayoutMode { WaveformTop, SplineTop, Combined, HeadBodyUrdf, HeadOnly, BodyOnly, SceneOnly }

    /// <summary>Rearranges existing controls rather than duplicating their editing state.</summary>
    internal sealed class TimelineLayoutController
    {
        private readonly Grid _editor, _audioArea, _audioHost, _splineHost, _splineAreaGrid, _legend;
        private readonly Border _splineArea;
        private readonly FrameworkElement _sectionLabels;
        private readonly SplineView _spline;
        private readonly RowDefinition _audioRow, _splineRow;
        private readonly int _firstTimelineRow;
        private GridLength _separateAudioHeight;
        public TimelineLayoutMode Mode { get; private set; }
        public GridLength LastSplineHeight { get; set; } = new(190);
        private static bool IsCombined(TimelineLayoutMode mode) => mode is not (TimelineLayoutMode.WaveformTop or TimelineLayoutMode.SplineTop);
        public GridLength SavedAudioHeight => IsCombined(Mode) ? _separateAudioHeight : _audioRow.Height;
        public GridLength SavedSplineHeight => _splineArea.Visibility == Visibility.Visible
            ? _splineRow.Height : LastSplineHeight;

        public void RestoreHeights(GridLength audioHeight, GridLength splineHeight)
        {
            _audioRow.MaxHeight = _splineRow.MaxHeight = double.PositiveInfinity;
            // Restore both the visible rows and the hidden separate-mode sizes.
            // Do not let Apply capture measurements from the startup layout.
            _separateAudioHeight = audioHeight;
            LastSplineHeight = splineHeight;
            _audioRow.Height = IsCombined(Mode)
                ? new GridLength(1, GridUnitType.Star) : audioHeight;
            _splineRow.Height = _splineArea.Visibility == Visibility.Visible
                ? splineHeight : new GridLength(0);
        }

        public TimelineLayoutController(Grid editor, Grid audioArea, Grid audioHost, Grid splineHost,
            Grid splineAreaGrid, Grid legend, Border splineArea, SplineView spline,
            RowDefinition audioRow, RowDefinition splineRow, int firstTimelineRow = 2)
        {
            _editor = editor; _audioArea = audioArea; _audioHost = audioHost; _splineHost = splineHost;
            _splineAreaGrid = splineAreaGrid; _legend = legend; _splineArea = splineArea;
            _sectionLabels = audioArea.Children.OfType<FrameworkElement>().FirstOrDefault(e => e.Name == "TimelineSectionLabels");
            _spline = spline; _audioRow = audioRow; _splineRow = splineRow;
            _separateAudioHeight = audioRow.Height;
            _firstTimelineRow = firstTimelineRow;
        }

        private static void Move(UIElement element, Panel destination)
        {
            if (ReferenceEquals(System.Windows.Media.VisualTreeHelper.GetParent(element), destination)) return;
            if (System.Windows.Media.VisualTreeHelper.GetParent(element) is Panel parent) parent.Children.Remove(element);
            destination.Children.Add(element);
        }

        public void Apply(TimelineLayoutMode mode, bool hasSplines)
        {
            // Clear pixel-row caps before a row can switch back to star sizing.
            // The host reinstates the appropriate pixel limits after layout.
            _audioRow.MaxHeight = _splineRow.MaxHeight = double.PositiveInfinity;
            if (_splineArea.Visibility == Visibility.Visible && _splineRow.Height.Value > 0)
                LastSplineHeight = _splineRow.Height;
            if (!IsCombined(Mode) && IsCombined(mode))
                _separateAudioHeight = _audioRow.Height;
            if (IsCombined(Mode) && !IsCombined(mode))
                _audioRow.Height = _separateAudioHeight;
            Mode = mode;
            bool split = mode == TimelineLayoutMode.HeadBodyUrdf;
            if (_sectionLabels != null) _sectionLabels.Visibility = split ? Visibility.Visible : Visibility.Collapsed;
            bool combined = IsCombined(mode);
            int laneFilter = mode switch { TimelineLayoutMode.HeadOnly => 0, TimelineLayoutMode.BodyOnly => 1, TimelineLayoutMode.SceneOnly => 2, _ => -1 };
            _spline.LaneFilter = laneFilter;
            bool splineTop = mode == TimelineLayoutMode.SplineTop && hasSplines;
            var topRow = splineTop ? _splineRow : _audioRow;
            var bottomRow = splineTop ? _audioRow : _splineRow;
            if (!ReferenceEquals(_editor.RowDefinitions[_firstTimelineRow], topRow))
            {
                _editor.RowDefinitions.Remove(_audioRow);
                _editor.RowDefinitions.Remove(_splineRow);
                _editor.RowDefinitions.Insert(_firstTimelineRow, topRow);
                _editor.RowDefinitions.Add(bottomRow);
            }
            Grid.SetRow(_audioArea, splineTop ? _firstTimelineRow + 2 : _firstTimelineRow);
            Grid.SetRow(_splineArea, splineTop ? _firstTimelineRow : _firstTimelineRow + 2);
            Move(_legend, combined ? _audioArea : _splineAreaGrid);
            Move(_spline, combined ? _audioHost : _splineHost);
            Grid.SetRow(_legend, 0);
            Grid.SetColumn(_legend, combined ? 1 : 0);
            _spline.ThreeLanes = split;
            _spline.Margin = combined && !split ? new Thickness(0, 44, 0, 26) : new Thickness(0);
            Panel.SetZIndex(_spline, combined ? 1 : 0);
            _spline.CombinedMode = combined;
            if (_audioHost.Children.OfType<WaveformView>().FirstOrDefault() is WaveformView waveform)
            {
                waveform.CombinedPresentation = combined;
                waveform.ThreeLanes = split;
                waveform.LaneFilter = laneFilter;
                waveform.InvalidateVisual();
            }
            _spline.Visibility = Visibility.Visible;
            bool separateSpline = !combined && hasSplines;
            _splineArea.Visibility = separateSpline ? Visibility.Visible : Visibility.Collapsed;
            _splineRow.MinHeight = separateSpline ? 80 : 0;
            _splineRow.Height = separateSpline
                ? (_splineRow.Height.Value > 0 ? _splineRow.Height : LastSplineHeight) : new GridLength(0);
            if (combined) _audioRow.Height = new GridLength(1, GridUnitType.Star);
            _spline.InvalidateVisual();
        }
    }
}
