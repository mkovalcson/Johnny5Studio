using System.IO;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using ServoAnimator;

internal static partial class Program
{
    private static void VideoClipChecks()
    {
        // Command settings
        Check(Throws(() => VideoClipCommand.Read("")), "A Video Clip needs a file and length");
        var clip = VideoClipCommand.Read("{\"path\":\"VideoMedia\\\\take.mp4\",\"sourceSeconds\":10}");
        Check(clip.Fit == TvFit.Contain && clip.Volume == 100 && !clip.Muted && clip.SkipSeconds == 0 && clip.EndSeconds == null && clip.PlayedSeconds == 10,
              "Video Clip defaults: contain, 100% volume, whole file");
        var trimmed = VideoClipCommand.Read("{\"path\":\"a.mov\",\"sourceSeconds\":10,\"skipSeconds\":2,\"endSeconds\":7.5,\"fit\":\"cover\",\"volume\":40,\"muted\":true}");
        Check(trimmed.PlayedSeconds == 5.5 && trimmed.Fit == TvFit.Cover && trimmed.Muted && trimmed.Volume == 40, "Skip to / End at set the played span");
        Check(VideoClipCommand.Read(trimmed.ToJson()).EndSeconds == 7.5 && VideoClipCommand.Read(clip.ToJson()).EndSeconds == null, "Settings round-trip");
        Check(Throws(() => VideoClipCommand.Read("{\"path\":\"a.png\",\"sourceSeconds\":10}")), "Images are not video clips");
        Check(Throws(() => VideoClipCommand.Read("{\"path\":\"a.mkv\",\"sourceSeconds\":10}")), "Only Media Foundation formats are accepted");
        Check(Throws(() => VideoClipCommand.Read("{\"path\":\"a.mp4\",\"sourceSeconds\":10,\"volume\":101}")), "Volume is limited to 0-100");
        Check(Throws(() => VideoClipCommand.Read("{\"path\":\"a.mp4\",\"sourceSeconds\":10,\"skipSeconds\":10}")), "Skip to must be inside the video");
        Check(Throws(() => VideoClipCommand.Read("{\"path\":\"a.mp4\",\"sourceSeconds\":10,\"skipSeconds\":4,\"endSeconds\":4}")) &&
              Throws(() => VideoClipCommand.Read("{\"path\":\"a.mp4\",\"sourceSeconds\":10,\"endSeconds\":11}")), "End at must follow Skip to and stay inside the video");
        Check(Throws(() => VideoClipCommand.Read("{\"path\":\"a.mp4\",\"sourceSeconds\":10,\"loop\":true}")), "Unknown settings are rejected");

        // A URDF-only text command: never hardware, never exports, no grid row, no mouth
        Check(ServoCommand.IsTextValued(ServoNames.VideoClip) && ServoCommand.IsUrdfOnly(ServoNames.VideoClip), "Video Clip is a URDF-only text command");
        Check(ServoCommand.DisplayName(ServoNames.VideoClip) == "Video Clip", "Video Clip display name");
        Check(TimelineLanes.For(ServoNames.VideoClip) == 2, "Video Clip markers sit in the Scene lane");
        var restored = new ServoCommand { Servo = ServoNames.VideoClip, Disable = true, Speed = ServoSpeed.Fast };
        restored.ClampToRange();
        Check(!restored.Disable && restored.Speed == ServoSpeed.NoChange, "Video Clip has no disable or speed");
        var doc = new AnimationDocument { Commands = { new ServoCommand { Servo = ServoNames.VideoClip, OffsetSeconds = 1, TextValue = clip.ToJson() } } };
        string file = Path.Combine(Path.GetTempPath(), "video-clip-check-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            doc.Save(file);
            var loaded = AnimationDocument.Load(file).Commands.Single();
            Check(loaded.Servo == ServoNames.VideoClip && loaded.TextValue == clip.ToJson() && loaded.OffsetSeconds == 1, "Video Clip commands save and load");
        }
        finally { File.Delete(file); }

        // Timeline evaluation and overlap rules
        ServoCommand C(double at, string text) => new() { Servo = ServoNames.VideoClip, OffsetSeconds = at, TextValue = text };
        string Ten(double skip = 0, double? end = null) => new VideoClipCommand { Path = "v.mp4", SourceSeconds = 10, SkipSeconds = skip, EndSeconds = end }.ToJson();
        var a = C(5, Ten()); var b = C(15, Ten(2, 6)); var broken = C(30, "{\"path\":\"v.mp4\"}");
        var all = new[] { a, b, broken };
        Check(VideoClipTimeline.At(all, 4.999) == null && VideoClipTimeline.At(all, 15) is { } s && s.Clip.Command == b && s.MediaSeconds == 2, "A clip starts at its command, showing Skip to");
        Check(VideoClipTimeline.At(all, 7.5).MediaSeconds == 2.5 && VideoClipTimeline.At(all, 14.999).Clip.Command == a, "Media time follows the cursor through the clip");
        Check(VideoClipTimeline.At(all, 19) == null && VideoClipTimeline.At(all, 31) == null, "Clips end at End at; invalid settings are ignored");
        Check(VideoClipTimeline.FindOverlap(all) == null, "Back-to-back clips do not overlap");
        var c = C(12, Ten());
        var overlap = VideoClipTimeline.FindOverlap(new[] { a, b, c });
        Check(overlap is { } o && o.First.Command == a && o.Second.Command == c, "Overlapping clips are found");
        Check(VideoClipTimeline.At(new[] { a, c }, 13).Clip.Command == c, "A hand-edited overlap plays the later clip");
        Check(VideoClipTimeline.Blocking(all, 0, 5) == null && VideoClipTimeline.Blocking(all, 0, 5.001)?.Command == a, "Inserting stops at the next clip");
        Check(VideoClipTimeline.Blocking(all, 14, 1)?.Command == a && VideoClipTimeline.Blocking(all, 19, 3) == null, "Inserting is refused inside a clip and allowed after one");
        Check(VideoClipTimeline.Blocking(all, 6, 9, except: a) == null && VideoClipTimeline.Blocking(all, 6, 9.5, except: a)?.Command == b, "A moved clip ignores itself but not its neighbours");

        // A real MP4 (sound only, which Media Foundation can write): length, sound envelope and import reuse
        string root = Path.Combine(Path.GetTempPath(), "video-clip-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string source = Path.Combine(Path.GetTempPath(), "video-clip-source-" + Guid.NewGuid().ToString("N") + ".mp4");
            var tone = new SignalGenerator(44100, 2) { Frequency = 440, Gain = .5 }.Take(TimeSpan.FromSeconds(2));
            MediaFoundationEncoder.EncodeToAac(tone.ToWaveProvider(), source);
            try
            {
                double seconds = VideoClipCommand.ProbeSeconds(source);
                Check(Math.Abs(seconds - 2) < .1, $"Media Foundation reads the length ({seconds:F3} s)");
                using (var reader = new AudioFileReader(source)) Check(reader.TotalTime.TotalSeconds > 1.8, "The clip's sound decodes for the waveform and its own output");
                string first = VideoClipCommand.Import(root, source);
                Check(first.StartsWith(VideoClipCommand.MediaFolder + Path.DirectorySeparatorChar) && File.Exists(Path.Combine(root, first)), "Import copies into Configuration\\VideoMedia");
                Check(VideoClipCommand.Import(root, source) == first && Directory.GetFiles(Path.Combine(root, VideoClipCommand.MediaFolder)).Length == 1,
                      "Importing the same video again reuses the copy");
                Check(VideoClipCommand.Import(root, Path.Combine(root, first)) == first, "A video already in Configuration is referenced where it is");
            }
            finally { File.Delete(source); }
            Check(Throws(() => VideoClipCommand.ProbeSeconds(Path.Combine(root, "missing.mp4"))), "A missing video has no length");
        }
        finally { Directory.Delete(root, true); }

        VideoFrameDecoding();
        var app = new App(); app.InitializeComponent(); DisableTestStartup(app);
        VideoClipTimelineView();
        VideoClipBackdropView();
        VideoClipSnapshot();
        VideoClipEditorApi();
        app.Shutdown();
    }

