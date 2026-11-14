using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ServoAnimator;

public partial class MainWindow
{
    /// <summary>True while Robot Frames renders: the off-screen export view joins ForEachHeadView, real-time servo motion
    /// is paused (frames step it exactly), and video clips are not drawn, only their stage hiding applies.</summary>
    private bool _exportingFrames;
    /// <summary>Off-screen view the frames are captured from. WPF draws it on the GPU like any window; RenderTargetBitmap
    /// would rasterise the model's million-plus triangles in software (about 10 s a frame).</summary>
    private RobotHeadView _exportHeadView;
    private Window _exportHost;
    private (int Width, int Height) _exportHostSize;

    private sealed class FrameRender
    {
        public RobotHeadView View;
        public Window Host;
        public Rect Area;            // framed area in the on-screen view's device-independent pixels
        public double Scale;         // capture pixels per on-screen DIP
        public int Width, Height;    // output frame size
        public int CaptureWidth, CaptureHeight;
        public Int32Rect Visible;    // the framed area's part inside the view, in capture pixels; kept on screen
        public double CursorBefore;
        public bool RobotOnly;
        public int Marker;
    }

    /// <summary>File > Export Robot Frames: a transparent PNG sequence of the robot, rendered at exact timeline times.</summary>
    private async void ExportRobotFrames_Click(object sender, RoutedEventArgs e)
    {
        if (!ApplyOpenCommandEditor()) return;
        if (IsRunning) PausePlayback();
        if (LiveDrive) { MessageBox.Show(this, "Turn off Drive HW before exporting robot frames.", "Export Robot Frames"); return; }
        var view = ApiUrdfView;
        if (view == null || !view.IsVisible || view.ActualWidth < 2)
        { MessageBox.Show(this, "Show the URDF view (docked or undocked) first; frames are framed like it.", "Export Robot Frames"); return; }
        try
        {
            var store = RobotFrameExportStore.Load(ConfigRoot);
            double sequenceEnd = ServoCommand.TimeKey(ContentEnd);
            string fallback = _folders?.ProjectFolderOrDefault ?? ConfigRoot;
            var dialog = new RobotFrameExportWindow(store.For(ConfigRoot, _jsonPath), sequenceEnd, _jsonPath, fallback, MeasureRobotFrameBytes) { Owner = this };
            if (dialog.ShowDialog() != true) return;
            var settings = dialog.Result;
            store.Remember(ConfigRoot, _jsonPath, settings);
            try { store.Save(ConfigRoot); }
            catch (Exception ex) { ShowStatus("Robot Frames settings not remembered: " + ex.Message); }

            string folder = settings.FolderFor(_jsonPath, fallback);
            try
            {
                Directory.CreateDirectory(folder);
                var existing = Directory.GetFiles(folder, RobotFrameExportSettings.FilePrefix + "*.png");
                if (existing.Length > 0)
                {
                    if (MessageBox.Show(this, $"{ConfigPathService.Reference(ConfigRoot, folder)}\n\nalready holds {existing.Length:N0} frame_*.png files from an earlier export. Replace them?",
                            "Export Robot Frames", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                    foreach (string file in existing) File.Delete(file);
                }
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Export Robot Frames", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            await RunRobotFrameExport(settings, folder, sequenceEnd);
        }
        finally { CloseExportHost(); }
    }

    /// <summary>Prepare an export (or a size measurement): frame the area once for the whole export, size the off-screen
    /// view to the on-screen view's shape at the scale that makes that area the output size, and pause real-time motion.</summary>
    private FrameRender BeginRobotFrameRender(RobotFrameExportSettings settings, double sequenceEnd)
    {
        var source = ApiUrdfView ?? throw new InvalidOperationException("Open a URDF view first.");
        if (!source.IsVisible || source.ActualWidth < 2) throw new InvalidOperationException("Show the URDF view first.");
        int vw = 0, vh = 0; var fit = TvFit.Contain;
        var clip = settings.MatchVideo ? _videoSpans.FirstOrDefault(c => c.End > settings.StartSeconds && c.Start <= settings.EndFor(sequenceEnd)) : null;
        if (clip != null && File.Exists(clip.Settings.FullPath(ConfigRoot)))
        {
            var picture = VideoFrames.Read(clip.Settings.FullPath(ConfigRoot), new[] { clip.Settings.SkipSeconds })[0];
            vw = picture.Width; vh = picture.Height; fit = clip.Settings.Fit;
        }
        var area = source.ExportReferenceArea(vw, vh, fit, (double)settings.Width / settings.Height);
        double scale = Math.Min(settings.Width / area.Width, settings.Height / area.Height);
        int captureW = (int)Math.Ceiling(source.ActualWidth * scale - 1e-6), captureH = (int)Math.Ceiling(source.ActualHeight * scale - 1e-6);
        if (captureW > 8192 || captureH > 8192) throw new InvalidOperationException($"That needs a {captureW} × {captureH} render; choose a smaller size.");
        var inside = Rect.Intersect(area, new Rect(0, 0, source.ActualWidth, source.ActualHeight));
        var visible = new Int32Rect((int)Math.Round(inside.X * scale), (int)Math.Round(inside.Y * scale),
            Math.Min(captureW - (int)Math.Round(inside.X * scale), (int)Math.Round(inside.Width * scale)),
            Math.Min(captureH - (int)Math.Round(inside.Y * scale), (int)Math.Round(inside.Height * scale)));
        var view = EnsureExportHost(source, captureW, captureH, visible);
        view.WaitForAppearancePreparation();
        var render = new FrameRender
        {
            View = view, Host = _exportHost, Area = area, Scale = scale, Width = settings.Width, Height = settings.Height,
            CaptureWidth = captureW, CaptureHeight = captureH, Visible = visible, CursorBefore = _cursorTime, RobotOnly = settings.RobotOnly,
        };
        _exportingFrames = true;
        view.SetExportHidesStage(settings.RobotOnly);
        ForEachHeadView(v => v.CalibratedMotionPaused = true);
        RebuildSplineData();
        return render;
    }

    /// <summary>The export view, configured like the undocked URDF view and copied from the on-screen one. Its window sits
    /// behind the editor with the framed area exactly on the primary screen from its top-left corner: Windows keeps a
    /// covered window's picture current, but not the parts of a window that are off screen.</summary>
    private RobotHeadView EnsureExportHost(RobotHeadView source, int width, int height, Int32Rect visible)
    {
        if (_exportHost == null)
        {
            var view = new RobotHeadView();
            view.SetUrdfConfiguration(_urdfConfig);
            view.SetServoConfiguration(_servoConfig);
            ConfigureMotionView(view);
            view.SetUrdfDriveEnabled(source.UrdfDriveEnabled);
            view.SetCollisionWarningsEnabled(false);
            if (!string.Equals(view.LoadedModelFile, source.LoadedModelFile, StringComparison.OrdinalIgnoreCase) && File.Exists(source.LoadedModelFile))
                view.InstallModel(source.LoadedModelFile, UrdfScene.Load(source.LoadedModelFile), source.CaptureCurrentFraming());   // 3D objects belong to the UI thread
            view.PrepareExportView();
            _exportHost = new Window
            {
                Content = view, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, ShowActivated = false,
                SizeToContent = SizeToContent.WidthAndHeight, WindowStartupLocation = WindowStartupLocation.Manual, Left = 0, Top = 0,
                Title = "Robot Frames renderer",
            };
            _exportHost.Show();
            Activate();   // keep the editor in front; the renderer works behind it
            _exportHeadView = view;
            _exportHostSize = default;
        }
        double dpi = VisualTreeHelper.GetDpi(_exportHost).DpiScaleX;
        int screenW = (int)Math.Round(SystemParameters.PrimaryScreenWidth * dpi), screenH = (int)Math.Round(SystemParameters.PrimaryScreenHeight * dpi);
        if (visible.Width > screenW || visible.Height > screenH)
            throw new InvalidOperationException($"The frames' {visible.Width} × {visible.Height} area must fit on the main screen ({screenW} × {screenH}). Choose a smaller size.");
        if (_exportHostSize != (width, height))
        {
            _exportHeadView.Width = width / dpi; _exportHeadView.Height = height / dpi;
            _exportHost.UpdateLayout();
            PumpRenders(3);
            var actual = WindowCapture.ClientSize(_exportHost);
            if (actual.Width != width || actual.Height != height)
                throw new InvalidOperationException($"Windows limited the renderer to {actual.Width} × {actual.Height} instead of {width} × {height}. Choose a smaller size, or make the URDF view's shape closer to the output's.");
            _exportHostSize = (width, height);
        }
        _exportHost.Left = -visible.X / dpi; _exportHost.Top = -visible.Y / dpi;
        _exportHeadView.SetExportMarkerArea(new Rect(visible.X / dpi, visible.Y / dpi, visible.Width / dpi, visible.Height / dpi));
        _exportHeadView.CopyCameraFrom(source);
        return _exportHeadView;
    }

    private void CloseExportHost()
    {
        if (_exportHost == null) return;
        var view = _exportHeadView;
        _exportHeadView = null; _exportHostSize = default;
        _exportHost.Close(); _exportHost = null;
        _backdropTimeline.Remove(view); _timelineSceneModels.Remove(view); _urdfTimelineBaselines.Remove(view);
    }

    /// <summary>Let WPF render <paramref name="count"/> frames (keeps the UI and the Cancel button responsive too).</summary>
    internal static void PumpRenders(int count)
    {
        int seen = 0;
        var frame = new DispatcherFrame();
        void Tick(object sender, EventArgs e) { if (++seen >= count) frame.Continue = false; }
        var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timeout.Tick += (_, _) => frame.Continue = false;
        CompositionTarget.Rendering += Tick; timeout.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { CompositionTarget.Rendering -= Tick; timeout.Stop(); }
    }

    /// <summary>Set the pose at timeline time <paramref name="t"/>. A positive <paramref name="step"/> advances calibrated
    /// servo motion by exactly that long, as playback would between frames; zero snaps to the targets.</summary>
    private void PoseRobotFrameAt(FrameRender render, double t, double step)
    {
        _cursorTime = t;
        var view = render.View;
        if (step <= 0) view.SnapCalibratedMotion = true;
        try { UpdateServoState(t); }
        finally { view.SnapCalibratedMotion = false; }
        if (step > 0)
        {
            view.CalibratedMotionPaused = false;
            try { view.AdvanceCalibratedMotion(step); }
            finally { view.CalibratedMotionPaused = true; }
        }
        if (PlaybackOutputAllowed(false)) view.SetMouth(MouthAmplitudeAt(t));
    }

    /// <summary>One output frame of the current pose: captured over black and over white, matted to premultiplied BGRA,
    /// and the framed area copied into the output size, centred.</summary>
    private byte[] CaptureRobotFrame(FrameRender render)
    {
        var matte = CaptureMatted(render.View, render.Host, render.CaptureWidth, render.CaptureHeight, render.Visible, ref render.Marker);
        return CropFrame(matte, render.CaptureWidth, render.CaptureHeight, render.Area, render.Scale, render.Width, render.Height);
    }

    /// <summary>The export view's current picture as premultiplied BGRA with a real alpha channel; the markers at the
    /// corners of <paramref name="visible"/> (capture pixels) are cleared.</summary>
    internal static byte[] CaptureMatted(RobotHeadView view, Window host, int width, int height, Int32Rect visible, ref int marker)
    {
        int corner = (int)Math.Ceiling(RobotHeadView.ExportMarkerSize * VisualTreeHelper.GetDpi(host).DpiScaleX) + 1;
        var overBlack = CaptureOver(view, host, width, height, visible, Brushes.Black, ref marker);
        var overWhite = CaptureOver(view, host, width, height, visible, Brushes.White, ref marker);
        var matte = WindowCapture.Matte(overBlack, overWhite);
        void Clear(int x0, int y0)
        {
            int x = Math.Clamp(x0, 0, width), w = Math.Clamp(x0 + corner, 0, width) - x;
            for (int y = Math.Max(0, y0); y < Math.Min(height, y0 + corner) && w > 0; y++) Array.Clear(matte, (y * width + x) * 4, w * 4);
        }
        Clear(visible.X - 1, visible.Y - 1);
        Clear(visible.X + visible.Width - corner + 1, visible.Y + visible.Height - corner + 1);
        return matte;
    }

    /// <summary>Capture over a solid background, retrying until both markers show this capture's colour, which proves the
    /// window has presented the latest pose and background across the framed area.</summary>
    private static byte[] CaptureOver(RobotHeadView view, Window host, int width, int height, Int32Rect visible, Brush background, ref int marker)
    {
        // Consecutive markers differ widely, so the ±2 tolerance for colour rounding can't accept the previous one.
        marker = (marker + 1) & 0xFFFF;
        var expected = Color.FromRgb((byte)(marker * 53 % 256), (byte)(marker * 97 % 256), (byte)(marker % 2 == 0 ? 40 : 200));
        view.SetExportBackground(background);
        view.SetExportMarker(expected);
        bool Shows(byte[] p, int x, int y)
        {
            int i = (y * width + x) * 4;
            return Math.Abs(p[i] - expected.B) <= 2 && Math.Abs(p[i + 1] - expected.G) <= 2 && Math.Abs(p[i + 2] - expected.R) <= 2;
        }
        string Seen(byte[] p, int x, int y) { int i = (y * width + x) * 4; return $"#{p[i + 2]:X2}{p[i + 1]:X2}{p[i]:X2}"; }
        string last = "no capture";
        for (int attempt = 0; attempt < 90; attempt++)
        {
            PumpRenders(attempt == 0 ? 2 : 1);
            var pixels = WindowCapture.CaptureClient(host, out int w, out int h);
            if (pixels == null || w != width || h != height) { last = pixels == null ? "capture failed" : $"window {w} × {h}, expected {width} × {height}"; continue; }
            int x0 = visible.X + 1, y0 = visible.Y + 1, x1 = visible.X + visible.Width - 2, y1 = visible.Y + visible.Height - 2;
            if (Shows(pixels, x0, y0) && Shows(pixels, x1, y1)) return pixels;
            last = $"markers showed {Seen(pixels, x0, y0)} and {Seen(pixels, x1, y1)}, expected #{expected.R:X2}{expected.G:X2}{expected.B:X2}";
        }
        throw new TimeoutException($"The renderer did not present a new frame; the export was stopped ({last}).");
    }

    /// <summary>Copy <paramref name="area"/> (DIPs, scaled by <paramref name="scale"/> into capture pixels) centred into a
    /// <paramref name="width"/> × <paramref name="height"/> frame; anything outside the capture stays transparent.</summary>
    internal static byte[] CropFrame(byte[] capture, int captureW, int captureH, Rect area, double scale, int width, int height)
    {
        var output = new byte[width * height * 4];
        int ox = (int)Math.Round((width - area.Width * scale) / 2), oy = (int)Math.Round((height - area.Height * scale) / 2);
        int sx = (int)Math.Round(area.X * scale) - ox, sy = (int)Math.Round(area.Y * scale) - oy;   // capture x = output x + sx
        int x0 = Math.Max(0, -sx), x1 = Math.Min(width, captureW - sx);
        if (x1 <= x0) return output;
        for (int y = 0; y < height; y++)
        {
            int cy = y + sy;
            if (cy < 0 || cy >= captureH) continue;
            Buffer.BlockCopy(capture, (cy * captureW + x0 + sx) * 4, output, (y * width + x0) * 4, (x1 - x0) * 4);
        }
        return output;
    }

    private void EndRobotFrameRender(FrameRender render)
    {
        _exportingFrames = false;
        ForEachHeadView(v => { v.CalibratedMotionPaused = false; v.EndFrameExport(); });
        _cursorTime = render.CursorBefore;
        ForEachHeadView(v => v.SnapCalibratedMotion = true);
        try { UpdateServoState(_cursorTime); }
        finally { ForEachHeadView(v => v.SnapCalibratedMotion = false); }
        if (PlaybackOutputAllowed(false)) ForEachHeadView(v => v.SetMouth(0));
        Waveform.CursorTime = _cursorTime; Waveform.InvalidateCursor(); UpdateTimeText();
    }

    /// <summary>Mouth level from the timeline audio at <paramref name="t"/>, exactly as playback drives it (never video sound).</summary>
    private double MouthAmplitudeAt(double t)
    {
        var source = DesiredSourceAt(t);
        if (source == null) return 0;
        double at = t - source.Start + source.SourceStart;
        return source.IsPrimary ? Waveform.AmplitudeAt(at) : AmplitudeFrom(source.PeakMin, source.PeakMax, at);
    }

    /// <summary>Average PNG size of frames rendered at the start, middle and end of the range, for the dialog's estimate.
    /// The renderer stays open for the export that follows.</summary>
    private (long Bytes, double Seconds) MeasureRobotFrameBytes(RobotFrameExportSettings settings)
    {
        double sequenceEnd = ServoCommand.TimeKey(ContentEnd);
        var render = BeginRobotFrameRender(settings, sequenceEnd);
        try
        {
            double start = settings.StartSeconds, end = settings.EndFor(sequenceEnd);
            long total = 0; int count = 0;
            var clock = new Stopwatch();
            foreach (double t in new[] { start, (start + end) / 2, end }.Distinct())
            {
                clock.Start();
                PoseRobotFrameAt(render, t, 0);
                var pixels = CaptureRobotFrame(render);
                clock.Stop();   // PNG encoding runs on worker threads during an export, alongside the next capture
                using var memory = new MemoryStream();
                EncodeRobotFrame(pixels, settings.Width, settings.Height, memory);
                total += memory.Length; count++;
            }
            return (total / Math.Max(1, count), clock.Elapsed.TotalSeconds / Math.Max(1, count));
        }
        finally { EndRobotFrameRender(render); }
    }

    internal static void EncodeRobotFrame(byte[] pixels, int width, int height, Stream output)
    {
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0)));   // straight alpha in the file
        encoder.Save(output);
    }

