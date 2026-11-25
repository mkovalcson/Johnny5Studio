using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
    private double? _dragCommandTime;
    public Brush Accent { get; set; } = Brushes.MediumPurple;
    internal Func<string> SelectedGroupName { get; init; }
    internal Action<bool> SelectGroup;
    internal Action GroupMenu;
    internal Action<double> Seek;
    internal Action<double, double?> MoveGroup;
    internal Func<bool> IsEditingGroup { get; init; }
    internal Func<ServoCommand, bool> IsCommandSelected { get; init; }
    internal Func<ServoCommand, double> PreviewOffset { get; init; }
    internal Action<double, bool> SelectCommandList { get; init; }
    internal Action<double> EditCommandList { get; init; }
    internal NamedGroupTrack(WaveformView timeline, ServoCommand[] commands, Func<double> cursor, bool hidden)
    {
        _timeline = timeline; _commands = commands; _cursor = cursor; _hidden = hidden;
        ClipToBounds = true; Focusable = true; MinHeight = 40;
        const string hint = "Click to select this group; drag its command strip to move it in time. Right-click for group actions. Mouse wheel zooms the shared timeline.";
        var label = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 420 };
        ToolTip = new ToolTip { Content = label, Placement = PlacementMode.MousePoint, HorizontalOffset = 14, VerticalOffset = 12 };
        ToolTipOpening += (_, _) =>
        {
            string name = SelectedGroupName?.Invoke();
            label.Text = string.IsNullOrEmpty(name) ? hint : name;
            label.FontWeight = string.IsNullOrEmpty(name) ? FontWeights.Normal : FontWeights.SemiBold;
        };
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
        bool selected = SelectedGroupName?.Invoke() != null;
        bool editing = IsEditingGroup?.Invoke() == true;
        var positions = _commands.Select(c => (Time: c.OffsetSeconds + (PreviewOffset?.Invoke(c) ?? 0),
            Selected: IsCommandSelected?.Invoke(c) == true)).GroupBy(c => c.Time).ToArray();
        dc.PushOpacity(_hidden ? .35 : 1);
        double start = positions.Min(p => p.Key), end = positions.Max(p => p.Key);
        double left = Math.Max(0, X(start)), right = Math.Min(ActualWidth, Math.Max(X(start) + 6, X(end)));
        if (right > left)
            dc.DrawRectangle(Accent, selected && !editing ? new Pen(foreground, 1) : null, new Rect(left, 20, right - left, 12));
        var pen = new Pen(Accent, editing ? 3 : 2);
        var selectedPen = new Pen(foreground, 3);
        foreach (var position in positions)
        {
            double x = X(position.Key);
            if (x >= 0 && x <= ActualWidth)
                dc.DrawLine(editing && position.Any(p => p.Selected) ? selectedPen : pen, new Point(x, 16), new Point(x, 36));
        }

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
        base.OnMouseLeftButtonDown(e);
        Focus();
        var point = e.GetPosition(this);
        double time = Time(point.X);
        bool toggle = Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (IsEditingGroup?.Invoke() == true)
        {
            double mark = _commands.MinBy(c => Math.Abs(X(c.OffsetSeconds) - point.X)).OffsetSeconds;
            if (point.Y < 12 || Math.Abs(X(mark) - point.X) > 8) Seek?.Invoke(time);
            else if (e.ClickCount == 2) EditCommandList?.Invoke(mark);
            else
            {
                if (toggle || !_commands.Any(c => c.OffsetSeconds == mark && IsCommandSelected?.Invoke(c) == true))
                    SelectCommandList?.Invoke(mark, toggle);
                if (!toggle) StartDrag(point, mark);
            }
        }
        else if (toggle) SelectGroup?.Invoke(true);
        else
        {
            if (SelectedGroupName?.Invoke() == null) SelectGroup?.Invoke(false);
            double start = _commands.Min(c => c.OffsetSeconds), end = _commands.Max(c => c.OffsetSeconds);
            if (point.Y >= 16 && time >= start - 8 / _timeline.PixelsPerSecond && time <= end + 8 / _timeline.PixelsPerSecond)
                StartDrag(point, null);
            else Seek?.Invoke(time);
        }
        e.Handled = true;
    }

    private void StartDrag(Point point, double? commandTime)
    {
        _dragCommandTime = commandTime;
        _dragStart = point;
        if (CaptureMouse()) Cursor = Cursors.SizeWE;
        else _dragStart = null;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragStart is not { } start) return;
        _timeline.PreviewSelectedMarkerMove((e.GetPosition(this).X - start.X) / _timeline.PixelsPerSecond, _dragCommandTime);
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_dragStart == null) return;
        double delta = _timeline.SelectedMarkerMoveOffset;
        _dragStart = null;
        _timeline.CancelSelectedMarkerMove();
        ReleaseMouseCapture();
        Cursor = Cursors.Arrow;
        if (delta != 0) MoveGroup?.Invoke(delta, _dragCommandTime);
        else if (_dragCommandTime.HasValue) Seek?.Invoke(_dragCommandTime.Value);
        else InvalidateVisual();
        e.Handled = true;
    }
    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        if (_dragStart != null) _timeline.CancelSelectedMarkerMove();
        _dragStart = null;
        InvalidateVisual();
        Cursor = Cursors.Arrow;
        base.OnLostMouseCapture(e);
    }
    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e) { GroupMenu?.Invoke(); e.Handled = true; }
    protected override void OnMouseWheel(MouseWheelEventArgs e) { _timeline.ZoomBy(e.Delta>0 ? 1.2 : 1/1.2); e.Handled = true; }
}
