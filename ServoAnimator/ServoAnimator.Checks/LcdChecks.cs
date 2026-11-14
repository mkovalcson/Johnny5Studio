using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using ServoAnimator;

internal static partial class Program
{
    private static void LcdChecks()
    {
        string body = Path.GetFullPath("ServoAnimator/FullBodyModel/johnny5_head.urdf");
        var urdf = System.Xml.Linq.XDocument.Load(body).Root;
        System.Xml.Linq.XElement Joint(string name) => urdf.Elements("joint").Single(j => (string)j.Attribute("name") == name);
        var left = Joint("PanelsOpen"); var right = Joint("PanelsOpenRight");
        Check((string)left.Attribute("type") == "revolute" && (string)left.Element("child").Attribute("link") == "body_waist_blue_panel_l_link"
              && (string)right.Element("child").Attribute("link") == "body_waist_blue_panel_r_link", "Each blue waist panel hangs on its own hinge joint");
        Check((string)right.Element("mimic")?.Attribute("joint") == "PanelsOpen", "The right door mimics PanelsOpen, so both move together");
        double Upper(System.Xml.Linq.XElement j) => double.Parse((string)j.Element("limit").Attribute("upper"), System.Globalization.CultureInfo.InvariantCulture);
        Check((string)left.Element("limit").Attribute("lower") == "0" && Math.Abs(Upper(left) - BodyControlCatalog.PanelsOpenMaximum) < 1e-9,
            "PanelsOpen runs from closed (0) to the catalogue's open angle");

        // The doors swing outward about their outer edges, mirrored left/right; the hinges stay put.
        var scene = UrdfScene.Load(body);
        Point3D World(string link, Point3D p) { scene.TryTransformLinkPoint(link, p, out var w); return w; }
        var inner = new Point3D(-.012, -.040, 0);                      // near the left door's inner edge, from its hinge
        var innerR = new Point3D(-.012, .040, 0);
        Point3D hingeL = World("body_waist_blue_panel_l_link", new Point3D()), hingeR = World("body_waist_blue_panel_r_link", new Point3D());
        Point3D l0 = World("body_waist_blue_panel_l_link", inner), r0 = World("body_waist_blue_panel_r_link", innerR);
        Check(Math.Abs(hingeL.Y + hingeR.Y) < 1e-6 && hingeL.Y > .04, "The hinges sit on the doors' outer edges, mirrored about the centre");
        scene.SetJoint("PanelsOpen", BodyControlCatalog.PanelsOpenMaximum);
        Point3D l1 = World("body_waist_blue_panel_l_link", inner), r1 = World("body_waist_blue_panel_r_link", innerR);
        Check((World("body_waist_blue_panel_l_link", new Point3D()) - hingeL).Length < 1e-9 && (World("body_waist_blue_panel_r_link", new Point3D()) - hingeR).Length < 1e-9,
            "Opening turns the doors about their hinges");
        Check(l1.X > l0.X + .02 && r1.X > r0.X + .02 && Math.Abs(l1.Y + r1.Y) < 1e-6 && Math.Abs(l1.X - r1.X) < 1e-6 && Math.Abs(l1.Z - r1.Z) < 1e-6,
            "Both doors swing forward together, as mirror images");
        scene.SetJoint("PanelsOpen", 0);
        Check((World("body_waist_blue_panel_l_link", inner) - l0).Length < 1e-9 && (World("body_waist_blue_panel_r_link", innerR) - r0).Length < 1e-9, "0 closes both doors exactly");

        // The LCD between the hinges.
        Check(scene.HasLcd && scene.LcdAspect > .45 && scene.LcdAspect < .7, $"The waist has an LCD screen, taller than wide (aspect {scene.LcdAspect:F3})");

        // The control.
        var control = BodyControlCatalog.Find(ServoNames.B_PanelsOpen);
        Check(control?.Joint == "PanelsOpen" && ServoCommand.IsUrdfOnly(ServoNames.B_PanelsOpen), "B_PanelsOpen is a URDF-only body control for the PanelsOpen joint");
        var setting = BodyMotionSetting.Default(control);
        Check(setting.Map(0, control) == 0 && setting.Map(-100, control) == 0 && Math.Abs(setting.Map(100, control) - BodyControlCatalog.PanelsOpenMaximum) < 1e-9,
            "B_PanelsOpen 0 (or less) is closed and 100 fully open");
        var paired = BodyControlCatalog.PairedPicklistControls.Select(c => c.Servo).ToList();
        Check(paired.IndexOf(ServoNames.B_PanelsOpen) == paired.IndexOf(ServoNames.B_WaistSwivel) + 1, "B_PanelsOpen follows B_WaistSwivel in the picklists");

        // The LCD Show command.
        Check(ServoCommand.IsTextValued(ServoNames.LcdShow) && ServoCommand.IsUrdfOnly(ServoNames.LcdShow) && ServoCommand.DisplayName(ServoNames.LcdShow) == "LCD Show",
            "LCD Show is a URDF-only text command");
        Check(LcdTimeline.StateAt(Array.Empty<ServoCommand>(), 5).Text == "If you can read this, you should have asked me out on a date first!",
            "Without an LCD Show the screen shows the default message");
        var hello = new LcdShowCommand { Text = "Hello", Color = "#FFFFFF" };
        var picture = new LcdShowCommand { Image = "LcdMedia\\face.png", Fit = TvFit.Cover };
        Check(LcdShowCommand.Read(hello.ToJson()).Text == "Hello" && LcdShowCommand.Read(picture.ToJson()).Fit == TvFit.Cover, "LCD Show settings round-trip");
        Check(Throws(() => LcdShowCommand.Read("{\"color\":\"green\"}")) && Throws(() => LcdShowCommand.Read("{\"image\":\"clip.mp4\"}"))
              && Throws(() => LcdShowCommand.Read("{\"font\":1}")), "Bad colours, non-picture files and unknown settings are rejected");
        var commands = new[]
        {
            new ServoCommand { Servo = ServoNames.LcdShow, OffsetSeconds = 1, TextValue = hello.ToJson() },
            new ServoCommand { Servo = ServoNames.LcdShow, OffsetSeconds = 3, TextValue = picture.ToJson() },
            new ServoCommand { Servo = ServoNames.LcdShow, OffsetSeconds = 4, TextValue = new LcdShowCommand { Text = "off" }.ToJson(), Disable = true },
        };
        Check(LcdTimeline.StateAt(commands, .5).Text == LcdShowCommand.DefaultText && LcdTimeline.StateAt(commands, 2).Text == "Hello"
              && LcdTimeline.StateAt(commands, 5).HasImage, "The latest enabled LCD Show at or before the cursor wins");

        var app = new App(); app.InitializeComponent(); DisableTestStartup(app);
        Check(ServoIconProvider.For(ServoNames.B_PanelsOpen) != null && ServoIconProvider.For(ServoNames.LcdShow) != null, "B_PanelsOpen and LCD Show have their icons");
        var image = (BitmapSource)LcdRenderer.Render(LcdShowCommand.Default, null, scene.LcdAspect);
        Check(Math.Abs((double)image.PixelWidth / image.PixelHeight - scene.LcdAspect) < .01, "The default message is drawn at the screen's shape");
        var pixels = new byte[image.PixelWidth * image.PixelHeight * 4]; image.CopyPixels(pixels, image.PixelWidth * 4, 0);
        Check(Enumerable.Range(0, pixels.Length / 4).Count(i => pixels[i * 4 + 1] > 150) > 500, "The message is actually drawn (bright green text)");

        // In the URDF view: the control opens both doors; the LCD shows timeline content.
        var view = new RobotHeadView();
        var viewScene = UrdfScene.Load(body); view.InstallModel(body, viewScene, new UrdfFraming());
        Point3D ViewWorld(string link, Point3D p) { viewScene.TryTransformLinkPoint(link, p, out var w); return w; }
        var closedL = ViewWorld("body_waist_blue_panel_l_link", inner); var closedR = ViewWorld("body_waist_blue_panel_r_link", innerR);
        view.ApplyBodyControl(ServoNames.B_PanelsOpen, 100);
        Check((ViewWorld("body_waist_blue_panel_l_link", inner) - closedL).Length > .02 && (ViewWorld("body_waist_blue_panel_r_link", innerR) - closedR).Length > .02
              && view.BodyPosition(ServoNames.B_PanelsOpen) == 100, "B_PanelsOpen +100 opens both doors in the URDF view");
        view.ApplyBodyControl(ServoNames.B_PanelsOpen, 0);
        Check((ViewWorld("body_waist_blue_panel_l_link", inner) - closedL).Length < 1e-9 && (ViewWorld("body_waist_blue_panel_r_link", innerR) - closedR).Length < 1e-9,
            "B_PanelsOpen 0 closes them again");
        Check(view.HasLcd, "The URDF view's robot has the LCD");
        view.SetLcd(hello, null);
        app.Shutdown();
    }
}