    private async Task RunRobotFrameExport(RobotFrameExportSettings settings, string folder, double sequenceEnd)
    {
        int count = settings.FrameCount(sequenceEnd);
        bool cancel = false;
        var progressText = new TextBlock { Text = "Preparing…", Margin = new Thickness(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap };
        var bar = new ProgressBar { Height = 12, Maximum = count };
        var stop = new Button { Content = "Cancel", Padding = new Thickness(16, 4, 16, 4), Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(progressText); panel.Children.Add(bar); panel.Children.Add(stop);
        var progress = new Window { Owner = this, Title = "Exporting Robot Frames", Content = panel, Width = 460, SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false };
        bool finished = false;
        stop.Click += (_, _) => { cancel = true; stop.IsEnabled = false; progressText.Text = "Stopping…"; };
        progress.Closing += (_, e) => { if (!finished) { e.Cancel = true; cancel = true; } };
        progress.Show();
        IsEnabled = false;
        FrameRender render = null;
        int written = 0;
        var writers = new List<Task>();
        using var throttle = new SemaphoreSlim(Math.Clamp(Environment.ProcessorCount / 2, 2, 8));
        var clock = Stopwatch.StartNew();
        string error = null;
        try
        {
            render = BeginRobotFrameRender(settings, sequenceEnd);
            double step = 1 / settings.FramesPerSecond;
            for (int i = 0; i < count && !cancel; i++)
            {
                double t = settings.TimeOfFrame(i);
                PoseRobotFrameAt(render, t, i == 0 ? 0 : step);
                byte[] pixels = CaptureRobotFrame(render);
                await throttle.WaitAsync();
                string file = Path.Combine(folder, RobotFrameExportSettings.FileName(i));
                writers.Add(Task.Run(() =>
                {
                    try { using var stream = File.Create(file); EncodeRobotFrame(pixels, settings.Width, settings.Height, stream); Interlocked.Increment(ref written); }
                    finally { throttle.Release(); }
                }));
                if (writers.Count > 64)
                {
                    var failed = writers.FirstOrDefault(w => w.IsFaulted);
                    if (failed != null) await failed;   // rethrows the write error
                    writers.RemoveAll(w => w.IsCompletedSuccessfully);
                }
                if (i % 4 == 0 || i == count - 1)
                {
                    double rate = (i + 1) / Math.Max(.001, clock.Elapsed.TotalSeconds);
                    var left = TimeSpan.FromSeconds((count - i - 1) / Math.Max(.001, rate));
                    progressText.Text = $"Frame {i + 1:N0} of {count:N0} (timeline {t:0.000} s) · {rate:0.0} frames/s · about {left:hh\\:mm\\:ss} left\n{ConfigPathService.Reference(ConfigRoot, folder)}";
                    bar.Value = i + 1;
                    Waveform.CursorTime = t; Waveform.InvalidateCursor();
                }
            }
            await Task.WhenAll(writers);
            var info = new
            {
                sequence = ConfigPathService.Reference(ConfigRoot, _jsonPath), firstFrameTimelineSeconds = settings.StartSeconds, framesPerSecond = settings.FramesPerSecond,
                frames = written, completed = !cancel, settings.Width, settings.Height, robotOnly = settings.RobotOnly, matchVideo = settings.MatchVideo,
                fileNames = RobotFrameExportSettings.FilePrefix + "000000.png onward; frame N shows timeline time firstFrameTimelineSeconds + N / framesPerSecond",
                exported = DateTime.Now,
            };
            File.WriteAllText(Path.Combine(folder, "robot-frames.json"), JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { error = ex.Message; try { await Task.WhenAll(writers); } catch { /* reported above */ } }
        finally
        {
            if (render != null) EndRobotFrameRender(render);
            IsEnabled = true; finished = true; progress.Close();
        }
        if (error != null) { MessageBox.Show(this, $"The export stopped after {written:N0} frames:\n\n{error}", "Export Robot Frames", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        long bytes = Directory.GetFiles(folder, RobotFrameExportSettings.FilePrefix + "*.png").Sum(f => new FileInfo(f).Length);
        string summary = $"{(cancel ? "Stopped" : "Exported")} {written:N0} of {count:N0} frames ({RobotFrameExportWindow.Size(bytes)}) in {clock.Elapsed:hh\\:mm\\:ss}.\n\n{ConfigPathService.Reference(ConfigRoot, folder)}\n\n" +
            $"In Vegas, open {RobotFrameExportSettings.FileName(0)} as an image sequence at {settings.FramesPerSecond:0.###} fps and place it at {settings.StartSeconds:0.###} s on the video's timeline.\n\nOpen the folder?";
        ShowStatus($"Robot Frames: {written:N0} frames in {ConfigPathService.Reference(ConfigRoot, folder)}");
        if (MessageBox.Show(this, summary, "Export Robot Frames", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + folder + "\"") { UseShellExecute = true });
    }
}
