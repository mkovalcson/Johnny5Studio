using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows.Controls;
using ServoAnimator;

internal static partial class Program
{
    private static void FaceFrameRateChecks()
    {
        Check(new FaceTrackingSettings().FrameRate == 30, "Existing face configurations default to 30 fps");
        var invalid = new FaceTrackingSettings { FrameRate = 45 };
        Check(Throws(() => invalid.Validate()), "Only 30 and 60 fps can be saved");
        var client = new FaceTrackingClient(() => true);
        Check(Throws(() => client.Start(0, 45)), "Invalid camera rate is rejected before opening any device");
        var packet = JsonSerializer.Deserialize<FacePacket>("{\"timestampMs\":100,\"detected\":false,\"processedFps\":57.8,\"captureWidth\":640,\"captureHeight\":480,\"reportedCaptureFps\":30,\"cameraWaitMs\":28.3,\"faceProcessingMs\":5.2}",
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        client.AcceptPacket(packet);
        Check(!client.Fresh && client.ProcessingFps == 57.8, "Processing FPS measures completed inference even without a detected face");
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(FaceTrackingClient).GetProperty(nameof(FaceTrackingClient.RequestedFrameRate)).SetValue(client, 60);
        var window = new FaceTrackingWindow(client, new FaceTrackingSettings { FrameRate = 60 });
        object Get(string name) => typeof(FaceTrackingWindow).GetField(name, flags).GetValue(window);
        void Refresh() => typeof(FaceTrackingWindow).GetMethod("Refresh", flags).Invoke(window, null);
        var selector = (ComboBox)Get("_frameRate"); var readout = (TextBlock)Get("_processingRate");
        var mirror = (CheckBox)Get("_mirrorVideo");
        Check(mirror.IsChecked == false, "Existing face configurations leave webcam mirroring off");
        mirror.IsChecked = true;
        Check(window.PreviewSettings.MirrorVideo && !client.Running, "Mirror selection updates the preview settings without starting a stopped camera");
        var startInfo = (System.Diagnostics.ProcessStartInfo)typeof(FaceTrackingClient).GetMethod("CreateStartInfo", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { 0, 60, true });
        Check(startInfo.ArgumentList.Contains("--mirror"), "The camera helper is instructed to mirror before tracking");
        var diagnostics = (TextBlock)Get("_captureDiagnostics");
        Check(selector.Items.Count == 2 && selector.SelectedIndex == 1, "Face tracking offers the saved 30/60 fps choice");
        Check(readout.Text.Contains("57.8") && readout.Text.Contains("60 fps"), "The screen shows actual average FPS separately from its requested target");
        Check(diagnostics.Text.Contains("640 × 480") && diagnostics.Text.Contains("driver reports 30 fps"), "Received resolution and driver-reported rate are separate from the 60 fps request");
        Check(diagnostics.Text.Contains("28.3 ms") && diagnostics.Text.Contains("5.2 ms"), "Camera wait and face-processing averages have separate readouts");
        packet.FormatRequestAccepted = false; packet.PixelFormat = "YUY2"; Refresh();
        Check(diagnostics.Text.Contains("MJPG request not accepted") && diagnostics.Text.Contains("YUY2"), "Tracking clearly identifies a negotiated format that differs from the MJPG request");
        packet.FormatRequestAccepted = null; packet.PixelFormat = null; Refresh();
        selector.SelectedIndex = 0;
        Check(window.PreviewSettings.FrameRate == 30 && !client.Running, "Selecting a rate updates the draft without opening a stopped camera");
        selector.SelectedIndex = 1;
        FaceTrackingSettings applied = null; window.ApplyRequested = settings => applied = settings;
        int saves = 0; window.SaveRequested = _ => saves++;
        typeof(FaceTrackingWindow).GetMethod("Apply", flags).Invoke(window, null);
        Check(applied.FrameRate == 60 && applied.MirrorVideo, "Save/apply includes the selected tracking rate and mirror setting");
        Check(saves == 0 && ((TextBlock)Get("_configurationState")).Text.Contains("not saved"), "Apply changes session settings without invoking Save");
        Check(!((Expander)Get("_performanceDetails")).IsExpanded, "Detailed performance diagnostics start collapsed");
        var grid = (DataGrid)Get("_grid");
        Check(((CheckBox)Get("_advancedMappings")).IsChecked == true && grid.Columns.All(c => c.Visibility == System.Windows.Visibility.Visible), "Advanced mappings are checked and visible when the window opens");
        ((CheckBox)Get("_advancedMappings")).IsChecked = false;
        Check(grid.Columns.Count(c => c.Visibility == System.Windows.Visibility.Visible) == 6, "Everyday mappings show only enable, movement, robot control, Speed, Reverse and remove");
        ((CheckBox)Get("_advancedMappings")).IsChecked = true;
        Check(grid.Columns.All(c => c.Visibility == System.Windows.Visibility.Visible), "Advanced mapping settings reveal all existing calibration controls");
        ((CheckBox)Get("_advancedMappings")).IsChecked = false;
        string folder = Path.Combine(Path.GetTempPath(), "FaceFps-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            applied.Save(folder);
            string originalFile = File.ReadAllText(Path.Combine(folder, "FaceTracking.json"));
            selector.SelectedIndex = 0;
            typeof(FaceTrackingWindow).GetMethod("Apply", flags).Invoke(window, null);
            Check(File.ReadAllText(Path.Combine(folder, "FaceTracking.json")) == originalFile && applied.FrameRate == 30,
                "Applying new session settings leaves an existing saved configuration unchanged");
            selector.SelectedIndex = 1;
            window.SaveRequested = settings => { settings.Save(folder); saves++; };
            typeof(FaceTrackingWindow).GetMethod("SaveConfiguration", flags).Invoke(window, null);
            Check(saves == 1 && ((TextBlock)Get("_configurationState")).Text == "Saved configuration", "Save configuration applies, persists and clears the unsaved indicator");
            Check(FaceTrackingSettings.Load(folder).FrameRate == 60, "60 fps survives save and reload");
            var mirroredSettings = FaceTrackingSettings.Load(folder);
            Check(mirroredSettings.MirrorVideo, "Webcam mirror selection survives save and reload");
            var mirroredWindow = new FaceTrackingWindow(new FaceTrackingClient(), mirroredSettings);
            Check(((CheckBox)typeof(FaceTrackingWindow).GetField("_mirrorVideo", flags).GetValue(mirroredWindow)).IsChecked == true,
                "Reopening Face Tracking restores the saved mirror checkbox");
            mirroredWindow.Close();
            mirror.IsChecked = false;
            window.SaveRequested = _ => throw new IOException("Test save failure");
            Check(Throws(() => typeof(FaceTrackingWindow).GetMethod("SaveConfiguration", flags).Invoke(window, null)), "A save failure is surfaced");
            Check(!applied.MirrorVideo && ((TextBlock)Get("_configurationState")).Text.Contains("not saved") && FaceTrackingSettings.Load(folder).MirrorVideo,
                "Failed saves keep the applied session configuration and preserve the last saved file");
            mirror.IsChecked = true; typeof(FaceTrackingWindow).GetMethod("Apply", flags).Invoke(window, null);
            File.WriteAllText(Path.Combine(folder, "FaceTracking.json"), "{}");
            Check(FaceTrackingSettings.Load(folder).FrameRate == 30, "Legacy settings without a rate retain 30 fps");
            Check(!FaceTrackingSettings.Load(folder).MirrorVideo, "Legacy settings without a mirror choice retain the unmodified image");
        }
        finally { File.Delete(Path.Combine(folder, "FaceTracking.json")); Directory.Delete(folder); }
        var baseline = new FacePacket { TimestampMs = 150, Detected = true, ProcessedFps = 30,
            CaptureWidth = 640, CaptureHeight = 480, ReportedCaptureFps = 60, CameraWaitMs = 27.6, FaceProcessingMs = 5.2,
            PixelFormat = "YUY2", Backend = "DSHOW" };
        var report = new FaceCaptureReport { Phase = "complete", RequestedFps = 60, CaptureFps = 59.9, CameraWaitMs = 16.7,
            Frames = 300, Seconds = 5.01, CaptureWidth = 640, CaptureHeight = 480, ReportedCaptureFps = 60,
            PixelFormat = "YUY2", Backend = "DSHOW" };
        Check(report.Describe(baseline, true).Contains("combined tracking pipeline"), "Capture-only near 60 and tracking near 30 identifies a combined-pipeline limitation");
        report.CaptureFps = 30;
        Check(report.Describe(baseline, true).Contains("Capture alone is below target"), "Capture-only at 30 is distinguished from a 60 fps driver report");
        report.CaptureFps = 59.9;
        Check(report.Describe(baseline, false).Contains("No matching"), "Different cameras or targets cannot produce an asserted comparison");
        baseline.PixelFormat = "MJPG";
        Check(report.Describe(baseline, true).Contains("No matching"), "Different negotiated pixel formats cannot be compared as the same capture mode");
        baseline.PixelFormat = "YUY2";
        report.FormatRequestAccepted = false;
        Check(report.Describe(baseline, true).Contains("MJPG request not accepted"), "Capture-only results also identify an unaccepted format request");
        report.FormatRequestAccepted = null;
        var cameraButtons = (WrapPanel)Get("_cameraButtons");
        var testReadout = (TextBlock)Get("_captureTestReadout"); var testButton = (Button)Get("_captureTestButton");
        void RunTest() => ((Task)typeof(FaceTrackingWindow).GetMethod("RunCaptureTestAsync", flags).Invoke(window, new object[] { 0, cameraButtons })).GetAwaiter().GetResult();
        bool usedSelectedRate = false;
        window.CaptureTestRunner = (camera, rate, progress, token) => { usedSelectedRate = camera == 0 && rate == 60; progress("Measuring test"); return Task.FromResult(report); };
        client.AcceptPacket(baseline); RunTest();
        Check(usedSelectedRate && testReadout.Text.Contains("59.9 fps") && testReadout.Text.Contains("30.0 fps"), "The on-screen capture-only result retains a comparison with the recent tracking baseline");
        Check(!client.Running && selector.IsEnabled && testButton.Content.ToString().Contains("7 seconds"), "A completed diagnostic leaves a previously stopped camera stopped and restores controls");
        window.CaptureTestRunner = (_, _, _, _) => Task.FromCanceled<FaceCaptureReport>(new CancellationToken(true));
        RunTest();
        Check(testReadout.Text.Contains("cancelled") && selector.IsEnabled, "Cancellation clears the diagnostic's busy state");
        window.CaptureTestRunner = (_, _, _, _) => Task.FromResult(report);
        client.AcceptPacket(baseline); RunTest(); client.AcceptPacket(baseline); Refresh();
        string output = PreviewDirectory("face-previews"); Directory.CreateDirectory(output);
        RenderControl((System.Windows.FrameworkElement)window.Content, Path.Combine(output, "FaceTrackingFrameRate.png"), 1160, 790);
        ((Expander)Get("_performanceDetails")).IsExpanded = true;
        RenderControl((System.Windows.FrameworkElement)window.Content, Path.Combine(output, "FaceTrackingPerformance.png"), 1160, 790);
        ((Expander)Get("_performanceDetails")).IsExpanded = false;
        RenderControl((System.Windows.FrameworkElement)window.Content, Path.Combine(output, "FaceTrackingCompact.png"), 960, 640);
        client.AcceptPacket(new FacePacket { TimestampMs = 200, Detected = true }); Refresh();
        Check(readout.Text.Contains("measuring"), "A new stream shows warm-up rather than a fabricated FPS");
        Check(diagnostics.Text.Contains("unavailable") && !diagnostics.Text.Contains("28.3"), "Missing driver metadata and timing warm-up never reuse old values");
        client.AcceptPacket(new FacePacket { TimestampMs = 300, ProcessedFps = 60 });
        typeof(FaceTrackingClient).GetField("_received", flags).SetValue(client, Environment.TickCount64 - 2000); Refresh();
        Check(client.ProcessingFps == null && readout.Text.Contains("no recent"), "Stalled tracking cannot display an old FPS as current");
        Check(diagnostics.Text == "Capture: —\nCamera wait: — · Face processing: —", "Stale capture and timing diagnostics clear with the FPS readout");
        client.Dispose(); Refresh(); Check(client.ProcessingFps == null, "Stopping clears the processing rate");
        window.Close();
    }
}
