using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace ServoAnimator;

public partial class MainWindow
{
    private bool _hardwarePanelCollapsed, _movieTimelineCollapsed;

    private void HardwarePanelToggle_Click(object sender, RoutedEventArgs e) => SetHardwarePanelCollapsed(!_hardwarePanelCollapsed);
    private void MoviePanelToggle_Click(object sender, RoutedEventArgs e) => SetMovieTimelineCollapsed(!_movieTimelineCollapsed);

    private void SetHardwarePanelCollapsed(bool collapsed)
    {
        _hardwarePanelCollapsed = collapsed;
        HardwareScroll.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        HardwareHeaderCaption.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        HardwareEditorColumn.Width = new GridLength(collapsed ? 38 : 140);
        HardwarePanelToggleIcon.Data = Geometry.Parse(collapsed
            ? "M 6,0 L 0,4 L 6,8 Z"
            : "M 0,0 L 6,4 L 0,8 Z");
        SetPanelToggleLabel(HardwarePanelToggle, collapsed ? "Show Hardware panel" : "Hide Hardware panel");
    }

    private void SetMovieTimelineCollapsed(bool collapsed)
    {
        _movieTimelineCollapsed = collapsed;
        MovieTimelineBody.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        CollapsedMovieSummary.Visibility = collapsed ? Visibility.Visible : Visibility.Collapsed;
        MoviePanelToggleIcon.Data = Geometry.Parse(collapsed
            ? "M 0,6 L 4,0 L 8,6 Z"
            : "M 0,0 L 8,0 L 4,6 Z");
        SetPanelToggleLabel(MoviePanelToggle, collapsed ? "Show Movie Timeline" : "Hide Movie Timeline");
        PositionMoviePanelToggle();
    }

    private void PositionMoviePanelToggle()
    {
        if (MoviePanelToggle == null || MovieTimeline == null) return;
        bool besideNewSequence = !_movieTimelineCollapsed && MovieTimeline.CanCreateSequence;
        MoviePanelToggle.HorizontalAlignment = besideNewSequence ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        double left = besideNewSequence ? Math.Clamp(MovieTimeline.NewSequenceButtonRightX + 2,
            0, Math.Max(0, MovieTimeline.ActualWidth - MoviePanelToggle.Width)) : 0;
        MoviePanelToggle.Margin = new Thickness(left, 0, 0, 0);
    }

    private static void SetPanelToggleLabel(Button button, string label)
    {
        button.ToolTip = label;
        AutomationProperties.SetName(button, label);
    }
}
