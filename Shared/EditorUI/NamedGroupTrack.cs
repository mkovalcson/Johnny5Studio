using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace ServoAnimator;

/// <summary>A group-specific command strip. Dragging operates on the group ID,
/// never all commands that happen to share the same timestamp.</summary>
internal sealed class NamedGroupTrack : FrameworkElement
{
    private readonly WaveformView _timeline;
    private readonly ServoCommand[] _commands;
    private readonly Func<double> _cursor;
    private readonly bool _hidden;
    private Point? _dragStart;
    private double _dragDelta;
    public Brush Accent { get; set; } = Brushes.MediumPurple;
    internal Action SelectGroup, GroupMenu;
    internal Action<double> Seek, MoveGroup;
    internal NamedGroupTrack(WaveformView timeline, ServoCommand[] commands, Func<double> cursor, bool hidden)
    {
        _timeline = timeline; _commands = commands; _cursor = cursor; _hidden = hidden;
        ClipToBounds = true; Focusable = true; MinHeight = 40;
        ToolTip = "Click to select this group; drag its command strip to move it in time. Right-click for group actions. Mouse wheel zooms the shared timeline.";
        SizeChanged += (_, _) => InvalidateVisual();
    }
    private double Origin => _timeline.IsLoaded ? _timeline.TranslatePoint(new Point(WaveformView.TimelineLeftInset, 0), this).X : WaveformView.TimelineLeftInset;
    private double X(double time) => Origin + (time - _timeline.ViewStart) * _timeline.PixelsPerSecond;
    private double Time(double x) => Math.Max(0, _timeline.ViewStart + (x-Origin) / _timeline.PixelsPerSecond);
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var background = TryFindResource("PanelBackground") as Brush ?? Brushes.DimGray;
        var foreground = TryFindResource("PrimaryText") as Brush ?? Brushes.White;
        dc.DrawRectangle(background, null, new Rect(RenderSize));
        if (_commands.Length == 0) return;
        double start = _commands.Min(c => c.OffsetSeconds) + _dragDelta, end = _commands.Max(c => c.OffsetSeconds) + _dragDelta;
        dc.PushOpacity(_hidden ? .35 : 1);
        double left = Math.Max(0, X(start)), right = Math.Min(ActualWidth, Math.Max(X(start)+6, X(end)));
        if (right > left) dc.DrawRectangle(Accent, null, new Rect(left, 20, right-left, 12));
        var pen = new Pen(Accent, 2);
        foreach (double t in _commands.Select(c => c.OffsetSeconds).Distinct())
        { double x = X(t + _dragDelta); if (x>=0 && x<=ActualWidth) dc.DrawLine(pen, new Point(x, 16), new Point(x, 36)); }
        dc.Pop();
        double step = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(.001, 75 / _timeline.PixelsPerSecond))));
        if (step * _timeline.PixelsPerSecond < 45) step *= 5;
        double first = Math.Ceiling(_timeline.ViewStart/step)*step;
        for (double t=first; X(t)<ActualWidth; t+=step)
        {
            if (X(t)<0) continue;
            var text = new FormattedText(t.ToString("0.##", CultureInfo.InvariantCulture)+"s", CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10, foreground, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(text, new Point(X(t)+2, 0));
            if (!double.IsFinite(t+step) || t+step<=t) break;
        }
        double cursor = X(_cursor());
        if (cursor>=0 && cursor<=ActualWidth) dc.DrawLine(new Pen(Brushes.Gold, 1.5), new Point(cursor,0), new Point(cursor,ActualHeight));
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e); Focus(); SelectGroup?.Invoke();
        var point = e.GetPosition(this); double t = Time(point.X);
        double a = _commands.Min(c=>c.OffsetSeconds), b = _commands.Max(c=>c.OffsetSeconds);
        if (point.Y >= 16 && t >= a-8/_timeline.PixelsPerSecond && t <= b+8/_timeline.PixelsPerSecond)
        { _dragStart = point; CaptureMouse(); }
        else Seek?.Invoke(t);
        e.Handled = true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragStart is not { } start) return;
        _dragDelta = Math.Max(-_commands.Min(c=>c.OffsetSeconds), (e.GetPosition(this).X-start.X)/_timeline.PixelsPerSecond);
        InvalidateVisual();
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_dragStart == null) return;
        double delta = _dragDelta; _dragStart = null; _dragDelta = 0; ReleaseMouseCapture();
        if (Math.Abs(delta) > .001) MoveGroup?.Invoke(delta); else InvalidateVisual();
        e.Handled = true;
    }
    protected override void OnLostMouseCapture(MouseEventArgs e) { _dragStart = null; _dragDelta = 0; InvalidateVisual(); base.OnLostMouseCapture(e); }
    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e) { GroupMenu?.Invoke(); e.Handled = true; }
    protected override void OnMouseWheel(MouseWheelEventArgs e) { _timeline.ZoomBy(e.Delta>0 ? 1.2 : 1/1.2); e.Handled = true; }
}
