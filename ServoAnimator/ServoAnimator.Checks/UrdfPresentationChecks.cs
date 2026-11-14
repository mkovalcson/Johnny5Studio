using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using ServoAnimator;

internal static partial class Program
{
    private static void UrdfPresentationChecks()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var app = new App(); app.InitializeComponent(); DisableTestStartup(app); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        typeof(Application).GetField("_startupUri", flags).SetValue(app, null);
        var window = new MainWindow(); // Never show: Loaded connects hardware.
        object Call(string name, params object[] args) => typeof(MainWindow).GetMethod(name, flags).Invoke(window, args);
        var view = (RobotHeadView)window.FindName("EmbeddedHeadView");
        object ViewCall(string name, params object[] args) => typeof(RobotHeadView).GetMethod(name, flags).Invoke(view, args);
        void ViewSet(string name, object value) => typeof(RobotHeadView).GetField(name, flags).SetValue(view, value);
        object ViewGet(string name) => typeof(RobotHeadView).GetField(name, flags).GetValue(view);
        var initial = new UrdfFraming { HorizontalPercent = 12, VerticalPercent = 24, Distance = 1.15, Yaw = .2, Pitch = .1 };
        // Consume the saved startup camera, as normal window initialization does.
        view.ApplyCameraState(0, 0, 1.15);
        view.ApplyFraming(initial);
        var captured = view.CaptureCurrentFraming();
        Check(view.GetCameraState().Distance == 1.15 && captured.HorizontalPercent == 12, "Reading current framing leaves camera unchanged");
        var doc = (AnimationDocument)typeof(MainWindow).GetField("_doc", flags).GetValue(window);
        doc.Commands.Clear();
        typeof(MainWindow).GetField("_cursorTime", flags).SetValue(window, 2d);
        Check((bool)Call("SaveUrdfPositionAtCursor", initial, null, false), "Save Position accepts current framing");
        Check(doc.Commands.Count == 7 && doc.Commands.All(c => c.OffsetSeconds == 2 && ServoCommand.IsUrdfOnly(c.Servo)), "Save Position inserts seven URDF commands at cursor");
        Check(doc.Commands.Single(c => c.Servo == ServoNames.URDFZoom).NumericValue == 100, "Zoom is saved in percent");
        foreach (var command in doc.Commands)
        {
            var copy = JsonSerializer.Deserialize<ServoCommand>(JsonSerializer.Serialize(command));
            Check(copy.Servo == command.Servo && copy.NumericValue == command.NumericValue && copy.Control == null, "URDF command round-trips");
            Check(CommandVM.ServoPickOptions.Any(p => p.Servo == command.Servo), "URDF command is available in editor");
            var vm = new CommandVM(copy, null, null, null, null, null, new CommandSplineDraft(new[] { copy.Servo }));
            Check(vm.SupportsSpline && vm.SplineEnabled && !vm.SupportsSpeed && !vm.SupportsDisable, "URDF exposes spline, not physical speed or disable");
        }
        doc.Commands.Clear();
        doc.Commands.AddRange(new[] {
            new ServoCommand { Servo = ServoNames.URDFSlide, OffsetSeconds = 1, NumericValue = -40 },
            new ServoCommand { Servo = ServoNames.URDFSlide, OffsetSeconds = 3, NumericValue = 40 }
        });
        Call("RefreshAfterEdit"); Call("UpdateServoState", 2d);
        Check(view.CaptureCurrentFraming().HorizontalPercent == -40, "Unsplined command holds until next command");
        Call("UpdateServoState", 3d);
        Check(view.CaptureCurrentFraming().HorizontalPercent == 40, "Unsplined command jumps at its time");
        Call("ApplySplineSettings", new HashSet<ServoNames> { ServoNames.URDFSlide });
        Call("RefreshAfterEdit");
        Call("UpdateServoState", 2d);
        Check(Math.Abs(view.CaptureCurrentFraming().HorizontalPercent) < .001, "Spline interpolates midpoint");
        Call("UpdateServoState", 0d);
        Check(view.CaptureCurrentFraming().HorizontalPercent == initial.HorizontalPercent, "Seeking before first command restores prior framing");
        view.ApplyUrdfCommand(ServoNames.URDFSpin, 90);
        var model = (Model3DGroup)ViewGet("_presentationModel");
        var pivot = new Point3D(.123658047, .000000084, .131272970);
        Check((model.Transform.Transform(pivot) - pivot).Length < 1e-8, "Spin keeps neck pivot fixed");
        view.ApplyUrdfCommand(ServoNames.URDFSpin, 0);
        view.ApplyUrdfCommand(ServoNames.URDFRotate, 90);
        var center = (Point3D)ViewGet("_modelRotateCenter");
        var right = model.Transform.Transform(center + new Vector3D(0, 1, 0)) - center;
        Check(right.Z < -.99, "Positive roll turns clockwise from front");
        view.ApplyUrdfCommand(ServoNames.URDFZoom, 200);
        Check(Math.Abs(view.GetCameraState().Distance - .575) < 1e-8, "Zoom percent uses existing camera distance");
        typeof(MainWindow).GetField("_hw", flags).SetValue(window, null);
        var urdfCommands = Enum.GetValues<ServoNames>().Where(ServoCommand.IsUrdfOnly).Select(s => new ServoCommand { Servo = s, Control = RobotControls.NeckTurn, Disable = true }).ToArray();
        Call("DispatchHardwareBatch", (object)urdfCommands);
        Check(true, "Even malformed URDF commands never dereference hardware dispatch");

