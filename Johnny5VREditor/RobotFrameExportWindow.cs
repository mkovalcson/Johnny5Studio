using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ServoAnimator;

/// <summary>Settings for File > Export Robot Frames. The size estimate multiplies the frame count by the average PNG
/// size of a few frames actually rendered with the chosen size and options.</summary>
internal sealed class RobotFrameExportWindow : Window
{
    private readonly double _sequenceEnd;
    private readonly string _sequencePath, _fallbackFolder;
    private readonly Func<RobotFrameExportSettings, (long Bytes, double Seconds)> _measure;
    private readonly TextBox _width = Box(), _height = Box(), _fps = Box(), _start = Box(), _end = Box(), _folder = new();
    private readonly CheckBox _toEnd = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
    private readonly CheckBox _robotOnly = new(), _matchVideo = new();
    private readonly TextBlock _estimate = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), FontWeight = FontWeights.SemiBold };
    private readonly TextBlock _status = new() { Foreground = Brushes.DarkRed, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    private long? _bytesPerFrame;
    private double _secondsPerFrame;
    private string _measuredFor;
    internal RobotFrameExportSettings Result { get; private set; }

    internal RobotFrameExportWindow(RobotFrameExportSettings settings, double sequenceEnd, string sequencePath, string fallbackFolder,
        Func<RobotFrameExportSettings, (long Bytes, double Seconds)> measureBytesPerFrame)
    {
        _sequenceEnd = sequenceEnd; _sequencePath = sequencePath; _fallbackFolder = fallbackFolder; _measure = measureBytesPerFrame;
        HelpSystem.EnableContextHelp(this, "video-clips");
        Title = "Export Robot Frames"; Width = 600; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var fields = new StackPanel { Margin = new Thickness(14), Background = Brushes.WhiteSmoke };
        Content = new Border { Background = Brushes.WhiteSmoke, Child = fields };
        fields.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10), Foreground = Brushes.Black,
            Text = "Renders the robot frame by frame at exact timeline times into numbered PNG files with a transparent background " +
                   "(frame_000000.png, …), for placing over the video in Vegas or another editor. Settings are remembered for this sequence.",
        });
        Row(fields, "Size (pixels)", Pair(_width, "×", _height), "Default 1920 × 1080");
        Row(fields, "Frames per second", _fps, "Default 30; match the video's frame rate");
        Row(fields, "Start (seconds)", _start, "First frame's timeline time");
        var endRow = new StackPanel { Orientation = Orientation.Horizontal }; endRow.Children.Add(_end); endRow.Children.Add(_toEnd);
        _toEnd.Content = $"End of the sequence ({sequenceEnd:0.###} s)";
        Row(fields, "End (seconds)", endRow, "Last frame's timeline time; with \"End of the sequence\" a regenerated export follows the sequence as it grows");
        var folderRow = new DockPanel();
        var browse = new Button { Content = "Browse…", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(6, 0, 0, 0) };
        DockPanel.SetDock(browse, Dock.Right); folderRow.Children.Add(browse); folderRow.Children.Add(_folder);
        Row(fields, "Folder", folderRow, "Existing frame_*.png files there are replaced");
        _robotOnly.Content = "Robot only (leave a 3D stage backdrop out)"; Row(fields, "", _robotOnly, null);
        _matchVideo.Content = "Frame the video clip's picture area, so the robot lines up with the video"; Row(fields, "", _matchVideo, null);
        fields.Children.Add(_estimate);
        var remeasure = new Button { Content = "Measure size again", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        fields.Children.Add(remeasure);
        fields.Children.Add(_status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var export = new Button { Content = "Export", IsDefault = true, Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(16, 6, 16, 6) };
        buttons.Children.Add(export); buttons.Children.Add(cancel); fields.Children.Add(buttons);

        _width.Text = settings.Width.ToString(CultureInfo.CurrentCulture); _height.Text = settings.Height.ToString(CultureInfo.CurrentCulture);
        _fps.Text = settings.FramesPerSecond.ToString(CultureInfo.CurrentCulture); _start.Text = settings.StartSeconds.ToString("0.###", CultureInfo.CurrentCulture);
        _toEnd.IsChecked = settings.EndSeconds == null;
        _end.Text = settings.EndFor(sequenceEnd).ToString("0.###", CultureInfo.CurrentCulture);
        _folder.Text = ConfigPathService.Display(settings.FolderFor(sequencePath, fallbackFolder));
        _robotOnly.IsChecked = settings.RobotOnly; _matchVideo.IsChecked = settings.MatchVideo;

        _toEnd.Checked += (_, _) => { _end.Text = sequenceEnd.ToString("0.###", CultureInfo.CurrentCulture); _end.IsEnabled = false; UpdateEstimate(); };
        _toEnd.Unchecked += (_, _) => { _end.IsEnabled = true; UpdateEstimate(); };
        _end.IsEnabled = _toEnd.IsChecked != true;
        foreach (var box in new[] { _width, _height, _fps, _start, _end }) box.TextChanged += (_, _) => UpdateEstimate();
        _robotOnly.Click += (_, _) => UpdateEstimate(); _matchVideo.Click += (_, _) => UpdateEstimate();
        browse.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Folder for the robot frames" };
            string current = ConfigPathService.Resolve(ConfigPathService.CurrentRoot, _folder.Text);
            if (Directory.Exists(current)) dialog.InitialDirectory = current;
            if (dialog.ShowDialog(this) == true) _folder.Text = ConfigPathService.Display(dialog.FolderName);
        };
        remeasure.Click += (_, _) => { _measuredFor = null; UpdateEstimate(measure: true); };
        export.Click += (_, _) =>
        {
            try { Result = Read(); DialogResult = true; }
            catch (Exception ex) { _status.Text = ex.Message; }
        };
        ContentRendered += (_, _) => UpdateEstimate(measure: true);
    }

    private static TextBox Box() => new() { Width = 90, HorizontalAlignment = HorizontalAlignment.Left };

    private static StackPanel Pair(UIElement a, string between, UIElement b)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(a); panel.Children.Add(new TextBlock { Text = " " + between + " ", VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Black }); panel.Children.Add(b);
        return panel;
    }

    private static void Row(Panel parent, string label, UIElement input, string help)
    {
        var block = new StackPanel { Margin = new Thickness(0, 3, 0, 5) }; parent.Children.Add(block);
        var row = new DockPanel(); block.Children.Add(row);
        var title = new TextBlock { Text = label, Width = 150, Foreground = Brushes.Black, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(title, Dock.Left); row.Children.Add(title); row.Children.Add(input);
        if (!string.IsNullOrWhiteSpace(help))
            block.Children.Add(new TextBlock { Text = help, Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(150, 2, 0, 0), FontSize = 11.5 });
    }

    private static double Number(TextBox box, string label)
    {
        if (double.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double v) ||
            double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return v;
        throw new InvalidOperationException(label + " must be a number.");
    }

    private RobotFrameExportSettings Read()
    {
        var settings = new RobotFrameExportSettings
        {
            Width = (int)Math.Round(Number(_width, "Width")), Height = (int)Math.Round(Number(_height, "Height")),
            FramesPerSecond = Number(_fps, "Frames per second"), StartSeconds = Number(_start, "Start"),
            EndSeconds = _toEnd.IsChecked == true ? null : Number(_end, "End"),
            RobotOnly = _robotOnly.IsChecked == true, MatchVideo = _matchVideo.IsChecked == true,
        };
        if (string.IsNullOrWhiteSpace(_folder.Text)) throw new InvalidOperationException("Choose a folder for the frames.");
        string folder = ConfigPathService.Resolve(ConfigPathService.CurrentRoot, _folder.Text.Trim());
        // Keep the default (empty) folder setting while the box still shows the default, so it follows a renamed sequence.
        settings.Folder = string.Equals(folder, Path.GetFullPath(new RobotFrameExportSettings().FolderFor(_sequencePath, _fallbackFolder)), StringComparison.OrdinalIgnoreCase) ? "" : folder;
        settings.Validate(_sequenceEnd);
        return settings;
    }

    private void UpdateEstimate(bool measure = false)
    {
        RobotFrameExportSettings settings;
        try { settings = Read(); _status.Text = ""; }
        catch (Exception ex) { _estimate.Text = "Size estimate: —"; _status.Text = ex.Message; return; }
        string shape = $"{settings.Width}x{settings.Height}|{settings.RobotOnly}|{settings.MatchVideo}";
        if (measure && _measuredFor != shape)
        {
            try { Cursor = System.Windows.Input.Cursors.Wait; (_bytesPerFrame, _secondsPerFrame) = _measure(settings); _measuredFor = shape; }
            catch (Exception ex) { _bytesPerFrame = null; _status.Text = "Could not measure: " + ex.Message; }
            finally { Cursor = null; }
        }
        int frames = settings.FrameCount(_sequenceEnd);
        string free = "";
        try
        {
            string root = Path.GetPathRoot(ConfigPathService.Resolve(ConfigPathService.CurrentRoot, _folder.Text.Trim()));
            if (!string.IsNullOrEmpty(root)) free = $" · {Size(new DriveInfo(root).AvailableFreeSpace)} free on {root.TrimEnd('\\')}";
        }
        catch (Exception) { /* free space is informational */ }
        double seconds = settings.EndFor(_sequenceEnd) - settings.StartSeconds;
        if (_bytesPerFrame is long perFrame && _measuredFor == shape)
            _estimate.Text = $"{frames:N0} frames ({seconds:0.###} s) · about {Size(perFrame * (double)frames)} (≈ {Size(perFrame)} per frame, measured){free}" +
                $"\nExport time: about {Duration(_secondsPerFrame * frames)} (≈ {_secondsPerFrame * 1000:0} ms per frame)";
        else
            _estimate.Text = $"{frames:N0} frames ({seconds:0.###} s) · size not measured for these settings; press Measure size again{free}";
    }

    private static string Duration(double seconds) =>
        seconds >= 3600 ? $"{seconds / 3600:0.0} hours" : seconds >= 90 ? $"{seconds / 60:0} minutes" : $"{seconds:0} seconds";

    internal static string Size(double bytes) =>
        bytes >= 1e9 ? $"{bytes / 1e9:0.0} GB" : bytes >= 1e6 ? $"{bytes / 1e6:0} MB" : $"{bytes / 1e3:0} KB";
}
