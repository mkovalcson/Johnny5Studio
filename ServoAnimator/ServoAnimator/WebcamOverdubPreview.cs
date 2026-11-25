using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;

namespace ServoAnimator;

internal sealed class WebcamOverdubPreview : Window
{
    private readonly Func<RobotHeadView> _source;
    private readonly Func<double> _time;
    private readonly double _timelineEnd, _start, _end;
    private readonly Viewport3D[] _views = [new(), new(), new()];
    private readonly ModelVisual3D[] _models = [new(), new(), new()];
    private readonly PerspectiveCamera[] _cameras = [new(), new(), new()];
    private readonly Border[] _backdrops = [new(), new(), new()];
    private readonly TextBlock _position = new();
    private readonly Button _record = new() { Content = "Start take (F9)", Padding = new Thickness(13, 6, 13, 6) };
    private readonly Button _close = new() { Content = "Cancel", Padding = new Thickness(13, 6, 13, 6), Margin = new Thickness(8, 0, 0, 0) };
    private readonly Grid _tracks = new();
    private readonly Line _playhead = new() { Stroke = Brushes.White, StrokeThickness = 2, IsHitTestVisible = false };
    private readonly TextBlock _poseStatus = new() { Text = "Ready · existing pose at the take start" };
    private TimeSpan _lastRender = TimeSpan.MinValue;
    private bool _recording;

    internal WebcamOverdubPreview(string choices, Func<RobotHeadView> source, Func<double> time,
        double start, double end, double originalEnd, Action toggle,
        ControllerKind? controller = null, Action<ControllerKind> showController = null)
    {
        _source = source; _time = time; _start = start; _end = end;
        _timelineEnd = Math.Max(end, originalEnd);
        Title = "Webcam Overdub";
        Width = 1150; Height = 745; MinWidth = 680; MinHeight = 475;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "AppBackground");
        SetResourceReference(ForegroundProperty, "PrimaryText");