        var bitmap = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[16], 8);
        var settings = new UrdfPresentationSettings { BackgroundMode = UrdfBackgroundMode.MovingImage, BackgroundRotation = true, BackgroundHalfTurns = true, BackgroundSeed = 42, BackgroundPanAmount = 100 };
        var roundTrip = JsonSerializer.Deserialize<UrdfPresentationSettings>(JsonSerializer.Serialize(settings));
        Check(roundTrip.BackgroundSeed == 42 && roundTrip.BackgroundRotation && roundTrip.BackgroundHalfTurns, "Motion settings persist");
        void Configure() { ViewSet("_configuredBackground", new ImageBrush(bitmap)); ViewCall("ConfigureMovingBackground", settings); }
        Configure();
        object first = ViewGet("_backgroundFrom"), next = ViewGet("_backgroundTo"); Configure();
        Check(first.Equals(ViewGet("_backgroundFrom")) && next.Equals(ViewGet("_backgroundTo")), "Seed reproduces target sequence");
        ViewCall("UpdateMovingBackgroundSubscription");
        Check(!(bool)ViewGet("_backgroundSubscribed"), "Unloaded background has no active frame subscription");
        foreach (var size in new[] { new Size(800, 450), new Size(450, 800), new Size(1200, 200) })
        {
            view.Measure(size); view.Arrange(new Rect(size));
            foreach (double angle in new[] { -15d, 0d, 15d, 165d, 180d, 195d })
            foreach (double zoom in new[] { 1d, 1.1, 2d })
            {
                ViewSet("_backgroundFrom", (zoom, 1d, -1d, angle));
                ViewSet("_backgroundTo", (zoom, 1d, -1d, angle));
                ViewCall("RenderMovingBackground");
                var inverse = ((MatrixTransform)ViewGet("_backgroundTransform")).Matrix; inverse.Invert();
                foreach (var corner in new[] { new Point(0, 0), new Point(1, 0), new Point(0, 1), new Point(1, 1) })
                {
                    var point = inverse.Transform(corner);
                    Check(point.X >= -1e-8 && point.X <= 1 + 1e-8 && point.Y >= -1e-8 && point.Y <= 1 + 1e-8, "Rotated/panned background covers viewport corners");
                }
            }
        }
        string backdropRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "J5-fit-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(System.IO.Path.Combine(backdropRoot, "Backdrops"));
        try
        {
            string imagePath = System.IO.Path.Combine(backdropRoot, "Backdrops", "wide.png");
            var pixels = new byte[160 * 90 * 4];
            for (int y = 0; y < 90; y++)
            for (int x = 0; x < 160; x++)
            {
                int index = (y * 160 + x) * 4;
                bool edge = x < 2 || x >= 158 || y < 2 || y >= 88;
                pixels[index] = (byte)(edge ? 80 : 120 + y);
                pixels[index + 1] = (byte)(edge ? 190 : 90 + x / 2);
                pixels[index + 2] = (byte)(edge ? 240 : 55);
                pixels[index + 3] = 255;
            }
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(160, 90, 96, 96, PixelFormats.Bgra32, null, pixels, 160 * 4)));
            using (var output = System.IO.File.Create(imagePath)) encoder.Save(output);
            view.PresentationRootOverride = backdropRoot;
            var still = new UrdfPresentationSettings { BackgroundMode = UrdfBackgroundMode.Image, BackgroundImage = "Backdrops/wide.png" };
            foreach (var legacy in Enum.GetValues<UrdfImageLayout>())
            {
                still.ImageLayout = legacy;
                var brush = (ImageBrush)still.CreateBrush(backdropRoot, Brushes.Black);
                Check(brush.Stretch == Stretch.Uniform && brush.TileMode == TileMode.None, "Every legacy static layout fits the entire image without distortion");
            }
            view.SetTimelineBackground(still);
            var fitFrame = new UrdfFraming { HorizontalPercent = 12, VerticalPercent = -8, Distance = 1.15, Yaw = .2, Pitch = .1 };
            view.ApplyFraming(fitFrame);
            Point? firstProjection = null, secondProjection = null;
            foreach (var size in new[] { new Size(800, 450), new Size(1600, 900), new Size(900, 450), new Size(450, 800) })
            {
                view.Measure(size); view.Arrange(new Rect(size)); view.UpdateLayout();
                ViewCall("UpdateCamera");
                var area = (Rect)ViewCall("StaticBackdropArea", size.Width, size.Height);
                Check(Math.Abs(area.Width / area.Height - 16d / 9) < 1e-9
                    && (Math.Abs(area.Width - size.Width) < 1e-9 || Math.Abs(area.Height - size.Height) < 1e-9), "Image fits edge-to-edge in one dimension and preserves aspect ratio");
                Point Project(Point3D world)
                {
                    object[] arguments = { world, new Point() };
                    Check((bool)typeof(RobotHeadView).GetMethod("TryProjectWorldPoint", flags).Invoke(view, arguments), "Robot reference point projects in front of camera");
                    var p = (Point)arguments[1];
                    return new Point((p.X - area.X) / area.Width, (p.Y - area.Y) / area.Height);
                }
                var a = Project(pivot); var b = Project(pivot + new Vector3D(0, .05, .05));
                if (firstProjection is Point firstPoint && secondProjection is Point secondPoint)
                    Check((a - firstPoint).Length < 1e-7 && (b - secondPoint).Length < 1e-7, "Robot position and scale relative to the image survive size and aspect changes");
                firstProjection = a; secondProjection = b;
                var retained = view.CaptureCurrentFraming();
                Check(retained.Distance == fitFrame.Distance && retained.HorizontalPercent == fitFrame.HorizontalPercent && retained.VerticalPercent == fitFrame.VerticalPercent,
                    "Resizing never rewrites saved zoom or placement");
                if (size.Width == 800 || size.Width == 450)
                {
                    string previewFolder = PreviewDirectory("renderer-previews");
                    System.IO.Directory.CreateDirectory(previewFolder);
                    var preview = new PngBitmapEncoder();
                    preview.Frames.Add(BitmapFrame.Create(view.RenderSnapshot(800, null, TvFit.Contain)));
                    using var output = System.IO.File.Create(System.IO.Path.Combine(previewFolder, $"Static-image-fit-{size.Width}x{size.Height}.png"));
                    preview.Save(output);
                }
            }
            view.SetTimelineBackground(new UrdfPresentationSettings { BackgroundMode = UrdfBackgroundMode.Studio });
            Check(Math.Abs(((PerspectiveCamera)typeof(RobotHeadView).GetField("_camera", flags).GetValue(view)).FieldOfView - 38) < 1e-9,
                "Leaving a static image restores the original camera projection");
            still.BackgroundMode = UrdfBackgroundMode.MovingImage;
            Check(((ImageBrush)still.CreateBrush(backdropRoot, Brushes.Black)).Stretch == Stretch.UniformToFill, "Moving backgrounds retain cover layout");
        }
        finally { view.PresentationRootOverride = null; System.IO.Directory.Delete(backdropRoot, true); }
        Check(!window.IsLoaded && !view.IsLoaded, "Checks never load hardware-owning windows");
    }
}
