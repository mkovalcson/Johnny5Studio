using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Windows;
using NAudio.Wave;
using ServoAnimator;

internal static partial class Program
{
    private static void HardwareTimelineChecks()
    {
        ServoCommand C(ServoNames servo, double time, int value, RobotControls? child = null,
            ServoSpeed speed = ServoSpeed.NoChange, bool broken = false, bool disabled = false) =>
            new() { Servo = servo, OffsetSeconds = time, NumericValue = value, Control = child, Speed = speed, BreakSpline = broken, Disable = disabled };
        var config = ServoConfiguration.CreateDefault();
        var commands = new[] {
            C(ServoNames.NeckTurn, 0, 0, speed: ServoSpeed.Slow), C(ServoNames.NeckTurn, 2, 100, broken: true),
            C(ServoNames.NeckTurn, 4, -50, speed: ServoSpeed.Fast), C(ServoNames.NeckTurn, 6, 50),
            C(ServoNames.NeckNodUp, 0, 40), C(ServoNames.NeckNodUp, 4, 40),
            C(ServoNames.NeckTiltRight, 0, 20), C(ServoNames.NeckTiltRight, 4, 60),
            C(ServoNames.IrisClose, 0, 0), C(ServoNames.IrisClose, 4, 100),
            C(ServoNames.IrisClose, 1, 30, RobotControls.LeftIris, ServoSpeed.Crawl),
            C(ServoNames.BothEyePop, 0, 100), C(ServoNames.LeftEyePop, 1, 500), C(ServoNames.BothEyePop, 3, 700),
            C(ServoNames.NoseBasket, 0, 20), C(ServoNames.NoseBasket, 1, 0, disabled: true), C(ServoNames.NoseBasket, 3, 40),
            C(ServoNames.URDFSlide, 0, 0), C(ServoNames.URDFSlide, 4, 100) };
        var curves = commands.Where(c => !c.Control.HasValue && !c.Disable)
            .GroupBy(c => c.Servo).Where(g => g.Key is ServoNames.NeckTurn or ServoNames.NeckNodUp or ServoNames.NeckTiltRight or ServoNames.IrisClose or ServoNames.URDFSlide)
            .ToDictionary(g => g.Key, g => {
                var points = g.OrderBy(c => c.OffsetSeconds).ToArray(); var t = points.Select(c => c.OffsetSeconds).ToArray();
                var v = points.Select(c => (double)c.NumericValue).ToArray(); var breaks = points.Select(c => c.BreakSpline).ToArray();
                var range = ServoCommand.RangeFor(g.Key);
                return new SplineCurve { Servo = g.Key, T = t, V = v, BreakAfter = breaks, M = SplineUtil.Tangents(t, v, breaks), Min = range.Min, Max = range.Max };
            });
        var evaluator = new TimelineHardwarePose(commands, curves, config);
        ServoCommand At(double time, RobotControls control) => evaluator.Evaluate(time).Single(c => c.Control == control);
        Check(At(1, RobotControls.NeckTurn).NumericValue == 50, "Hardware gets an intermediate spline target before its endpoint");
        Check(At(3, RobotControls.NeckTurn).NumericValue == 100 && At(4, RobotControls.NeckTurn).NumericValue == -50,
            "Hardware holds across Break Spline and changes at the exact next command");
        Check(At(5, RobotControls.NeckTurn).NumericValue == 0 && At(1, RobotControls.NeckTurn).Speed == ServoSpeed.Slow &&
            At(5, RobotControls.NeckTurn).Speed == ServoSpeed.Fast, "Spline resumes and historical speed profiles are restored on a seek");
        Check(At(2, RobotControls.LeftIris).NumericValue == 30 && At(2, RobotControls.RightIris).NumericValue == 50 &&
            At(2, RobotControls.LeftIris).Speed == ServoSpeed.Crawl, "A child overrides only its own splined actuator and profile");
        Check(At(4, RobotControls.LeftIris).NumericValue == 100, "The next parent command retakes its child at the command time");
        Check(At(2, RobotControls.LeftEyePop).NumericValue == 500 && At(2, RobotControls.RightEyePop).NumericValue == 100 &&
            At(3, RobotControls.LeftEyePop).NumericValue == 700, "Eye-pop aliases select the newest target, with both-eye commands taking ties");
        Check(At(2, RobotControls.NoseBasket).Disable && !At(3, RobotControls.NoseBasket).Disable, "Cursor initialization respects disabled channels and their later reenable");
        var pair = NeckMix.Pistons(config, 40, 40);
        Check(At(2, RobotControls.NeckTiltLeft).NumericValue == pair.Left && At(2, RobotControls.NeckTiltRight).NumericValue == pair.Right,
            "Hardware spline targets use the same complementary neck mixer as face tracking and URDF");
        Check(evaluator.Evaluate(2).All(c => !ServoCommand.IsUrdfOnly(c.Servo)) && evaluator.Evaluate(2).Select(c => c.Control).Distinct().Count() == evaluator.Evaluate(2).Length,
            "One target is emitted per physical channel and presentation commands never reach hardware");
        var carry = new TimelineHardwarePose(Array.Empty<ServoCommand>(), new Dictionary<ServoNames, SplineCurve>(), config,
            new Dictionary<ServoNames, double> { [ServoNames.LeftEyePop] = 321, [ServoNames.RightEyePop] = 456 },
            new Dictionary<(ServoNames, RobotControls), double> { [(ServoNames.IrisClose, RobotControls.LeftIris)] = 22 });
        Check(carry.Evaluate(0).Single(c => c.Control == RobotControls.LeftEyePop).NumericValue == 321 &&
            carry.Evaluate(0).Single(c => c.Control == RobotControls.RightEyePop).NumericValue == 456 &&
            carry.Evaluate(0).Single(c => c.Control == RobotControls.LeftIris).NumericValue == 22, "Untouched movie channels retain their individual carried poses");

        var changes = new TimelineHardwareChanges();
        Check(changes.Take(evaluator.Evaluate(1)).Single(c => c.Control == RobotControls.NeckTurn).Speed == ServoSpeed.Slow,
            "First hardware pose initializes the active speed profile");
        Check(changes.Take(evaluator.Evaluate(1)).Length == 0, "Repeated poses do not flood the device queue");
        Check(changes.Take(evaluator.Evaluate(1.5)).Single(c => c.Control == RobotControls.NeckTurn).Speed == ServoSpeed.NoChange,
            "Spline samples update positions without repeatedly configuring speed/acceleration");
        Check(changes.Take(evaluator.Evaluate(4)).Single(c => c.Control == RobotControls.NeckTurn).Speed == ServoSpeed.Fast,
            "A new authored speed profile is sent even while following a spline");
        HardwareSampleQueueChecks();
        HardwareTimelineWindowChecks(commands);
    }

