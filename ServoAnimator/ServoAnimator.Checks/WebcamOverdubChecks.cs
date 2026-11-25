using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Media3D;
using ServoAnimator;

internal static partial class Program
{
    private static void WebcamOverdubChecks()
    {
        ServoCommand Key(ServoNames servo, int value, double time) =>
            new() { Servo = servo, NumericValue = value, OffsetSeconds = time };
        var original = new[]
        {
            Key(ServoNames.NeckTurn, 11, 5.5), Key(ServoNames.NeckNodUp, 23, 5.5),
            Key(ServoNames.NeckTiltRight, 17, 5.5), Key(ServoNames.NeckNodUp, 29, 7),
            Key(ServoNames.B_ElbowLeft, 16, 5.5)
        };
        var take = new ControllerRecording(5, [ServoNames.NeckNodUp, ServoNames.NeckTurn],
            logicalNeck: [ServoNames.NeckNodUp]);
        take.Accept(Key(ServoNames.NeckNodUp, 0, 5)); take.Sample(5);
        take.Accept(Key(ServoNames.NeckNodUp, 42, 6));
        take.Accept(Key(ServoNames.NeckTiltRight, 99, 6)); take.Sample(6);
        var recorded = take.Process(0, 0, new HashSet<ServoNames>());
        var merged = take.Merge(original, recorded);
        Check(recorded.All(c => c.Servo == ServoNames.NeckNodUp && c.Control == null) &&
            recorded.Last().NumericValue == 42, "A nod-only face take records logical nod and rejects tilt samples");
        Check(merged.Any(c => c.Servo == ServoNames.NeckTiltRight && c.NumericValue == 17) &&
            merged.Any(c => c.Servo == ServoNames.B_ElbowLeft && c.NumericValue == 16) &&
            merged.Any(c => c.Servo == ServoNames.NeckNodUp && c.OffsetSeconds == 7 && c.NumericValue == 29) &&
            !merged.Any(c => c.Servo == ServoNames.NeckNodUp && c.OffsetSeconds == 5.5),
            "Keep replaces only the chosen axis inside the recorded interval and preserves body commands");

        var both = new ControllerRecording(5, [], logicalNeck: [ServoNames.NeckNodUp, ServoNames.NeckTiltRight]);
        both.Accept(Key(ServoNames.NeckNodUp, 18, 5));
        both.Accept(Key(ServoNames.NeckTiltRight, -21, 5));
        both.Sample(5); both.Sample(6);
        Check(both.Frames.All(frame => frame.Commands.Length == 2), "Logical nod and tilt are saved at the same sample time");
        var piston = Key(ServoNames.NeckNodUp, 26, 5.5);
        piston.Control = RobotControls.NeckTiltLeft;
        Check(!both.Merge([piston], both.Process(0, 0, new HashSet<ServoNames>())).Any(c => c.Control.HasValue),
            "Replacing both logical axes removes direct piston keys within the interval");

        var app = new App(); app.InitializeComponent(); DisableTestStartup(app);
        string model = Path.GetFullPath("ConfigTemplate/Models/Robots/Johnny5FullBody/johnny5_head.urdf");
        var source = new RobotHeadView(deferModelLoad: true);
        source.SetServoConfiguration(new ServoConfiguration());
        source.InstallModel(model, UrdfScene.Load(model, optimizeExterior: false), new UrdfFraming());
        source.SetUrdfDriveEnabled(true);
        Viewport3D[] views = [new(), new(), new()];
        PerspectiveCamera[] cameras = [new(), new(), new()];
        for (int i = 0; i < views.Length; i++)
        {
            views[i].Measure(new Size(340, 230)); views[i].Arrange(new Rect(0, 0, 340, 230));
            views[i].Camera = cameras[i];
            views[i].Children.Add(new ModelVisual3D { Content = source.OverdubSceneRoot });
        }
        source.FrameWebcamOverdub(views, cameras);
        var beforeFront = cameras[0].LookDirection; beforeFront.Normalize();
        var beforePov = cameras[1].LookDirection; beforePov.Normalize();
        source.SetServo(ServoNames.NeckTurn, 60);
        source.SetServo(ServoNames.NeckNodUp, 33);
        source.FrameWebcamOverdub(views, cameras);
        var front = cameras[0].LookDirection; front.Normalize();
        var pov = cameras[1].LookDirection; pov.Normalize();
        Check((front - beforeFront).Length < .001 && (pov - beforePov).Length > .05,
            "The Front camera stays square to the shoulders while the POV follows combined head motion");
        Check(cameras.All(c => double.IsFinite(c.Position.X) && double.IsFinite(c.LookDirection.Length)) &&
            cameras[2].LookDirection.Length > cameras[0].LookDirection.Length,
            "The Wide camera fits the complete robot from a larger distance");
        Check(views.All(v => ReferenceEquals(((ModelVisual3D)v.Children[0]).Content, source.OverdubSceneRoot)),
            "Three viewport visuals can share the live URDF scene without copying the model");
    }
}