        var layout = new DockPanel { Margin = new Thickness(12) };
        Content = layout;
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        header.Children.Add(new TextBlock { Text = choices, FontSize = 15, TextWrapping = TextWrapping.Wrap });
        header.Children.Add(_poseStatus);
        if (controller is ControllerKind kind && showController != null)
        {
            var mapping = new Button
            {
                Content = kind == ControllerKind.Xbox ? "Show Xbox Controller View" : "Show Steam Controller View",
                HorizontalAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 7, 0, 0)
            };
            mapping.Click += (_, _) => showController(kind);
            header.Children.Add(mapping);
        }
        DockPanel.SetDock(header, Dock.Top); layout.Children.Add(header);

        var footer = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(footer, Dock.Bottom); layout.Children.Add(footer);
        var transport = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(_record); actions.Children.Add(_close);
        DockPanel.SetDock(actions, Dock.Right); transport.Children.Add(actions);
        _position.VerticalAlignment = VerticalAlignment.Center;
        transport.Children.Add(_position);
        DockPanel.SetDock(transport, Dock.Bottom); footer.Children.Add(transport);

        var timeline = new Grid();
        timeline.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        timeline.ColumnDefinitions.Add(new ColumnDefinition());
        var labels = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
        labels.Children.Add(new TextBlock { Text = "Existing timeline", Height = 19 });
        labels.Children.Add(new TextBlock { Text = "Face overdub", Height = 19 });
        timeline.Children.Add(labels);
        Grid.SetColumn(_tracks, 1); timeline.Children.Add(_tracks);
        footer.Children.Add(timeline);

        var panes = new Grid();
        panes.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42, GridUnitType.Star), MinWidth = 200 });
        panes.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        panes.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58, GridUnitType.Star), MinWidth = 200 });
        panes.RowDefinitions.Add(new RowDefinition());
        panes.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
        panes.RowDefinitions.Add(new RowDefinition());
        layout.Children.Add(panes);
        AddPane("Front · Square to shoulders", 0, 0, 0);
        AddPane("Robot POV · At eye height", 1, 2, 0);
        AddPane("Wide · Full stage", 2, 0, 2);
        var divider = new GridSplitter { Width = 8, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        Grid.SetColumn(divider, 1); Grid.SetRowSpan(divider, 3); panes.Children.Add(divider);

        void AddPane(string label, int index, int row, int column)
        {
            var pane = new DockPanel();
            pane.SetResourceReference(Panel.BackgroundProperty, "PanelBackground");
            var caption = new TextBlock { Text = label, Padding = new Thickness(8, 5, 8, 5), FontWeight = FontWeights.SemiBold };
            DockPanel.SetDock(caption, Dock.Top); pane.Children.Add(caption);
            _cameras[index].FieldOfView = ShotView.HorizontalFov * 180 / Math.PI;
            _cameras[index].NearPlaneDistance = .01;
            _cameras[index].FarPlaneDistance = 5000;
            _views[index].Camera = _cameras[index];
            _views[index].Children.Add(_models[index]);
            _backdrops[index].Child = _views[index]; pane.Children.Add(_backdrops[index]);
            Grid.SetColumn(pane, column); Grid.SetRow(pane, row);
            if (index == 2) Grid.SetRowSpan(pane, 3);
            panes.Children.Add(pane);
        }

        _tracks.Children.Add(MakeRange(0, originalEnd, 2, Color.FromRgb(138, 118, 192)));
        _tracks.Children.Add(MakeRange(start, end, 21, Color.FromRgb(49, 155, 189)));
        _tracks.Children.Add(_playhead);
        _record.Click += (_, _) => toggle();
        _close.Click += (_, _) => Close();
        PreviewKeyDown += (_, e) =>
        {
            if (e.IsRepeat) return;
            if (e.Key == Key.F9) toggle();
            else if (e.Key == Key.Escape) Close();
            else return;
            e.Handled = true;
        };
        Loaded += (_, _) => CompositionTarget.Rendering += Render;
        Closed += (_, _) =>
        {
            CompositionTarget.Rendering -= Render;
            foreach (var model in _models) model.Content = null;
        };
    }

    private Grid MakeRange(double start, double end, double top, Color color)
    {
        var range = new Grid { Height = 13, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, top, 0, 0) };
        range.SetResourceReference(Panel.BackgroundProperty, "ControlBackground");
        foreach (double width in new[] { start, Math.Max(0, end - start), Math.Max(0, _timelineEnd - end) })
            range.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width, GridUnitType.Star) });
        var fill = new Border { Background = new SolidColorBrush(color) };
        Grid.SetColumn(fill, 1); range.Children.Add(fill);
        return range;
    }

    internal void SetRecording(bool recording)
    {
        _recording = recording;
        _record.Content = recording ? "Stop take (F9)" : "Start take (F9)";
        _close.Content = recording ? "Close and review" : "Cancel";
        _poseStatus.Text = recording ? "● Recording · combined timeline and webcam pose" : "Ready · existing pose at the take start";
    }

    private void Render(object sender, EventArgs e)
    {
        if (!IsVisible || WindowState == WindowState.Minimized || e is not RenderingEventArgs frame ||
            (_lastRender != TimeSpan.MinValue && frame.RenderingTime - _lastRender < TimeSpan.FromMilliseconds(30))) return;
        _lastRender = frame.RenderingTime;
        var source = _source();
        for (int i = 0; i < _views.Length; i++)
        {
            var root = source?.OverdubSceneRoot;
            if (!ReferenceEquals(_models[i].Content, root)) _models[i].Content = root;
            if (source != null && !ReferenceEquals(_backdrops[i].Background, source.Background))
                _backdrops[i].Background = source.Background;
        }
        source?.FrameWebcamOverdub(_views, _cameras);
        double time = _recording ? Math.Clamp(_time(), _start, _end) : _start;
        _position.Text = $"Playhead  {TimeSpan.FromSeconds(time):mm\\:ss\\.fff}";
        _playhead.X1 = _playhead.X2 = _tracks.ActualWidth * Math.Clamp(time / Math.Max(1, _timelineEnd), 0, 1);
        _playhead.Y2 = _tracks.ActualHeight;
    }
}

public sealed partial class RobotHeadView
{
    internal Model3D OverdubSceneRoot => _sceneLightingRoot;

