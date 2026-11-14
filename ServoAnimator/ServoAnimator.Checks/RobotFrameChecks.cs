using System.IO;
using ServoAnimator;

internal static partial class Program
{
    private static void RobotFrameChecks()
    {
        // Settings, frame timing and defaults
        var s = new RobotFrameExportSettings();
        Check(s.Width == 1920 && s.Height == 1080 && s.FramesPerSecond == 30 && s.StartSeconds == 0 && s.EndSeconds == null && s.RobotOnly && s.MatchVideo,
              "Defaults: 1920 × 1080, 30 fps, 0 s to the end of the sequence, robot only, matched to the video");
        Check(s.FrameCount(10) == 301 && s.TimeOfFrame(0) == 0 && s.TimeOfFrame(1) == .033 && s.TimeOfFrame(300) == 10, "0–10 s at 30 fps is 301 frames on exact 1/30 s steps");
        s.StartSeconds = 2; s.EndSeconds = 3; s.FramesPerSecond = 24;
        Check(s.FrameCount(99) == 25 && s.EndFor(99) == 3 && s.TimeOfFrame(24) == 3, "A fixed end ignores the sequence end");
        Check(new RobotFrameExportSettings().FrameCount(321.557) == 9647, "A 321.557 s sequence is 9,647 frames at 30 fps");
        Check(Throws(() => new RobotFrameExportSettings { Width = 8 }.Validate(10)) && Throws(() => new RobotFrameExportSettings { FramesPerSecond = 0 }.Validate(10)) &&
              Throws(() => new RobotFrameExportSettings { StartSeconds = 5, EndSeconds = 4 }.Validate(10)) && Throws(() => new RobotFrameExportSettings { StartSeconds = 11 }.Validate(10)),
              "Size, frame rate and range are validated");
        Check(RobotFrameExportSettings.FileName(42) == "frame_000042.png", "Frames are numbered for image-sequence import");
        Check(new RobotFrameExportSettings().FolderFor(@"C:\Cfg\Projects\Demo_seq.json", @"C:\x") == @"C:\Cfg\Projects\Demo_seq frames" &&
              new RobotFrameExportSettings().FolderFor(null, @"C:\x") == @"C:\x\Unsaved sequence frames" &&
              new RobotFrameExportSettings { Folder = @"D:\Out" }.FolderFor(@"C:\a.json", @"C:\x") == @"D:\Out", "Default folder sits beside the sequence");

        // Remembered per sequence
        string root = Path.Combine(Path.GetTempPath(), "robot-frames-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = RobotFrameExportStore.Load(root);
            Check(store.For(root, Path.Combine(root, "Projects", "a.json")).Width == 1920, "An unknown sequence gets the defaults");
            store.Remember(root, Path.Combine(root, "Projects", "a.json"), new RobotFrameExportSettings { Width = 1280, Height = 720, FramesPerSecond = 60, StartSeconds = 4, EndSeconds = 9, RobotOnly = false });
            store.Remember(root, Path.Combine(root, "Projects", "b.json"), new RobotFrameExportSettings { StartSeconds = 1 });
            store.Save(root);
            var again = RobotFrameExportStore.Load(root);
            var a = again.For(root, Path.Combine(root, "PROJECTS", "A.json"));
            Check(a.Width == 1280 && a.Height == 720 && a.FramesPerSecond == 60 && a.StartSeconds == 4 && a.EndSeconds == 9 && !a.RobotOnly,
                  "Settings come back for the same sequence (path case ignored)");
            Check(again.For(root, Path.Combine(root, "Projects", "b.json")).EndSeconds == null && again.For(root, Path.Combine(root, "Projects", "b.json")).StartSeconds == 1,
                  "Each sequence keeps its own settings, including 'end of sequence'");
            Check(RobotFrameExportStore.Key(root, Path.Combine(root, "Projects", "a.json")) == Path.Combine("Projects", "a.json"), "Sequences are keyed relative to Configuration");
            File.WriteAllText(Path.Combine(root, RobotFrameExportStore.FileName), "{ not json");
            Check(RobotFrameExportStore.Load(root).Sequences.Count == 0, "An unreadable settings file falls back to defaults");
        }
        finally { Directory.Delete(root, true); }

        // Framing area maths
        bool Near(System.Windows.Rect r, double x, double y, double rw, double rh) => Math.Abs(r.X - x) < .01 && Math.Abs(r.Y - y) < .01 && Math.Abs(r.Width - rw) < .01 && Math.Abs(r.Height - rh) < .01;
        Check(Near(RobotHeadView.ExportArea(800, 600, 0, 0, TvFit.Contain, 16.0 / 9), 0, 75, 800, 450), "Without a video the frame is the centred 16:9 part of the view");
        Check(Near(RobotHeadView.ExportArea(800, 600, 640, 480, TvFit.Contain, 16.0 / 9), 0, 0, 800, 600) && Near(RobotHeadView.ExportArea(1000, 500, 1920, 1080, TvFit.Contain, 16.0 / 9), (1000 - 500 * 16 / 9.0) / 2, 0, 500 * 16 / 9.0, 500),
              "A Contain video frames exactly its picture area");
        Check(Near(RobotHeadView.ExportArea(800, 600, 1920, 1080, TvFit.Cover, 16.0 / 9), (800 - 600 * 16 / 9.0) / 2, 0, 600 * 16 / 9.0, 600), "A Cover video frames its whole, overflowing picture");
        Check(Near(RobotHeadView.ExportArea(800, 600, 1920, 1080, TvFit.Stretch, 16.0 / 9), 0, 0, 800, 600), "A Stretch video frames the whole view");

        // Cropping the framed area into the output
        var capture = new byte[8 * 6 * 4];
        for (int y = 0; y < 6; y++) for (int x = 0; x < 8; x++) capture[(y * 8 + x) * 4] = (byte)(y * 10 + x);
        var crop = MainWindow.CropFrame(capture, 8, 6, new System.Windows.Rect(1, 1, 4, 2), 1, 4, 2);
        Check(crop[0] == 11 && crop[3 * 4] == 14 && crop[(1 * 4 + 0) * 4] == 21, "The framed area is copied pixel for pixel");
        var padded = MainWindow.CropFrame(capture, 8, 6, new System.Windows.Rect(0, 0, 8, 6), 1, 10, 6);
        Check(padded[0] == 0 && padded[3] == 0 && padded[1 * 4] == 0 && padded[(0 * 10 + 2) * 4 + 1] == 0 && padded[(2 * 10 + 1) * 4] == 20, "A narrower area is centred with transparent sides");
        var cover = MainWindow.CropFrame(capture, 8, 6, new System.Windows.Rect(-2, 0, 12, 6), 1, 12, 6);
        Check(cover[(0 * 12 + 2) * 4] == 0 && cover[(1 * 12 + 3) * 4] == 11 && cover[(1 * 12 + 11) * 4 + 3] == 0, "An overflowing area keeps the view's part in place");

        // GPU capture of an off-screen view: transparent background, opaque robot, soft edges
        var app = new App(); app.InitializeComponent(); DisableTestStartup(app);
        var view = new RobotHeadView();
        string body = Path.GetFullPath("ServoAnimator/FullBodyModel/johnny5_head.urdf");
        view.InstallModel(body, UrdfScene.Load(body), new UrdfFraming());
        view.SetTimelineBackground(new UrdfPresentationSettings { BackgroundMode = UrdfBackgroundMode.Color, BackgroundColor = "#00FF00" });
        view.PrepareExportView();
        var host = new System.Windows.Window { Content = view, WindowStyle = System.Windows.WindowStyle.None, ResizeMode = System.Windows.ResizeMode.NoResize, ShowInTaskbar = false,
            ShowActivated = false, SizeToContent = System.Windows.SizeToContent.WidthAndHeight, WindowStartupLocation = System.Windows.WindowStartupLocation.Manual, Left = 0, Top = 0 };
        host.Show();
        double dpi = System.Windows.Media.VisualTreeHelper.GetDpi(host).DpiScaleX;
        view.Width = 1920 / dpi; view.Height = 1080 / dpi; host.UpdateLayout(); MainWindow.PumpRenders(3);
        var visible = new System.Windows.Int32Rect(0, 0, 1920, 1080);
        view.SetExportMarkerArea(new System.Windows.Rect(0, 0, 1920 / dpi, 1080 / dpi));
        var size = WindowCapture.ClientSize(host);
        Check(size == (1920, 1080), $"The off-screen renderer is exactly 1920 × 1080 ({size.Width} × {size.Height})");
        int marker = 0;
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var pixels = MainWindow.CaptureMatted(view, host, 1920, 1080, visible, ref marker);
        int opaque = 0, clear = 0, edge = 0;
        for (int i = 3; i < pixels.Length; i += 4) { if (pixels[i] == 255) opaque++; else if (pixels[i] == 0) clear++; else edge++; }
        Check(pixels[3] == 0 && pixels[(1920 * 1080 - 1) * 4 + 3] == 0 && clear > 1920 * 1080 / 4, $"The background (green in the view) is transparent in the frame (first {pixels[3]}, last {pixels[(1920 * 1080 - 1) * 4 + 3]}, clear {clear:N0}, opaque {opaque:N0}, edge {edge:N0})");
        Check(opaque > 1920 * 1080 / 20 && edge > 1000, $"The robot is opaque with antialiased edges ({opaque:N0} opaque, {edge:N0} edge pixels)");
        bool premultiplied = true;
        for (int i = 0; i < pixels.Length; i += 4) if (pixels[i] > pixels[i + 3] || pixels[i + 1] > pixels[i + 3] || pixels[i + 2] > pixels[i + 3]) { premultiplied = false; break; }
        Check(premultiplied, "Colours are premultiplied by alpha, as WPF and the PNG encoder expect");
        var repeat = MainWindow.CaptureMatted(view, host, 1920, 1080, visible, ref marker);
        int differ = 0; for (int i = 0; i < pixels.Length; i++) if (Math.Abs(pixels[i] - repeat[i]) > 2) differ++;
        Check(differ < pixels.Length / 1000, $"Capturing the same pose again gives the same frame ({differ:N0} bytes differ)");
        timer.Restart();
        for (int i = 0; i < 10; i++) MainWindow.CaptureMatted(view, host, 1920, 1080, visible, ref marker);
        double captureMs = timer.Elapsed.TotalMilliseconds / 10;
        string file = Path.Combine(AppContext.BaseDirectory, "robot-frame-check.png");
        timer.Restart();
        using (var png = File.Create(file)) MainWindow.EncodeRobotFrame(pixels, 1920, 1080, png);
        Console.WriteLine($"1080p: capture + matte {captureMs:0} ms/frame, PNG encode {timer.Elapsed.TotalMilliseconds:0} ms (on worker threads), {new FileInfo(file).Length / 1024:N0} KB");
        Check(captureMs < 1000, "A frame takes well under a second");
        Check(new FileInfo(file).Length < 3_000_000, "A 1080p robot frame PNG stays small");

        // Robot only: the 3D stage leaves the scene during the export and comes back after
        var root3d = (System.Windows.Media.Media3D.Model3DGroup)typeof(RobotHeadView).GetField("_sceneLightingRoot", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(view);
        view.SetTimelineBackground(new UrdfPresentationSettings { BackgroundMode = UrdfBackgroundMode.Urdf3D,
            Stage = new UrdfStageSettings { ModelFile = Path.GetFullPath("ServoAnimator/Models/MatrixConstruct_backdrop.urdf") } });
        var stage = (System.Windows.Media.Media3D.Model3DGroup)typeof(RobotHeadView).GetField("_stageModel", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(view);
        view.SetExportHidesStage(true);
        Check(!root3d.Children.Contains(stage), "Robot only leaves the 3D stage out");
        view.SetVideoForExport(true); view.SetVideoForExport(false);
        Check(!root3d.Children.Contains(stage), "A clip ending during a robot-only export keeps the stage out");
        view.EndFrameExport();
        Check(root3d.Children.Contains(stage), "The stage returns after the export");
        host.Close(); app.Shutdown();
    }
}