    /// <summary>The editor API's read-only video methods on a real (unshown) editor window.</summary>
    private static void VideoClipEditorApi()
    {
        string root = Path.Combine(Path.GetTempPath(), "video-clip-api-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, VideoClipCommand.MediaFolder));
        try
        {
            string video = Path.Combine(root, VideoClipCommand.MediaFolder, "take.mp4");
            WriteTestVideo(video, 320, 240, 30, 60);
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var window = new MainWindow();   // never shown: no hardware or restored documents
            typeof(MainWindow).GetField("_folders", flags).SetValue(window, new FolderSettings { ConfigFolder = root, ProjectFolder = root });
            object Call(EditorApiRequest input)
            {
                try { return typeof(MainWindow).GetMethod("HandleEditorApi", flags).Invoke(window, new object[] { input }); }
                catch (System.Reflection.TargetInvocationException ex) { throw ex.InnerException; }
            }
            System.Text.Json.JsonElement Json(object value) => System.Text.Json.JsonSerializer.SerializeToElement(value, EditorApiServer.Json);
            var doc = (AnimationDocument)typeof(MainWindow).GetField("_doc", flags).GetValue(window);
            doc.Commands.Add(new ServoCommand { Servo = ServoNames.VideoClip, OffsetSeconds = 2,
                TextValue = new VideoClipCommand { Path = Path.Combine(VideoClipCommand.MediaFolder, "take.mp4"), SourceSeconds = VideoClipCommand.ProbeSeconds(video) }.ToJson() });
            typeof(MainWindow).GetMethod("RefreshAfterEdit", flags).Invoke(window, null);

            var clips = Json(Call(new() { Method = "get_video_clips" })).GetProperty("videoClips");
            Check(clips.GetArrayLength() == 1 && clips[0].GetProperty("startSeconds").GetDouble() == 2 && Math.Abs(clips[0].GetProperty("endSeconds").GetDouble() - 4) < .05 &&
                  clips[0].GetProperty("fileFound").GetBoolean(), "get_video_clips lists the clip's span and file");
            var state = Json(Call(new() { Method = "get_state" })).GetProperty("state");
            Check(state.GetProperty("videoClips").GetArrayLength() == 1 && state.GetProperty("methods").EnumerateArray().Any(m => m.GetString() == "get_view_snapshot"),
                  "get_state includes the clips and advertises the video methods");
            var frames = Json(Call(new() { Method = "get_video_frames", Times = new() { 1, 2.5, 3.5, 2.5 }, MaxWidth = 160 })).GetProperty("frames");
            Check(frames.GetArrayLength() == 4 && frames[0].GetProperty("image").ValueKind == System.Text.Json.JsonValueKind.Null &&
                  frames[0].GetProperty("note").GetString().Contains("No video clip"), "Times outside a clip are noted, not pictured");
            Check(Math.Abs(frames[1].GetProperty("videoSeconds").GetDouble() - .5) < .04 && Math.Abs(frames[2].GetProperty("videoSeconds").GetDouble() - 1.5) < .04 &&
                  frames[3].GetProperty("videoSeconds").GetDouble() == frames[1].GetProperty("videoSeconds").GetDouble(), "Frames come from the clip's own video time");
            Check(frames[1].GetProperty("image").GetString().StartsWith("data:image/jpeg;base64,") && frames[1].GetProperty("width").GetInt32() == 160, "Frames are JPEG at the requested width");
            var range = Json(Call(new() { Method = "get_video_frames", StartSeconds = 2, EndSeconds = 3 })).GetProperty("frames");
            Check(range.GetArrayLength() == 6 && range[5].GetProperty("timelineSeconds").GetDouble() == 3, "A range without an interval gives 6 evenly spaced frames");
            Check(Throws(() => Call(new() { Method = "get_video_frames", Times = Enumerable.Range(0, 13).Select(i => (double)i).ToList() })) &&
                  Throws(() => Call(new() { Method = "get_video_frames", StartSeconds = 3, EndSeconds = 2 })), "Too many frames or a backwards range is refused");
            Check(Throws(() => Call(new() { Method = "get_view_snapshot" })), "A snapshot needs a URDF view on screen");
            window.Close();
        }
        finally { try { Directory.Delete(root, true); } catch (IOException) { /* a decoder may still hold the file briefly */ } }
    }

    /// <summary>get_view_snapshot's image: backdrop, then the decoded picture with the clip's fit, then the robot.</summary>
    private static void VideoClipSnapshot()
    {
        string path = Path.Combine(Path.GetTempPath(), "video-clip-snapshot-" + Guid.NewGuid().ToString("N") + ".mp4");
        try
        {
            WriteTestVideo(path, 320, 240, 30, 30);
            var view = new RobotHeadView();
            string body = Path.GetFullPath("ServoAnimator/FullBodyModel/johnny5_head.urdf");
            view.InstallModel(body, UrdfScene.Load(body), new UrdfFraming());
            view.SetTimelineBackground(new UrdfPresentationSettings { BackgroundMode = UrdfBackgroundMode.Color, BackgroundColor = "#00FF00" });
            var window = new System.Windows.Window { Content = view, Width = 640, Height = 240, Left = -20000, Top = -20000, ShowInTaskbar = false,
                ShowActivated = false, WindowStyle = System.Windows.WindowStyle.None, ResizeMode = System.Windows.ResizeMode.NoResize };
            window.Show(); view.UpdateLayout();
            var picture = VideoFrames.ToBitmap(VideoFrames.Read(path, new[] { .5 })[0], 0);
            var image = view.RenderSnapshot(640, picture, TvFit.Contain);
            byte[] Pixel(int x, int y) { var px = new byte[4]; image.CopyPixels(new System.Windows.Int32Rect(x, y, 1, 1), px, 4, 0); return px; }   // B G R A
            int w = image.PixelWidth, h = image.PixelHeight;
            var side = Pixel(5, h / 2); var videoTop = Pixel(w / 2 - (int)(h * 4 / 3.0 / 2) + 4, 4); var videoBottom = Pixel(w / 2 - (int)(h * 4 / 3.0 / 2) + 4, h - 4);
            Check(side[1] > 200 && side[0] < 60 && side[2] < 60, "Contain leaves the backdrop colour beside the video");
            Check(videoTop[2] > 180 && videoTop[0] < 80 && videoBottom[0] > 180 && videoBottom[2] < 80, "The video picture is upright under the robot");
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
            using (var stream = File.Create(Path.Combine(AppContext.BaseDirectory, "video-clip-snapshot-check.png"))) encoder.Save(stream);
            var plain = view.RenderSnapshot(320, null, TvFit.Contain);
            var corner = new byte[4]; plain.CopyPixels(new System.Windows.Int32Rect(2, 2, 1, 1), corner, 4, 0);
            Check(plain.PixelWidth == 320 && plain.PixelHeight == 120 && corner[1] > 200 && corner[2] < 60, "Without a clip the robot is shown over the backdrop, scaled to maxWidth");
            window.Close();
        }
        finally { File.Delete(path); }
    }

    /// <summary>The bar is drawn in the strip above the axis; drags stop at the neighbouring clips.</summary>
    private static void VideoClipTimelineView()
    {
        var peaks = Enumerable.Range(0, 10000).Select(i => (float)(.8 * Math.Sin(i / 40.0))).ToArray();
        VideoClipVisual Bar(double start, double seconds) => new()
        {
            Command = new ServoCommand { Servo = ServoNames.VideoClip, OffsetSeconds = start }, Name = "gameplay.mp4", Duration = seconds,
            PeakMin = peaks.Select(p => -Math.Abs(p)).ToArray(), PeakMax = peaks.Select(Math.Abs).ToArray(),
        };
        var first = Bar(5, 10); var second = Bar(20, 4); var third = Bar(26, 3);
        var wave = new WaveformView { Duration = 30, ContentDuration = 30, VideoClips = new() { first, second, third }, Width = 800, Height = 200 };
        // SKElement draws only inside a presentation source, so host it in an off-screen window.
        var host = new System.Windows.Window { Content = wave, SizeToContent = System.Windows.SizeToContent.WidthAndHeight, Left = -20000, Top = -20000,
            WindowStyle = System.Windows.WindowStyle.None, ShowInTaskbar = false, ShowActivated = false };
        host.Show(); wave.ZoomToFit(); wave.UpdateLayout();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(800, 200, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(wave);
        host.Close();
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using (var stream = File.Create(Path.Combine(AppContext.BaseDirectory, "video-clip-timeline-check.png"))) encoder.Save(stream);
        byte[] Pixel(double x, double y) { var px = new byte[4]; bitmap.CopyPixels(new System.Windows.Int32Rect((int)x, (int)y, 1, 1), px, 4, 0); return px; }   // B G R A
        double laneY = 200 - 26 - 12;
        var inside = Pixel(wave.XAtTime(13), laneY - 6); var outside = Pixel(wave.XAtTime(17), laneY);   // clear of the name label
        Check(inside[0] > inside[1] + 40 && inside[2] > inside[1], $"The video bar is drawn violet in the strip above the axis (BGRA {string.Join(",", inside)})");
        Check(Math.Abs(outside[0] - outside[1]) < 30, "No bar is drawn between clips");
        Check(wave.VideoClipAt(new System.Windows.Point(wave.XAtTime(10), laneY)) == first && wave.VideoClipAt(new System.Windows.Point(wave.XAtTime(10), 60)) == null &&
              wave.VideoClipAt(new System.Windows.Point(wave.XAtTime(17), laneY)) == null, "Bars are hit only in the video strip");
        var clamp = typeof(WaveformView).GetMethod("ClampVideoStart", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        double Drag(VideoClipVisual clip, double start) => (double)clamp.Invoke(wave, new object[] { clip, start });
        Check(Drag(first, -3) == 0 && Drag(first, 8) == 8 && Drag(first, 12) == 10, "A dragged bar stops at zero and at the next clip");
        Check(Drag(second, 10) == 15 && Drag(second, 25) == 22, "A bar between clips stays between them");
        Check(Drag(third, 40) == 40, "The last bar can move freely to the right");
    }

    /// <summary>Write a small H.264 MP4 with Media Foundation: every frame is red on top and blue below, and its green
    /// level is 4 × the frame number, so decoded pictures can be checked for orientation and timing.</summary>
    private static void WriteTestVideo(string path, int width, int height, int fps, int frameCount)
    {
        static Guid G(string s) => new(s);
        Guid video = G("73646976-0000-0010-8000-00AA00389B71"), h264 = G("34363248-0000-0010-8000-00AA00389B71"), rgb32 = G("00000016-0000-0010-8000-00AA00389B71");
        Guid frameSize = G("1652c33d-d6b2-4012-b834-72030849a37d"), frameRate = G("c459a2e8-3d2c-4e44-b132-fee5156c7bb0"),
             aspect = G("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6"), interlace = G("e2724bb8-e676-4806-b4b2-a8d6efb44ccd"),
             bitrate = G("20332624-fb0d-4d9e-bd0d-cbf6786c102e"), stride = G("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
        NAudio.MediaFoundation.MediaFoundationApi.Startup();
        NAudio.MediaFoundation.MediaFoundationInterop.MFCreateSinkWriterFromURL(path, null, null, out var writer);
        NAudio.MediaFoundation.IMFMediaType Type(Guid subtype)
        {
            var t = NAudio.MediaFoundation.MediaFoundationApi.CreateMediaType();
            t.SetGUID(NAudio.MediaFoundation.MediaFoundationAttributes.MF_MT_MAJOR_TYPE, video);
            t.SetGUID(NAudio.MediaFoundation.MediaFoundationAttributes.MF_MT_SUBTYPE, subtype);
            t.SetUINT64(frameSize, ((long)width << 32) | (uint)height); t.SetUINT64(frameRate, ((long)fps << 32) | 1);
            t.SetUINT64(aspect, (1L << 32) | 1); t.SetUINT32(interlace, 2);   // progressive
            return t;
        }
        var output = Type(h264); output.SetUINT32(bitrate, 800000);
        writer.AddStream(output, out int streamIndex);
        var input = Type(rgb32); input.SetUINT32(stride, width * 4);   // top-down rows
        writer.SetInputMediaType(streamIndex, input, null);
        writer.BeginWriting();
        long duration = 10_000_000L / fps;
        for (int f = 0; f < frameCount; f++)
        {
            var pixels = new byte[width * height * 4];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int i = (y * width + x) * 4; bool top = y < height / 2;
                    pixels[i] = (byte)(top ? 0 : 255); pixels[i + 1] = (byte)(f * 4); pixels[i + 2] = (byte)(top ? 255 : 0); pixels[i + 3] = 255;
                }
            var buffer = NAudio.MediaFoundation.MediaFoundationApi.CreateMemoryBuffer(pixels.Length);
            buffer.Lock(out IntPtr data, out _, out _);
            System.Runtime.InteropServices.Marshal.Copy(pixels, 0, data, pixels.Length);
            buffer.Unlock(); buffer.SetCurrentLength(pixels.Length);
            var sample = NAudio.MediaFoundation.MediaFoundationApi.CreateSample();
            sample.AddBuffer(buffer); sample.SetSampleTime(f * duration); sample.SetSampleDuration(duration);
            writer.WriteSample(streamIndex, sample);
            System.Runtime.InteropServices.Marshal.ReleaseComObject(sample); System.Runtime.InteropServices.Marshal.ReleaseComObject(buffer);
        }
        writer.DoFinalize();
        System.Runtime.InteropServices.Marshal.ReleaseComObject(writer);
    }

    /// <summary>Decoded pictures are upright, come from the requested time, and reach the API's snapshot.</summary>
    private static void VideoFrameDecoding()
    {
        string path = Path.Combine(Path.GetTempPath(), "video-clip-frames-" + Guid.NewGuid().ToString("N") + ".mp4");
        try
        {
            WriteTestVideo(path, 320, 240, 30, 60);
            Check(Math.Abs(VideoClipCommand.ProbeSeconds(path) - 2) < .05, "The test video is 2 s long");
            var frames = VideoFrames.Read(path, new[] { 0.0, .5, .51, 1.0, 1.9, 5.0, .2 });
            Check(frames.All(f => f.Width == 320 && f.Height == 240), "Frames decode at the video's size");
            int Green(VideoFrames.Frame f) => f.Pixels[(120 * 320 + 160) * 4 + 1];
            (int B, int G, int R) At(VideoFrames.Frame f, int y) { int i = (y * 320 + 160) * 4; return (f.Pixels[i], f.Pixels[i + 1], f.Pixels[i + 2]); }
            var top = At(frames[0], 30); var bottom = At(frames[0], 210);
            Check(top.R > 200 && top.B < 60 && bottom.B > 200 && bottom.R < 60, $"Frames are upright (top {top}, bottom {bottom})");
            int[] expected = { 0, 15, 15, 30, 57, 59, 6 };
            for (int i = 0; i < expected.Length; i++)
            {
                int frameNumber = (int)Math.Round(At(frames[i], 30).G / 4.0);
                Check(Math.Abs(frameNumber - expected[i]) <= 1, $"Frame {i} shows picture {frameNumber}, expected about {expected[i]}");
            }
            Check(frames[5].Seconds >= 1.9, "A time past the end gives the last picture");
            var bitmap = VideoFrames.ToBitmap(frames[3], 160);
            Check(bitmap.PixelWidth == 160 && bitmap.PixelHeight == 120 && VideoFrames.ToJpegDataUrl(bitmap).StartsWith("data:image/jpeg;base64,"), "Frames scale down and encode as JPEG");
            Check(Throws(() => VideoFrames.Read(Path.Combine(Path.GetTempPath(), "no-such-video.mp4"), new[] { 0.0 })), "A missing file is an error");
        }
        finally { File.Delete(path); }
    }

    /// <summary>Visual check: plays <paramref name="video"/> behind the robot in an on-screen window for two seconds
    /// and saves a screenshot of the composited result (video frames only reach the screen, not RenderTargetBitmap).</summary>
    private static void VideoClipPreview(string video, string png)
    {
        var app = new App(); app.InitializeComponent(); DisableTestStartup(app);
        var view = new RobotHeadView();
        string body = Path.GetFullPath("ServoAnimator/FullBodyModel/johnny5_head.urdf");
        view.InstallModel(body, UrdfScene.Load(body), new UrdfFraming());
        view.SetTimelineBackground(new UrdfPresentationSettings { BackgroundMode = UrdfBackgroundMode.Color, BackgroundColor = "#00FF00" });
        var window = new System.Windows.Window { Content = view, Width = 640, Height = 400, Left = 40, Top = 40, Topmost = true, WindowStyle = System.Windows.WindowStyle.None };
        window.Show();
        var command = new ServoCommand { Servo = ServoNames.VideoClip, TextValue = new VideoClipCommand { Path = Path.GetFullPath(video), SourceSeconds = VideoClipCommand.ProbeSeconds(video) }.ToJson() };
        var started = DateTime.Now;
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        timer.Tick += (_, _) =>
        {
            double t = (DateTime.Now - started).TotalSeconds;
            view.SetTimelineVideo(VideoClipTimeline.At(new[] { command }, Math.Min(t, .5)), t < 2, 1, Path.GetTempPath());
            if (t < 2.5) return;
            timer.Stop();
            var topLeft = view.PointToScreen(new System.Windows.Point());
            var size = view.PointToScreen(new System.Windows.Point(view.ActualWidth, view.ActualHeight)) - topLeft;
            using var bitmap = new System.Drawing.Bitmap((int)size.X, (int)size.Y);
            using (var g = System.Drawing.Graphics.FromImage(bitmap)) g.CopyFromScreen((int)topLeft.X, (int)topLeft.Y, 0, 0, bitmap.Size);
            bitmap.Save(png, System.Drawing.Imaging.ImageFormat.Png);
            window.Close(); app.Shutdown();
        };
        timer.Start();
        app.Run();
    }

    /// <summary>The video layer sits behind the 3D view; a 3D stage is hidden while a clip plays and returns after.</summary>
    private static void VideoClipBackdropView()
    {
        string video = Path.Combine(Path.GetTempPath(), "video-clip-view-" + Guid.NewGuid().ToString("N") + ".mp4");
        MediaFoundationEncoder.EncodeToAac(new SignalGenerator(44100, 2) { Gain = .2 }.Take(TimeSpan.FromSeconds(1)).ToWaveProvider(), video);
        try
        {
            var view = new RobotHeadView();
            string body = Path.GetFullPath("ServoAnimator/FullBodyModel/johnny5_head.urdf");
            view.InstallModel(body, UrdfScene.Load(body), new UrdfFraming());
            view.SetTimelineBackground(new UrdfPresentationSettings { BackgroundMode = UrdfBackgroundMode.Urdf3D,
                Stage = new UrdfStageSettings { ModelFile = Path.GetFullPath("ServoAnimator/Models/MatrixConstruct_backdrop.urdf") } });
            var window = new System.Windows.Window { Content = view, Width = 320, Height = 240, Left = -20000, Top = -20000, ShowInTaskbar = false, ShowActivated = false };
            window.Show();
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var root = (System.Windows.Media.Media3D.Model3DGroup)typeof(RobotHeadView).GetField("_sceneLightingRoot", flags).GetValue(view);
            var stage = (System.Windows.Media.Media3D.Model3DGroup)typeof(RobotHeadView).GetField("_stageModel", flags).GetValue(view);
            var media = (System.Windows.Controls.MediaElement)typeof(RobotHeadView).GetField("_backdropVideo", flags).GetValue(view);
            var viewport = (System.Windows.Controls.Viewport3D)typeof(RobotHeadView).GetField("_viewport", flags).GetValue(view);
            Check(view.Children.IndexOf(media) < view.Children.IndexOf(viewport) && media.IsMuted, "The muted video layer is behind the 3D view");
            Check(root.Children.Contains(stage), "The 3D stage shows before the clip");
            var command = new ServoCommand { Servo = ServoNames.VideoClip, OffsetSeconds = 2,
                TextValue = new VideoClipCommand { Path = video, SourceSeconds = 1, Fit = TvFit.Cover }.ToJson() };
            view.SetTimelineVideo(VideoClipTimeline.At(new[] { command }, 2.5), false, 0, Path.GetTempPath());
            Check(!root.Children.Contains(stage) && media.Visibility == System.Windows.Visibility.Visible && media.Stretch == System.Windows.Media.Stretch.UniformToFill,
                  "While the clip plays the stage is hidden and the video shows with its fit");
            view.SetTimelineBackground(new UrdfPresentationSettings { BackgroundMode = UrdfBackgroundMode.Urdf3D,
                Stage = new UrdfStageSettings { ModelFile = Path.GetFullPath("ServoAnimator/Models/MatrixConstruct_backdrop.urdf"), RobotX = .5 } });
            Check(!root.Children.Contains(stage), "A backdrop change during the clip keeps the stage hidden");
            view.SetTimelineVideo(VideoClipTimeline.At(new[] { command }, 3.5), false, 0, Path.GetTempPath());
            Check(root.Children.Contains(stage) && media.Visibility == System.Windows.Visibility.Collapsed, "After the clip the stage returns and the video hides");
            window.Close();
        }
        finally { File.Delete(video); }
    }
}