    private static void HardwareSampleQueueChecks()
    {
        using var queue = new OrderedActionQueue("Hardware timeline checks");
        using var started = new ManualResetEventSlim(); using var release = new ManualResetEventSlim(); using var done = new ManualResetEventSlim();
        var output = new ConcurrentQueue<int>();
        queue.Enqueue(() => { started.Set(); release.Wait(TimeSpan.FromSeconds(5)); });
        Check(started.Wait(TimeSpan.FromSeconds(5)), "Device test worker starts");
        for (int i = 1; i <= 100; i++) { int sample = i; queue.EnqueueLatest("before-boundary", () => output.Enqueue(sample)); }
        queue.Enqueue(() => output.Enqueue(101));
        queue.EnqueueLatest("after-boundary", () => output.Enqueue(102));
        queue.Enqueue(done.Set); release.Set();
        Check(done.Wait(TimeSpan.FromSeconds(5)) && output.SequenceEqual(new[] { 100, 101, 102 }),
            "Slow hardware coalesces old samples without crossing authored command boundaries");
        QueueCancellation();
    }

    private static void HardwareTimelineWindowChecks(ServoCommand[] commands)
    {
        var app = new App(); app.InitializeComponent(); DisableTestStartup(app);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(Application).GetField("_startupUri", flags).SetValue(app, null);
        var window = new MainWindow(); // Never shown: hardware stays disconnected.
        void Set(string name, object value) => typeof(MainWindow).GetField(name, flags).SetValue(window, value);
        object Call(string name, params object[] args) => typeof(MainWindow).GetMethod(name, flags).Invoke(window, args);
        string root = CheckDirectory("hardware-timeline-checks"); Directory.CreateDirectory(root);
        Set("_folders", new FolderSettings { ConfigFolder = root, ProjectFolder = root });
        var doc = new AnimationDocument { Commands = commands.Select(c => c.Clone()).ToList() };
        doc.CommandGroupNumbers["hidden"] = 1;
        foreach (var command in doc.Commands) command.GroupId = "hidden";
        using (var writer = new WaveFileWriter(Path.Combine(root, "hidden.wav"), new WaveFormat(44100, 16, 1)))
            writer.Write(new byte[44100 * 2], 0, 44100 * 2);
        // Prepare offscreen so the test does not open a loading dialog owned by an unshown editor.
        ((MediaAssetCache)typeof(MainWindow).GetField("_mediaCache", flags).GetValue(window)).Audio(Path.Combine(root, "hidden.wav")).GetAwaiter().GetResult();
        doc.Commands.Add(new() { Servo = ServoNames.Play, OffsetSeconds = 7, TextValue = "hidden.wav", GroupId = "hidden" });
        doc.Commands.Add(new() { Servo = ServoNames.RGBCommand, OffsetSeconds = 2, TextValue = "ClearAll", GroupId = "hidden" });
        Set("_doc", doc);
        Call("ApplySplineSettings", new[] { ServoNames.NeckTurn, ServoNames.NeckNodUp, ServoNames.NeckTiltRight, ServoNames.IrisClose });
        Call("RefreshAfterEdit");
        var wave = (WaveformView)window.FindName("Waveform");
        var source = Call("DesiredSourceAt", 7.5);
        Check(source != null, $"Audio source is playable: {string.Join("; ", wave.AudioClips.Select(c => $"{c.Start}/{c.Duration}/{c.ResolvedPath}"))}");
        doc.HiddenCommandGroups.Add("hidden"); Call("RefreshGroupVisibility");
        Check(wave.Markers.Count == 0 && wave.AudioClips.Count == 0, "Hide Group removes command markers and audio handles visually");
        var hiddenSource = Call("DesiredSourceAt", 7.5);
        Check(source != null && hiddenSource != null && ReferenceEquals(source.GetType().GetField("Key").GetValue(source), hiddenSource.GetType().GetField("Key").GetValue(hiddenSource)),
            "Hiding audio retains its playback source without restarting it");
        var ordered = (ServoCommand[])typeof(MainWindow).GetField("_orderedCommands", flags).GetValue(window);
        Check(ordered.Length == doc.Commands.Count && ordered.Any(c => c.Servo == ServoNames.RGBCommand), "Hidden movement, audio and RGB remain in the playback command stream");
        var curve = ((SplineView)window.FindName("Spline")).Curves.Single(c => c.Servo == ServoNames.NeckTurn);
        Check(curve.T.Length == 4 && curve.PointVisible.All(p => !p), "Hidden spline points remain in interpolation data but cannot be seen or selected");
        var hardware = (TimelineHardwarePose)typeof(MainWindow).GetField("_timelineHardwarePose", flags).GetValue(window);
        Check(hardware.Evaluate(1).Single(c => c.Control == RobotControls.NeckTurn).NumericValue == 50, "Hiding a group does not change physical spline targets");
        Call("StartPlaybackAt", 1d, false); Call("StopPlaybackRendering");
        var head = (RobotHeadView)window.FindName("EmbeddedHeadView");
        Check(head.CapturePose().NeckTurn == 50, "Playback begins with URDF snapped to the cursor's interpolated pose");
        Check(!window.IsLoaded && !((HardwareManager)typeof(MainWindow).GetField("_hw", flags).GetValue(window)).Connected,
            "Regression checks never initialize or move physical hardware");
        Call("StopPlayback", true);
        Call("SetCursor", 1d);
        Call("PlayPause_Click", null, new RoutedEventArgs()); Call("StopPlaybackRendering");
        Check((double)typeof(MainWindow).GetField("_cursorTime", flags).GetValue(window) == 1d && head.CapturePose().NeckTurn == 50,
            "The Sequence button starts at the current cursor rather than resetting to zero");
        Call("StopPlayback", true);
        Call("StartPlaybackAt", 3d, false); Call("StopPlaybackRendering");
        Check(head.CapturePose().NeckTurn == 100, "Starting inside a broken interval uses its held pose");
        Call("StopPlayback", true);
        doc.HiddenCommandGroups.Clear(); Call("RefreshGroupVisibility");
        Check(wave.Markers.Count > 0 && wave.AudioClips.Count == 1, "Show Group restores visual commands and audio handles");
    }
}
