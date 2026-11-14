using System.IO;
using System.Windows.Media.Media3D;
using ServoAnimator;

internal static partial class Program
{
    private static void LaserChecks()
    {
        string body = Path.GetFullPath("ServoAnimator/FullBodyModel/johnny5_head.urdf");
        var scene = UrdfScene.Load(body);
        Point3D World(string link) { scene.TryTransformLinkPoint(link, new Point3D(), out var p); return p; }

        // The mount: as wide as the hinge pin's left cap, from its outer face out to the end of the left shoulder hub.
        var urdf = System.Xml.Linq.XDocument.Load(body).Root;
        var mount = urdf.Elements("link").Single(l => (string)l.Attribute("name") == "body_laser_mount_link");
        var cylinder = mount.Element("visual").Element("geometry").Element("cylinder");
        var cap = urdf.Elements("link").Single(l => (string)l.Attribute("name") == "body_hinge_pin_cap_l_link").Element("visual").Element("geometry").Element("cylinder");
        Check((string)cylinder.Attribute("radius") == (string)cap.Attribute("radius"), "The laser mount is as wide as the hinge pin cap");
        double length = double.Parse((string)cylinder.Attribute("length"), System.Globalization.CultureInfo.InvariantCulture);
        var centre = World("body_laser_mount_link"); var capCentre = World("body_hinge_pin_cap_l_link");
        Check(Math.Abs(centre.X - capCentre.X) < 1e-6 && Math.Abs(centre.Z - capCentre.Z) < 1e-6 && Math.Abs(centre.Y - length / 2 - (capCentre.Y + .004)) < 1e-6,
            "It runs out along the pin from the cap's outer face");
        double capFace = capCentre.Y + .004, hubFace = .2056;
        Check(Math.Abs(centre.Y + length / 2 - (capFace + .9 * (hubFace - capFace))) < .001, "It stops 10% short of the left shoulder hub's inner face, so the elbow clears the support");
        Check(World("body_laser_head_link").Y + .043 > hubFace + .03, "The laser on its hinge still reaches well over the hub");

        // Toolbox Swap: the toolbox rides the laser's hinge and shows in its place by default.
        Check(scene.HasToolbox && scene.ToolboxShown && scene.LaserVisualsAttached(UrdfScene.ToolboxLink) && !scene.LaserVisualsAttached(UrdfScene.LaserHeadLink)
              && scene.LaserVisualsAttached("body_laser_support_link"), "By default the toolbox shows on the hinge and the laser does not");
        scene.SetToolboxShown(false);
        Check(!scene.LaserVisualsAttached(UrdfScene.ToolboxLink) && scene.LaserVisualsAttached(UrdfScene.LaserHeadLink), "Toolbox Swap = false shows the laser instead");
        scene.SetLaserHidden(true);
        scene.SetToolboxShown(true);
        Check(!scene.LaserVisualsAttached(), "Hide Laser hides the toolbox too, whatever the swap");
        scene.SetLaserHidden(false);
        Check(scene.LaserVisualsAttached(UrdfScene.ToolboxLink) && !scene.LaserVisualsAttached(UrdfScene.LaserHeadLink), "Shown again, the swap still decides");
        scene.SetToolboxShown(false);
        var toolJoint = urdf.Elements("joint").Single(j => (string)j.Element("child").Attribute("link") == UrdfScene.ToolboxLink);
        Check((string)toolJoint.Attribute("type") == "fixed" && (string)toolJoint.Element("parent").Attribute("link") == UrdfScene.LaserHeadLink,
            "The toolbox is fixed to the laser's hinge link, so Laser Lower moves it");
        Check(ServoCommand.IsTextValued(ServoNames.ToolboxSwap) && ServoCommand.IsUrdfOnly(ServoNames.ToolboxSwap) && ServoCommand.DisplayName(ServoNames.ToolboxSwap) == "Toolbox Swap",
            "Toolbox Swap is a URDF-only true/false command");
        Check(ToolboxSwapCommand.Read("").Swap && !ToolboxSwapCommand.Read("{\"swap\":false}").Swap && !ToolboxSwapCommand.Read(new ToolboxSwapCommand { Swap = false }.ToJson()).Swap
              && Throws(() => ToolboxSwapCommand.Read("{\"toolbox\":true}")), "Toolbox Swap settings: a new command shows the toolbox; false the laser; unknown settings are rejected");
        var swaps = new[]
        {
            new ServoCommand { Servo = ServoNames.ToolboxSwap, OffsetSeconds = 1, TextValue = new ToolboxSwapCommand { Swap = false }.ToJson() },
            new ServoCommand { Servo = ServoNames.ToolboxSwap, OffsetSeconds = 3, TextValue = new ToolboxSwapCommand { Swap = true }.ToJson() },
            new ServoCommand { Servo = ServoNames.ToolboxSwap, OffsetSeconds = 4, TextValue = new ToolboxSwapCommand { Swap = false }.ToJson(), Disable = true },
        };
        Check(LaserTimeline.ToolboxAt(Array.Empty<ServoCommand>(), 9) && LaserTimeline.ToolboxAt(swaps, .5) && !LaserTimeline.ToolboxAt(swaps, 2)
              && LaserTimeline.ToolboxAt(swaps, 5), "The latest enabled Toolbox Swap at or before the cursor decides; with none the toolbox shows");

        // Hide Laser.
        Check(scene.HasLaser && !scene.LaserHidden && scene.LaserVisualsAttached(), "The laser is shown by default");
        scene.SetLaserHidden(true);
        Check(scene.LaserHidden && !scene.LaserVisualsAttached(), "Hiding detaches every laser visual from the scene (nothing left to draw)");
        scene.SetJoint("WaistSwivel", .5);
        var turned = World("body_laser_mount_link");
        scene.SetLaserHidden(false);
        Check(scene.LaserVisualsAttached() && (World("body_laser_mount_link") - turned).Length < 1e-12, "Showing it again puts it back in place, following the joints");
        scene.SetJoint("WaistSwivel", 0);

        Check(ServoCommand.IsTextValued(ServoNames.HideLaser) && ServoCommand.IsUrdfOnly(ServoNames.HideLaser) && ServoCommand.DisplayName(ServoNames.HideLaser) == "Hide Laser",
            "Hide Laser is a URDF-only true/false command");
        Check(HideLaserCommand.Read("").Hide && !HideLaserCommand.Read("{\"hide\":false}").Hide && HideLaserCommand.Read(new HideLaserCommand { Hide = false }.ToJson()).Hide == false
              && Throws(() => HideLaserCommand.Read("{\"hidden\":true}")), "Hide Laser settings: a new command hides; false shows; unknown settings are rejected");
        var commands = new[]
        {
            new ServoCommand { Servo = ServoNames.HideLaser, OffsetSeconds = 1, TextValue = new HideLaserCommand { Hide = true }.ToJson() },
            new ServoCommand { Servo = ServoNames.HideLaser, OffsetSeconds = 3, TextValue = new HideLaserCommand { Hide = false }.ToJson() },
            new ServoCommand { Servo = ServoNames.HideLaser, OffsetSeconds = 4, TextValue = new HideLaserCommand { Hide = true }.ToJson(), Disable = true },
        };
        Check(!LaserTimeline.HiddenAt(Array.Empty<ServoCommand>(), 9) && !LaserTimeline.HiddenAt(commands, .5) && LaserTimeline.HiddenAt(commands, 2)
              && !LaserTimeline.HiddenAt(commands, 5), "The latest enabled Hide Laser at or before the cursor decides; with none the laser shows");

        // Laser Lower: 0 up at 70 degrees, 100 lowered onto the shoulder; it moves with the chest.
        var lower = urdf.Elements("joint").Single(j => (string)j.Attribute("name") == "LaserLower");
        Check(Math.Abs(double.Parse((string)lower.Element("limit").Attribute("upper"), System.Globalization.CultureInfo.InvariantCulture) - BodyControlCatalog.LaserLowerMaximum) < 1e-6
              && (string)lower.Element("limit").Attribute("lower") == "0", "The LaserLower limit matches the B_LaserLower control's range");
        var lowerControl = BodyControlCatalog.Find(ServoNames.B_LaserLower);
        var lowerSetting = BodyMotionSetting.Default(lowerControl);
        Check(lowerControl.Joint == "LaserLower" && lowerSetting.Map(0, lowerControl) == 0 && Math.Abs(lowerSetting.Map(100, lowerControl) - BodyControlCatalog.LaserLowerMaximum) < 1e-9,
            "B_LaserLower: 0 is up and 100 is down");
        Point3D Tip() { scene.TryTransformLinkPoint("body_laser_head_link", new Point3D(.2, 0, 0), out var p); return p; }
        var up = Tip(); var hingeAt = World("body_laser_head_link");
        Check(Math.Abs(Math.Atan2(up.Z - hingeAt.Z, up.X - hingeAt.X) * 180 / Math.PI - 70) < .5, "Up, the laser points 70 degrees above the horizontal");
        scene.SetJoint("LaserLower", BodyControlCatalog.LaserLowerMaximum);
        var down = Tip();
        Check(down.Z < up.Z - .05 && down.X > up.X && Math.Abs(down.Z - hingeAt.Z) < 1e-6, "Lowered fully, the laser lies level, its front forward");
        Point3D Shoulder() { scene.TryTransformLinkPoint("body_seg_shoulder_left_link", new Point3D(), out var p); return p; }
        Check(Math.Abs(hingeAt.Z - (Shoulder().Z + .055)) < .001, "Lowered, its underside rests on top of the shoulder hub");
        Point3D Column(double z) { scene.TryTransformLinkPoint("body_laser_support_link", new Point3D(0, 0, z), out var p); return p; }
        Check(Math.Abs(Column(1).X - Column(0).X) < 1e-9 && Math.Abs(Column(1).Y - Column(0).Y) < 1e-9, "The support stands vertical in the built pose");
        scene.SetJoint("LaserLower", 0);
        scene.SetJoint("ChestTilt", .2);
        Check((World("body_laser_head_link") - hingeAt).Length > .01, "The laser group moves with the chest");
        scene.SetJoint("ChestTilt", 0);

        // Fire Laser: bolts from the firing window along the laser, only while the laser itself shows.
        Check(ServoCommand.IsTextValued(ServoNames.FireLaser) && ServoCommand.IsUrdfOnly(ServoNames.FireLaser) && ServoCommand.DisplayName(ServoNames.FireLaser) == "Fire Laser",
            "Fire Laser is a URDF-only settings command");
        var burst = FireLaserCommand.Read("");
        Check(burst.Shots == 3 && Math.Abs(burst.Duration - (.5 + 10 / 15.0)) < 1e-9 && Throws(() => FireLaserCommand.Read("{\"shots\":0}"))
              && Throws(() => FireLaserCommand.Read("{\"color\":\"red\"}")) && Throws(() => FireLaserCommand.Read("{\"power\":9}")),
            "Fire Laser settings: a 3-bolt burst by default; bad values and unknown settings are rejected");
        Check(burst.SoundFile("C:\\cfg").EndsWith("Laser_Fire.wav") && FireLaserCommand.Read("{\"sound\":\"heavy\"}").SoundFile("C:\\cfg").EndsWith("Laser_Heavy.wav")
              && FireLaserCommand.Read("{\"sound\":\"Medium\"}").SoundFile("C:\\cfg").EndsWith("Laser_Medium.wav")
              && FireLaserCommand.Read("{\"sound\":\"none\"}").SoundFile("C:\\cfg") == null, "Fire Laser sounds: the pew by default, the medium or heavy blast, or none");
        var fire = new[]
        {
            new ServoCommand { Servo = ServoNames.FireLaser, OffsetSeconds = 1, TextValue = "" },
            new ServoCommand { Servo = ServoNames.FireLaser, OffsetSeconds = 5, TextValue = "", Disable = true },
        };
        Check(FireLaserTimeline.ShotsAt(fire, .9).Count == 0 && FireLaserTimeline.ShotsAt(fire, 1.1).Count == 1 && FireLaserTimeline.ShotsAt(fire, 1.6).Count == 3
              && FireLaserTimeline.ShotsAt(fire, 1.7).Count == 2 && FireLaserTimeline.ShotsAt(fire, 2.2).Count == 0 && FireLaserTimeline.ShotsAt(fire, 5.1).Count == 0,
            "Bolts leave one interval apart and are gone after their range; disabled commands don't fire");
        var flying = FireLaserTimeline.ShotsAt(fire, 1.6).OrderBy(s => s.Start).ToArray();
        Check(Math.Abs(flying[0].Front - 9) < 1e-9 && Math.Abs(flying[0].Tail - 8.6) < 1e-9 && Math.Abs(flying[2].Front - 1.5) < 1e-9
              && FireLaserTimeline.LatestShot(fire, 1.6)?.Index == 2, "Each bolt's front travels at its speed, its tail one bolt length behind");
        scene.SetToolboxShown(true);
        Check(!scene.LaserReadyToFire, "With the toolbox out (the default) the laser can't fire");
        scene.SetToolboxShown(false);
        Check(scene.LaserReadyToFire && scene.TryLaserMuzzle(out var muzzle, out var aim) && Math.Abs(Math.Atan2(aim.Z, Math.Sqrt(aim.X * aim.X + aim.Y * aim.Y)) * 180 / Math.PI - 70) < .5
              && (muzzle - World("body_laser_head_link")).Length > .3, "With the laser out it fires from its tip, along the laser (70 degrees up when raised)");
        scene.SetJoint("LaserLower", BodyControlCatalog.LaserLowerMaximum);
        Check(scene.TryLaserMuzzle(out _, out var level) && Math.Abs(level.Z) < 1e-6 && level.X > .99, "Lowered, it fires level and straight ahead");
        scene.SetJoint("LaserLower", 0);
        scene.SetLaserHidden(true);
        Check(!scene.LaserReadyToFire, "A hidden laser can't fire");
        scene.SetLaserHidden(false); scene.SetToolboxShown(true);

        // Editor controls: 0..100 sliders (below 0 did nothing), controller actions, API requests.
        Check(ServoCommand.RangeFor(ServoNames.B_LaserLower) == (0, 100) && ServoCommand.RangeFor(ServoNames.B_PanelsOpen) == (0, 100),
            "B_LaserLower and B_PanelsOpen run 0 to 100");
        Check(ControllerCatalog.Actions.Contains(ControllerCatalog.LaserToggleAction) && ControllerCatalog.Actions.Contains(ControllerCatalog.ToolboxToggleAction),
            "Controller actions toggle the laser and swap toolbox/laser");
        var lcdRequest = System.Text.Json.JsonSerializer.Deserialize<EditorApiRequest>("{\"method\":\"insert_lcd_show\",\"text\":\"Hi\",\"image\":\"a.png\",\"fit\":\"cover\",\"color\":\"#FF0000\"}", EditorApiServer.Json);
        var hideRequest = System.Text.Json.JsonSerializer.Deserialize<EditorApiRequest>("{\"method\":\"insert_hide_laser\",\"hide\":false}", EditorApiServer.Json);
        var swapRequest = System.Text.Json.JsonSerializer.Deserialize<EditorApiRequest>("{\"method\":\"insert_toolbox_swap\",\"swap\":false}", EditorApiServer.Json);
        Check(lcdRequest.Text == "Hi" && lcdRequest.Image == "a.png" && lcdRequest.Color == "#FF0000" && hideRequest.Hide == false && swapRequest.Swap == false,
            "insert_lcd_show, insert_hide_laser and insert_toolbox_swap request fields deserialize");

        var app = new App(); app.InitializeComponent(); DisableTestStartup(app);
        Check(ServoIconProvider.For(ServoNames.HideLaser) != null && ServoIconProvider.For(ServoNames.B_LaserLower) != null && ServoIconProvider.For(ServoNames.ToolboxSwap) != null,
            "Hide Laser, B_LaserLower and Toolbox Swap have their icons");
        Check(ControllerCatalog.ActionIcon("Action:" + ControllerCatalog.LaserToggleAction) != null && ControllerCatalog.ActionIcon("Action:" + ControllerCatalog.ToolboxToggleAction) != null
              && ControllerCatalog.ActionIcon("Action:URDF Beam In") != null && ControllerCatalog.ActionIcon("Action:Stop") == null, "The laser controller actions show their command icons");
        var view = new RobotHeadView();
        var viewScene = UrdfScene.Load(body); view.InstallModel(body, viewScene, new UrdfFraming());
        view.SetLaserHidden(true);
        Check(view.HasLaser && view.LaserHidden && !viewScene.LaserVisualsAttached(), "The URDF view hides the laser");
        view.SetLaserHidden(false);
        Check(!view.LaserHidden && viewScene.LaserVisualsAttached(), "and shows it again");
        view.SetToolboxShown(false);
        Check(!view.ToolboxShown && viewScene.LaserVisualsAttached(UrdfScene.LaserHeadLink) && !viewScene.LaserVisualsAttached(UrdfScene.ToolboxLink), "The URDF view swaps the toolbox for the laser");
        view.SetLaserShots(FireLaserTimeline.ShotsAt(fire, 1.05));
        int justFired = view.LaserShotModels;
        view.SetLaserShots(FireLaserTimeline.ShotsAt(fire, 1.6));
        Check(justFired == 3 && view.LaserShotModels == 6, "The view draws each bolt (core and glow) and a muzzle flash as a bolt leaves");
        view.SetToolboxShown(true);
        view.SetLaserShots(FireLaserTimeline.ShotsAt(fire, 1.6));
        Check(view.LaserShotModels == 0, "With the toolbox out, nothing fires");
        view.SetLaserShots(Array.Empty<LaserShot>());
        Check(view.LaserShotModels == 0, "Bolts clear when none are in flight");
        Check(ServoIconProvider.For(ServoNames.FireLaser) != null && ControllerCatalog.Actions.Contains(ControllerCatalog.FireLaserAction)
              && ControllerCatalog.ActionIcon("Action:" + ControllerCatalog.FireLaserAction) != null, "Fire Laser has its icon and a controller action");
        var fireRequest = System.Text.Json.JsonSerializer.Deserialize<EditorApiRequest>("{\"method\":\"insert_fire_laser\",\"shots\":5,\"interval\":0.1,\"speed\":20,\"range\":6,\"length\":0.3,\"color\":\"#00FF00\",\"sound\":\"none\",\"volume\":50}", EditorApiServer.Json);
        Check(fireRequest.Shots == 5 && fireRequest.Interval == .1 && fireRequest.Speed == 20 && fireRequest.Range == 6 && fireRequest.Length == .3,
            "insert_fire_laser request fields deserialize");
        app.Shutdown();
    }
}
