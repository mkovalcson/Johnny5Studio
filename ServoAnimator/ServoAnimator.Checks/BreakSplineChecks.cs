using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using ServoAnimator;
using SkiaSharp;

internal static partial class Program
{
    private static void BreakSplineChecks()
    {
        var app = new App(); app.InitializeComponent(); DisableTestStartup(app); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        typeof(Application).GetField("_startupUri", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(app, null);
        var json = new JsonSerializerOptions(); json.Converters.Add(new ServoCommandJsonConverter());
        var command = new ServoCommand { Servo = ServoNames.NeckTurn, OffsetSeconds = 3, NumericValue = 20, BreakSpline = true };
        Check(JsonSerializer.Deserialize<ServoCommand>(JsonSerializer.Serialize(command, json), json).BreakSpline, "Break Spline survives command JSON roundtrip");
        Check(!JsonSerializer.Deserialize<ServoCommand>("{\"servo\":\"NeckTurn\",\"value\":20}", json).BreakSpline, "Older commands default to a continuous line");
        Check(command.Clone().BreakSpline && new AnimationDocument { Commands = new() { command } }.Clone().Commands[0].BreakSpline,
            "Command/document snapshots preserve breaks for copy/paste and undo");
        var original = command.Clone(); original.BreakSpline = false;
        var session = new CommandEditSession(new[] { original }, 3);
        var draft = new CommandSplineDraft(new[] { ServoNames.NeckTurn });
        var vm = new CommandVM(session.Commands[0], null, null, null, null, null, draft);
        Check(vm.SupportsBreakSpline, "Spline-enabled numeric commands allow a break");
        vm.BreakSpline = true;
        Check(!original.BreakSpline && session.Merge(new[] { original }).Single().BreakSpline, "Break-only edits remain drafts until merged and are recognized as changes");
        vm.SplineEnabled = false;
        Check(!vm.SupportsBreakSpline && vm.BreakSpline, "Turning spline off preserves the saved break for later use");
        vm.SplineEnabled = true; vm.Disable = true;
        Check(!vm.SupportsBreakSpline, "Disabled commands do not offer a nonexistent spline segment");
        vm.Disable = false;
        Check(vm.SupportsBreakSpline, "Reenabling a numeric command restores the break checkbox");
        vm.SelectedServoItem = CommandVM.ServoPickOptions.First(i => i.Servo == ServoNames.RGBCommand);
        Check(!vm.SupportsBreakSpline, "RGB commands cannot create numeric spline breaks");

        var view = new SplineView { PixelsPerSecond = 80, Duration = 10, ViewStart = 0 };
        view.Measure(new Size(900, 200)); view.Arrange(new Rect(0, 0, 900, 200));
        var curve = new SplineCurve { Servo = ServoNames.NeckTurn, T = new[] { 1.0, 3, 5, 7 }, V = new double[4], M = new double[4],
            Min = -100, Max = 100, Color = SKColors.Lime, BreakAfter = new[] { false, true, false, true } };
        view.Curves = new() { curve };
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        float X(double time) => view.XAtTime(time);
        var draw = typeof(SplineView).GetMethod("DrawCurve", flags);
        using var bitmap = new SKBitmap(900, 200); using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent); draw.Invoke(view, new object[] { canvas, curve, 900f, 200f, 8f });
        Check(bitmap.GetPixel((int)X(2), 100).Alpha > 0 && bitmap.GetPixel((int)X(4), 100).Alpha == 0 && bitmap.GetPixel((int)X(6), 100).Alpha > 0,
            "Renderer leaves precisely the outgoing segment blank and resumes at the next command");
        Check(bitmap.GetPixel((int)X(3), 100).Alpha > 0 && bitmap.GetPixel((int)X(5), 100).Alpha > 0, "Both ends of a broken segment retain editable command dots");
        object HitLine(double time) => typeof(SplineView).GetMethod("HitLine", flags).Invoke(view, new object[] { new Point(X(time), 100), 0.0, 0.0 });
        Check(HitLine(4) == null && ReferenceEquals(HitLine(6), curve), "Hidden spline segments cannot be selected as phantom lines");
        curve.BreakAfter[1] = false;
        canvas.Clear(SKColors.Transparent); draw.Invoke(view, new object[] { canvas, curve, 900f, 200f, 8f });
        Check(bitmap.GetPixel((int)X(4), 100).Alpha > 0, "Unchecking Break Spline restores its line");

        var window = new MainWindow(); // Not shown: no hardware connections.
        BreakSplinePlaybackChecks(window);
        var doc = new AnimationDocument { Commands = new()
        {
            new() { Servo = ServoNames.NeckTurn, OffsetSeconds = 1, BreakSpline = true },
            new() { Servo = ServoNames.NeckTurn, OffsetSeconds = 3 },
            new() { Servo = ServoNames.NeckTurn, OffsetSeconds = 2, Disable = true },
            new() { Servo = ServoNames.NeckNodUp, OffsetSeconds = 1, BreakSpline = true },
            new() { Servo = ServoNames.NeckTiltRight, OffsetSeconds = 3 },
            new() { Servo = ServoNames.NeckNodUp, OffsetSeconds = 5 }
        } };
        typeof(MainWindow).GetField("_doc", flags).SetValue(window, doc);
        typeof(MainWindow).GetMethod("ApplySplineSettings", flags).Invoke(window, new object[] { new[] { ServoNames.NeckTurn, ServoNames.NeckNodUp } });
        typeof(MainWindow).GetMethod("RebuildSplineData", flags).Invoke(window, null);
        var curves = ((SplineView)window.FindName("Spline")).Curves;
        var normal = curves.Single(c => c.Servo == ServoNames.NeckTurn);
        // Nod and tilt are complementary, so each is an ordinary curve (no merged "shared neck" curve).
        var nod = curves.Single(c => c.Servo == ServoNames.NeckNodUp);
        Check(normal.T.SequenceEqual(new[] { 1.0, 3 }) && !normal.IsSegmentVisible(0), "Curve rebuilding aligns breaks with authored points, excluding disabled commands");
        Check(!curves.Any(c => c.IsSharedNeck) && nod.T.SequenceEqual(new[] { 1.0, 5 }) && !nod.IsSegmentVisible(0),
              "Nod is its own curve, with its break on its outgoing segment");
        Check(!curves.Any(c => c.Servo == ServoNames.NeckTiltRight), "Tilt is not drawn unless its own spline is enabled");
        string folder = PreviewDirectory("spline-previews"); Directory.CreateDirectory(folder);
        var editor = new CommandEditorWindow(doc, 1, null, null, null, null, null, folder, folder,
            new[] { ServoNames.NeckTurn, ServoNames.NeckNodUp }, (_, _) => true);
        RenderControl((FrameworkElement)editor.Content, Path.Combine(folder, "BreakSplineEditor.png"), 1010, 280);
        string savePath = Path.Combine(folder, "BreakSplineRoundtrip.json"); doc.Save(savePath);
        Check(AnimationDocument.Load(savePath).Commands.Count(c => c.BreakSpline) == 2, "Sequence save/load retains breaks on separate controls");
        SplineWorkflowChecks(window, folder);
    }

