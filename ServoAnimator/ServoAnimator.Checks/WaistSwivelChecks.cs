using System.IO;
using System.Windows.Media.Media3D;
using ServoAnimator;

internal static partial class Program
{
    private static void WaistSwivelChecks()
    {
        string body = Path.GetFullPath("ServoAnimator/FullBodyModel/johnny5_head.urdf");
        var scene = UrdfScene.Load(body);
        Point3D World(string link) { scene.TryTransformLinkPoint(link, new Point3D(), out var p); return p; }
        Check(scene.HasJoint("WaistSwivel"), "The body has a WaistSwivel joint");

        // Geometry at rest (scaled from the photograph): the axis leans 7 degrees forward and runs 93 mm from the waist
        // drive support's centre to the hinge pin; the swivel sprocket is 30 mm below the pin, the motor sprocket 75 mm
        // behind it in the same plane, on top of the drive box.
        var joint = System.Xml.Linq.XDocument.Load(body).Root.Elements("joint").Single(j => (string)j.Attribute("name") == "WaistSwivel");
        var a = ((string)joint.Element("axis").Attribute("xyz")).Split(' ').Select(double.Parse).ToArray();
        var axis = new Vector3D(a[0], a[1], a[2]); axis.Normalize();
        Check(Math.Abs(Math.Acos(axis.Z) * 180 / Math.PI - 7) < .05 && axis.X > 0 && Math.Abs(axis.Y) < 1e-9, "The swivel axis leans 7 degrees forward");
        var pin = World("body_hinge_pin_link"); var support = World("body_waist_drive_support_link");
        Check(Math.Abs((pin - support).Length - .093) < .0005 && Vector3D.CrossProduct(pin - support, axis).Length < 1e-4,
            "The axis runs 93 mm from the waist drive support's centre to the hinge pin");
        var sprocket = World("body_waist_swivel_sprocket_link");
        Check(Math.Abs(Vector3D.DotProduct(pin - sprocket, axis) - .030) < 1e-4 && Vector3D.CrossProduct(pin - sprocket, axis).Length < 1e-4,
            "The swivel sprocket sits on the axis 30 mm below the pin");
        var drive = World("body_waist_drive_sprocket_link") - sprocket;
        var across = drive - Vector3D.DotProduct(drive, axis) * axis;
        Check(Math.Abs(across.Length - .075) < .001 && across.X < 0 && Math.Abs(across.Y) < 1e-9 && Math.Abs(Vector3D.DotProduct(drive, axis)) < 1e-5,
            "The motor sprocket sits 75 mm behind, level with the swivel sprocket");
        Check(Vector3D.DotProduct(World("body_waist_drive_box_link") - World("body_waist_drive_sprocket_link"), axis) < -.02, "The motor sprocket is on top of the drive box");

        // Motion: the swivel sprocket, hinge and everything above turn about the axis; the support, drive box, motor
        // sprocket, belt and column stay.
        string[] above = { "body_hinge_pin_cap_l_link", "body_seg_shoulder_left_link", "base_link" };
        string[] still = { "body_waist_drive_support_link", "body_waist_drive_box_link", "body_waist_drive_sprocket_link", "body_waist_drive_belt_link",
                           "body_waist_bellows_lower_00_link", "body_waist_bellows_lower_15_link",
                           "body_waist_ring_top_link", "body_waist_column_link" };
        var before = above.Concat(still).ToDictionary(l => l, World);
        scene.SetJoint("WaistSwivel", Math.PI / 2);
        Check(still.All(l => (World(l) - before[l]).Length < 1e-9), "Twisting leaves the support, drive box, motor sprocket, belt, lower bellows and column in place");
        Check((World("body_waist_swivel_sprocket_link") - sprocket).Length < 1e-5, "The swivel sprocket turns about its own centre");
        Check(above.All(l => (World(l) - before[l]).Length > .01), "The hinge, chest and head turn with the swivel");
        Vector3D Radial(Point3D p) { var v = p - pin; return v - Vector3D.DotProduct(v, axis) * axis; }
        // link origins are rounded to 1 um, so allow 0.2 mm out at the head
        Check(above.All(l => Math.Abs(Radial(World(l)).Length - Radial(before[l]).Length) < 2e-4
                            && Math.Abs(Vector3D.DotProduct(World(l) - pin, axis) - Vector3D.DotProduct(before[l] - pin, axis)) < 2e-4),
            "Parts turn on circles about the column axis");
        var cap = Radial(before["body_hinge_pin_cap_l_link"]); var capNow = Radial(World("body_hinge_pin_cap_l_link"));
        Check(Math.Abs(Vector3D.AngleBetween(cap, capNow) - 90) < .01 && Vector3D.DotProduct(Vector3D.CrossProduct(cap, capNow), axis) > 0,
            "Positive twist turns the upper body anticlockwise seen from above");
        var twisted = World("body_seg_shoulder_left_link");
        scene.SetJoint("ChestTilt", .2);
        Check((World("body_seg_shoulder_left_link") - twisted).Length > .01, "ChestTilt still works on the twisted hinge");
        scene.SetJoint("ChestTilt", 0); scene.SetJoint("WaistSwivel", 0);
        Check(above.All(l => (World(l) - before[l]).Length < 1e-9), "Returning to 0 restores the upper body exactly");

        // The B_WaistSwivel control: listed right after B_ChestTilt, drives the joint over its full range.
        var paired = BodyControlCatalog.PairedPicklistControls.Select(c => c.Servo).ToList();
        Check(paired.IndexOf(ServoNames.B_WaistSwivel) == paired.IndexOf(ServoNames.B_ChestTilt) + 1,
            "B_WaistSwivel follows B_ChestTilt in the picklists and the URDF Configuration screen");
        var control = BodyControlCatalog.Find(ServoNames.B_WaistSwivel);
        Check(control.Joint == "WaistSwivel" && ServoCommand.IsUrdfOnly(ServoNames.B_WaistSwivel), "B_WaistSwivel is a URDF-only body control for the WaistSwivel joint");
        var setting = BodyMotionSetting.Default(control);
        Check(Math.Abs(setting.Map(100, control) - 1.57) < 1e-9 && Math.Abs(setting.Map(-100, control) + 1.57) < 1e-9 && setting.Map(0, control) == 0,
            "Command -100..100 maps to -90..90 degrees, 0 at the built pose");
        var app = new App(); app.InitializeComponent(); DisableTestStartup(app);
        Check(ServoIconProvider.For(ServoNames.B_WaistSwivel) != null, "B_WaistSwivel has its control icon");
        var view = new RobotHeadView();
        var viewScene = UrdfScene.Load(body); view.InstallModel(body, viewScene, new UrdfFraming());
        Point3D ViewWorld(string link) { viewScene.TryTransformLinkPoint(link, new Point3D(), out var p); return p; }
        var shoulder = ViewWorld("body_seg_shoulder_left_link");
        view.ApplyBodyControl(ServoNames.B_WaistSwivel, 100);
        Check(Math.Abs(Vector3D.AngleBetween(Radial(shoulder), Radial(ViewWorld("body_seg_shoulder_left_link"))) - 90) < .1 && view.BodyPosition(ServoNames.B_WaistSwivel) == 100,
            "B_WaistSwivel +100 turns the upper body 90 degrees in the URDF view");
        view.ApplyBodyControl(ServoNames.B_WaistSwivel, 0);
        Check((ViewWorld("body_seg_shoulder_left_link") - shoulder).Length < 1e-9, "B_WaistSwivel 0 restores the built pose");
        app.Shutdown();
    }
}
