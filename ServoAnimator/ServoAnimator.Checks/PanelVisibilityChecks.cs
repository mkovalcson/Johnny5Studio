using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using ServoAnimator;

internal static partial class Program
{
    private static void PanelVisibilityChecks()
    {
        var app = new App(); app.InitializeComponent(); DisableTestStartup(app);
        var window = new MainWindow();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        void Call(string method, bool value) => typeof(MainWindow).GetMethod(method, flags).Invoke(window, new object[] { value });
        Call("SetHardwarePanelCollapsed", false); Call("SetMovieTimelineCollapsed", false);
        var root = (FrameworkElement)window.Content;
        // Preserve ElementName lookup when hosting the window's visuals separately.
        NameScope.SetNameScope(root, NameScope.GetNameScope(window));
        window.Content = null;
        using var presentation = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("Panel visibility verification")
        { Width = 1400, Height = 900, WindowStyle = unchecked((int)0x80000000) });
        presentation.RootVisual = root;
        void Arrange()
        {
            root.Measure(new Size(1400, 900)); root.Arrange(new Rect(0, 0, 1400, 900)); root.UpdateLayout();
        }
        Arrange();
        var urdf = (Border)window.FindName("RobotHeadEmbeddedBorder");
        var divider = (GridSplitter)window.FindName("CommandsTimelineSplitter");
        double Bottom(FrameworkElement element) => element.TranslatePoint(new Point(0, element.ActualHeight), root).Y;
        Check(Math.Abs(urdf.TranslatePoint(new Point(), root).Y) < 1 && Math.Abs(Bottom(urdf) - Bottom(divider)) < 1,
            "Docked URDF is flush with the client top and the bottom of the horizontal resize bar");
        typeof(MainWindow).GetMethod("EmbeddedHeadView_VerticalResizeDeltaRequested", flags).Invoke(window, new object[] { 50d }); Arrange();
        Check(Bottom(urdf) >= Bottom(divider) + 49, "Manually stretching the docked URDF still extends below the resize bar");
        typeof(MainWindow).GetField("_embeddedUrdfHeightPixels", flags).SetValue(window, 0d);
        typeof(MainWindow).GetMethod("ApplyEmbeddedUrdfHeight", flags).Invoke(window, null); Arrange();
        Check(window.FindName("FocusControlButton") == null && window.FindName("FocusControlMenuItem") is MenuItem,
            "Focus Control is available in Config without a redundant Hardware button");
        var safeguard = (ToggleButton)window.FindName("CollisionSafeguardButton");
        var deviceStatus = (Border)window.FindName("HardwareDeviceStatusArea");
        Check(safeguard.TranslatePoint(new Point(0, safeguard.ActualHeight), root).Y <= deviceStatus.TranslatePoint(new Point(), root).Y,
            "Collision Safeguard sits above the hardware device status area");
        var hardware = (ScrollViewer)window.FindName("HardwareScroll");
        var hardwareColumn = (ColumnDefinition)window.FindName("HardwareEditorColumn");
        var movie = (Grid)window.FindName("MovieTimelineBody");
        var moviePanel = (Border)window.FindName("MovieTimelinePanel");
        var hardwareToggle = (Button)window.FindName("HardwarePanelToggle");
        var movieToggle = (Button)window.FindName("MoviePanelToggle");
        double expandedMovieHeight = moviePanel.ActualHeight;
        hardwareToggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Arrange();
        Check(hardware.Visibility == Visibility.Collapsed && hardwareColumn.ActualWidth == 38 && hardwareToggle.IsVisible,
            $"Hardware collapse frees horizontal space and retains its expand button (content {hardware.Visibility}, width {hardwareColumn.ActualWidth}, toggle visible {hardwareToggle.IsVisible})");
        movieToggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Arrange();
        Check(movie.Visibility == Visibility.Collapsed && moviePanel.ActualHeight < expandedMovieHeight - 50 && movieToggle.IsVisible,
            "Movie collapse frees vertical space and retains its expand button");
        var fileText = (TextBlock)window.FindName("MovieFileText");
        var statusText = (TextBlock)window.FindName("MovieStatusText");
        fileText.Text = "Demonstration* — 42 s";
        statusText.Text = "Webcam · URDF ON · HW OFF · ⓘ";
        root.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind); Arrange();
        var collapsedFile = (TextBlock)window.FindName("CollapsedMovieFileText");
        var collapsedStatus = (TextBlock)window.FindName("CollapsedMovieStatusText");
        Check(collapsedFile.IsVisible && collapsedFile.Text == fileText.Text && collapsedStatus.IsVisible && collapsedStatus.Text == statusText.Text,
            $"Collapsed Movie header retains movie details and robot-control statuses (file visible {collapsedFile.IsVisible}, '{collapsedFile.Text}' / '{fileText.Text}', status visible {collapsedStatus.IsVisible}, '{collapsedStatus.Text}' / '{statusText.Text}')");
        statusText.Text = "Timeline · URDF ON · HW ON · ⓘ";
        root.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
        Check(collapsedStatus.Text == statusText.Text, "Status changes remain live while the Movie Timeline is collapsed");
        var collapsedStatusButton = (Button)window.FindName("CollapsedRobotControlStatusButton");
        collapsedStatusButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Check(collapsedStatusButton.ContextMenu?.IsOpen == true && ReferenceEquals(collapsedStatusButton.ContextMenu.PlacementTarget, collapsedStatusButton),
            "Collapsed status opens its details menu at the visible status button");
        collapsedStatusButton.ContextMenu.IsOpen = false;
        Check(hardwareToggle.ToolTip.ToString().StartsWith("Show") && movieToggle.ToolTip.ToString().StartsWith("Show"),
            "Collapsed controls describe how to restore the sections");
        string previews = PreviewDirectory("layout-previews"); System.IO.Directory.CreateDirectory(previews);
        RenderControl(root, System.IO.Path.Combine(previews, "Panels-collapsed.png"), 1400, 900);
        hardwareToggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); movieToggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Arrange();
        Check(hardware.Visibility == Visibility.Visible && hardwareColumn.ActualWidth == 140 && movie.Visibility == Visibility.Visible && Math.Abs(moviePanel.ActualHeight - expandedMovieHeight) < 1,
            "Both sections restore their original content and dimensions");
        var timeline = (MovieTimelineView)window.FindName("MovieTimeline");
        timeline.SetItems(new[] { new MovieSequenceItem { FilePath = "Introduction.json", DurationSeconds = 12 },
            new MovieSequenceItem { FilePath = "Demonstration.json", DurationSeconds = 30 } });
        timeline.CanCreateSequence = true; timeline.SelectedIndex = 0;
        ((Border)window.FindName("MovieEmptyStatePanel")).Visibility = Visibility.Collapsed;
        timeline.ZoomToFit(); Arrange();
        var scroll = (ScrollBar)window.FindName("MovieScroll");
        var controlsRow = (Grid)window.FindName("MovieControlsRow");
        Check(scroll.Visibility == Visibility.Collapsed && Math.Abs(controlsRow.TranslatePoint(new Point(), timeline).Y - timeline.ActualHeight) < 1,
            "Fitted movie controls sit directly below the blocks without an unused scrollbar gap");
        Check(Math.Abs(movieToggle.Margin.Left - timeline.NewSequenceButtonRightX - 2) < 1,
            "Collapse control sits just to the right of the New Sequence button without overlap");
        var newSequence = (Button)typeof(MovieTimelineView).GetField("_newSequenceButton", flags).GetValue(timeline);
        Check(Math.Abs(newSequence.TranslatePoint(new Point(0, newSequence.ActualHeight), timeline).Y - (timeline.ActualHeight - 4)) < 1 &&
            Math.Abs(newSequence.ActualHeight - (timeline.ActualHeight - 8)) < 1 && newSequence.ActualWidth == 72 && Equals(newSequence.Content, "+New Sequence") &&
            movieToggle.TranslatePoint(new Point(), timeline).Y < 1,
            "Narrow two-line New Sequence matches sequence block height while the collapse arrow stays at the top");
        timeline.ZoomBy(2); Arrange();
        Check(scroll.Visibility == Visibility.Visible && Math.Abs(controlsRow.TranslatePoint(new Point(), timeline).Y - timeline.ActualHeight - scroll.ActualHeight) < 1,
            "Overflowing movies reserve only the space required for the horizontal scrollbar");
        timeline.ZoomToFit(); Arrange();
        Check(scroll.Visibility == Visibility.Collapsed, "Fitting the movie removes the scrollbar and reclaims its space");
        RenderControl(root, System.IO.Path.Combine(previews, "Panels-expanded.png"), 1400, 900);
        var menus = ((Grid)((Border)window.FindName("MainMenuPanel")).Child).Children.OfType<Menu>().Single().Items.OfType<MenuItem>();
        var config = menus.Single(menu => menu.Header.ToString() == "_Config");
        var headers = config.Items.OfType<MenuItem>().Select(item => item.Header.ToString()).ToList();
        Check(headers.IndexOf("Webcam Face Tracking…") + 1 == headers.IndexOf("Xbox Controller Mapping…"),
            "Webcam Face Tracking appears directly above Xbox Controller Mapping");
        window.Content = null;
    }
}