    internal void FrameWebcamOverdub(Viewport3D[] views, PerspectiveCamera[] cameras)
    {
        if (_scene == null || _presentationModel == null || _presentationModel.Bounds.IsEmpty) return;
        var transform = _presentationModel.Transform;
        Vector3 World(Point3D p) => ToVector(transform.Transform(p));
        Vector3 Link(string name, Vector3 fallback) =>
            _scene.TryTransformLinkPoint(name, new Point3D(), out var local) ? World(local) : fallback;
        float Aspect(int i) => (float)(Math.Max(1, views[i].ActualWidth) / Math.Max(1, views[i].ActualHeight));
        var body = _presentationModel.Bounds;
        var center = new Vector3((float)(body.X + body.SizeX / 2), (float)(body.Y + body.SizeY / 2), (float)(body.Z + body.SizeZ / 2));
        var forward = Direction(transform.Transform(new Vector3D(1, 0, 0)));
        var left = Link(CameraShots.LeftShoulder, center);
        var right = Link(CameraShots.RightShoulder, center);
        var shoulders = (left + right) / 2;
        var front = Vector3.Cross(left - right, Vector3.UnitZ);
        float heading = CameraShots.Heading(front.LengthSquared() > 1e-8f ? front : forward);
        var headPoints = _scene.GetWorldVisualBoundsPoints(LibraryPoseThumbnailLinks).Select(World).ToList();
        if (left != right)
        {
            float halfWidth = Vector3.Distance(left, right) / 2 + .13f;
            var side = Vector3.Normalize(left - right) * halfWidth;
            headPoints.Add(shoulders + side - Vector3.UnitZ * .14f);
            headPoints.Add(shoulders - side - Vector3.UnitZ * .14f);
        }
        var frontShot = CameraShots.Fit(headPoints.Count > 0 ? headPoints : Corners(body), heading, Aspect(0));
        Set(cameras[0], frontShot.Eye, frontShot.Target, Vector3.UnitZ);

        if (_scene.TryTransformLinkPoint("head_link", new Point3D(), out var origin) &&
            _scene.TryTransformLinkPoint("head_link", new Point3D(1, 0, 0), out var positiveX) &&
            _scene.TryTransformLinkPoint("head_link", new Point3D(0, 0, 1), out var positiveZ))
        {
            var gaze = Vector3.Normalize(World(positiveX) - World(origin));
            var up = Vector3.Normalize(World(positiveZ) - World(origin));
            var eye = (Link("left_eye_pop_link", World(origin)) + Link("right_eye_pop_link", World(origin))) / 2;
            // Move just beyond the lenses so Johnny's own eye geometry does not fill this view.
            float lensFront = headPoints.Count > 0 ? headPoints.Max(p => Vector3.Dot(p - eye, gaze)) : .07f;
            eye += gaze * Math.Max(.07f, lensFront + .015f);
            Set(cameras[1], eye, eye + gaze, up);
        }
        else
        {
            var gaze = Vector3.Normalize(forward);
            var eye = center + gaze * (float)Math.Max(body.SizeX, body.SizeY) + Vector3.UnitZ * (float)(body.SizeZ / 3);
            Set(cameras[1], eye, eye + gaze, Vector3.UnitZ);
        }

        var wideBounds = body;
        float wideHeading = heading;
        if (_stage != null && _stageModel != null && _sceneLightingRoot.Children.Contains(_stageModel))
        {
            wideBounds.Union(_stageModel.Bounds);
            wideHeading = CameraShots.Heading(Direction(_stageModel.Transform.Transform(new Vector3D(1, 0, 0))));
        }
        var wideShot = CameraShots.Fit(Corners(wideBounds), wideHeading, Aspect(2), .13f);
        Set(cameras[2], wideShot.Eye, wideShot.Target, Vector3.UnitZ);

        static Vector3 Direction(Vector3D p) => new((float)p.X, (float)p.Y, (float)p.Z);
        static Vector3 ToVector(Point3D p) => new((float)p.X, (float)p.Y, (float)p.Z);
        static IEnumerable<Vector3> Corners(Rect3D bounds)
        {
            var min = new Vector3((float)bounds.X, (float)bounds.Y, (float)bounds.Z);
            return CameraShots.Corners(min, min + new Vector3((float)bounds.SizeX, (float)bounds.SizeY, (float)bounds.SizeZ));
        }
        static void Set(PerspectiveCamera camera, Vector3 eye, Vector3 target, Vector3 up)
        {
            camera.Position = new Point3D(eye.X, eye.Y, eye.Z);
            var direction = target - eye;
            camera.LookDirection = new Vector3D(direction.X, direction.Y, direction.Z);
            camera.UpDirection = new Vector3D(up.X, up.Y, up.Z);
        }
    }
}