    private static void BreakSplinePlaybackChecks(MainWindow window)
    {
        var t = new[] { 0.0, 1, 3, 4 };
        var v = new[] { 0.0, 20, 80, 100 };
        var breaks = new[] { false, true, false, false };
        var m = SplineUtil.Tangents(t, v, breaks);
        Check(m.SequenceEqual(new[] { 20.0, 20, 20, 20 }), "Tangents use only points on the same side of a break");
        foreach (double time in new[] { 1.0, 1.001, 2, Math.BitDecrement(3.0) })
            Check(SplineUtil.Eval(t, v, m, time, breaks) == 20, "Break holds the outgoing value through the entire interval");
        Check(SplineUtil.Eval(t, v, m, 3, breaks) == 80 && SplineUtil.Eval(t, v, m, 3.5, breaks) == 90,
            "Destination takes effect at its timestamp and interpolation resumes afterwards");
        Check(SplineUtil.Eval(t, v, m, .5, breaks) == 10, "Interpolation before a break remains smooth");
        Check(SplineUtil.Eval(t, v, m, -1, breaks) == 0 && SplineUtil.Eval(t, v, m, 5, breaks) == 100,
            "Breaks preserve endpoint holds outside the curve");
        var all = Enumerable.Repeat(true, t.Length).ToArray();
        Check(SplineUtil.Eval(t, v, SplineUtil.Tangents(t, v, all), 3.5, all) == 80, "Consecutive breaks hold each command independently");

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object Call(string name, params object[] args) => typeof(MainWindow).GetMethod(name, flags).Invoke(window, args);
        var doc = new AnimationDocument { Commands = new() };
        foreach (var servo in new[] { ServoNames.NeckTurn, ServoNames.NeckNodUp, ServoNames.NeckTiltRight, ServoNames.URDFSlide })
            for (int i = 0; i < t.Length; i++)
                doc.Commands.Add(new() { Servo = servo, OffsetSeconds = t[i], NumericValue = (int)v[i], BreakSpline = breaks[i] });
        typeof(MainWindow).GetField("_doc", flags).SetValue(window, doc);
        Call("ApplySplineSettings", doc.Commands.Select(c => c.Servo).Distinct().ToArray());
        Call("RebuildSplineData");
        var head = (RobotHeadView)window.FindName("EmbeddedHeadView");
        Call("LoadSpeedCalibration", false);
        typeof(MainWindow).GetField("_speedCalibration", flags).SetValue(window, new SpeedCalibrationData { UseInUrdf = false });
        var rows = (System.Collections.ObjectModel.ObservableCollection<ServoStateRow>)typeof(MainWindow).GetField("_rows", flags).GetValue(window);
        foreach (double time in new[] { .5, 1, 2, 3, 3.5, 2 })
        {
            typeof(MainWindow).GetField("_cursorTime", flags).SetValue(window, time);
            Call("UpdateServoState", time);
            double expected = time < 1 ? 10 : time < 3 ? 20 : time == 3 ? 80 : 90;
            Check(rows.First(r => r.Servo == ServoNames.NeckTurn).Value == expected && head.CapturePose().NeckTurn == expected,
                $"URDF head follows breaks at {time}: row {rows.First(r => r.Servo == ServoNames.NeckTurn).Value}, model {head.CapturePose().NeckTurn}, expected {expected}");
            Check(head.CaptureCurrentFraming().HorizontalPercent == expected, "URDF placement follows the same break behavior");
        }
        foreach (var servo in doc.Commands.Select(c => c.Servo).Distinct())
        {
            var samples = ((IEnumerable<ServoCommand>)Call("GenerateSplineSamples", servo)).ToArray();
            Check(!samples.Any(c => c.OffsetSeconds > 1 && c.OffsetSeconds < 3), "Hardware exports contain no interpolated commands across a break");
            Check(samples.Any(c => c.OffsetSeconds > 3 && c.NumericValue > 80), "Hardware export interpolation resumes after the destination");
        }
        var replay = new ControllerRecordingReplay(doc.Commands, doc.Commands.Select(c => c.Servo).ToHashSet(), 0, 4);
        Check(replay.Evaluate(2).All(c => c.NumericValue == 20) && replay.Evaluate(3).All(c => c.NumericValue == 80),
            "Recording replay respects breaks and exact destination times");
        doc.Commands.Single(c => c.Servo == ServoNames.NeckTurn && c.OffsetSeconds == 1).BreakSpline = false;
        Call("RebuildSplineData"); Call("UpdateServoState", 2d);
        Check(rows.First(r => r.Servo == ServoNames.NeckTurn).Value > 20 && head.CapturePose().NeckTurn > 20,
            "Clearing a break restores interpolated model motion");
    }
}
