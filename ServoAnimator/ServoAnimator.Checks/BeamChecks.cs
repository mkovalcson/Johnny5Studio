using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using ServoAnimator;

internal static partial class Program
{
    private static void BeamChecks()
    {
        // Settings
        var d = BeamCommand.Read("");
        Check(d.Direction == BeamDirection.In && d.Seconds == 3.5 && d.Particles && d.Sound == "default" && d.Volume == 100 && d.Rgb, "Beam defaults: in, 3.5 s, particles, synthesized sound, RGB");
        var o = BeamCommand.Read("{\"direction\":\"Out\",\"seconds\":2,\"particles\":false,\"sound\":\"none\",\"volume\":40,\"rgb\":false}");
        Check(o.Direction == BeamDirection.Out && !o.Particles && o.Sound == "none" && o.Volume == 40 && !o.Rgb && BeamCommand.Read(o.ToJson()).Direction == BeamDirection.Out, "Beam settings parse and round-trip");
        Check(Throws(() => BeamCommand.Read("{\"seconds\":0.1}")) && Throws(() => BeamCommand.Read("{\"volume\":120}")) && Throws(() => BeamCommand.Read("{\"color\":1}")), "Beam settings are validated");
        Check(d.RgbText() == "Beam,140,200,255,200,Both,LR,3500,In" && o.RgbText() == null, "RGB sparkle text, or none when RGB is off");
        Check(d.SoundFile(@"C:\Cfg").EndsWith(@"Library\Audio\Beam_In.wav") && o.SoundFile(@"C:\Cfg") == null &&
              BeamCommand.Read("{\"direction\":\"Out\"}").SoundFile(@"C:\Cfg").EndsWith("Beam_Out.wav"), "Default sounds per direction; none plays nothing");
        Check(ServoCommand.IsTextValued(ServoNames.UrdfBeam) && ServoCommand.IsUrdfOnly(ServoNames.UrdfBeam) && ServoCommand.DisplayName(ServoNames.UrdfBeam) == "URDF Beam", "URDF Beam is a URDF-only text command");

        // Visibility rules and phases
        ServoCommand B(double t, string text = "", bool disable = false) => new() { Servo = ServoNames.UrdfBeam, OffsetSeconds = t, TextValue = text, Disable = disable };
        BeamState At(double t, params ServoCommand[] cs) => BeamTimeline.StateAt(cs, t);
        Check(!At(5).Hidden && !At(5).Active && At(5).RobotOpacity == 1, "No beam commands: robot visible");
        var beamIn = B(5); var beamOut = B(12, "{\"direction\":\"Out\"}");
        Check(At(3, beamIn, beamOut).Hidden, "Before the first beam-in the robot is invisible");
        Check(At(3, B(5, "{\"direction\":\"Out\"}")).RobotOpacity == 1, "Before a first beam-out the robot is visible");
        var start = At(5, beamIn, beamOut); var mid = At(6.75, beamIn, beamOut);
        Check(start.Active && start.RobotOpacity == 0 && Math.Abs(mid.Progress - .5) < 1e-9 && mid.RobotOpacity > 0 && mid.RobotOpacity < 1, "Beam in materializes over its duration");
        Check(!At(9, beamIn, beamOut).Active && !At(9, beamIn, beamOut).Hidden, "After beam-in the robot is visible");
        Check(At(12.1, beamIn, beamOut).Active && At(12.1, beamIn, beamOut).RobotOpacity > .9 && At(15.4, beamIn, beamOut).RobotOpacity < .05, "Beam out dematerializes");
        Check(At(16, beamIn, beamOut).Hidden && At(30, beamIn, beamOut).Hidden, "After beam-out the robot stays invisible");
        Check(At(21, beamIn, beamOut, B(20)).RobotOpacity < 1 && !At(25, beamIn, beamOut, B(20)).Hidden, "A later beam-in brings it back");
        Check(!At(3, B(5, "", disable: true)).Hidden, "Disabled beams are ignored");
        Check(Math.Abs(mid.Glow) > .5 && At(5.05, beamIn).Sparkle < .2 && mid.Sparkle > .9, "Glow and sparkle peak mid-beam");
        Check(At(5.5, beamIn).ParticleTravel is > 0 and < 1 && At(7.5, beamIn).ParticleTravel < 0, "Beam-in particles fall during the first part");
        Check(At(12.5, beamOut).ParticleTravel < 0 && At(15, beamOut).ParticleTravel is > 0 and < 1, "Beam-out particles rise during the last part");
        Check(At(5.5, B(5, "{\"particles\":false}")).ParticleTravel < 0, "Particles can be switched off");
        Check(BeamTimeline.Sparkles(50, 1.23).SequenceEqual(BeamTimeline.Sparkles(50, 1.23)) && !BeamTimeline.Sparkles(50, 1.23).SequenceEqual(BeamTimeline.Sparkles(50, 1.5)), "Sparkles are deterministic in time");
        var column = new Rect(300, 200, 200, 400); var view = new Size(800, 700);
        var falling = BeamTimeline.Stream(90, .6, BeamDirection.In, column, view).ToArray();
        Check(falling.Length > 20 && falling.All(p => p.Position.Y >= -13 && p.Position.Y <= column.Bottom + 1), "Falling particles travel between the top of the view and the column");

        // RGB ring sparkle: the simulator must reproduce the sketch exactly. Independent re-implementation of
        // BeamSparkleNonBlocking's frame for comparison (before the ring brightness scaling).
        Check(RgbCommandWireFormat.ToArduinoWireOrder("Beam,140,200,255,200,Both,LR,1000,In") == "Beam,200,140,255,200,Both,LR,1000,In", "BEAM colors are reordered for the Arduino wire");
        Color[] Expected(int durationMs, bool beamIn, long atMs, int brightness)
        {
            uint rng = 0x2545F491u; Color[] last = null;
            for (long now = 0; now <= atMs; now += 30)
            {
                if (now >= durationMs) return Enumerable.Repeat(Colors.Black, 64).ToArray();
                int p = (int)(now * 256 / durationMs), rise = beamIn ? 179 : 77;
                int env = p < rise ? p * 255 / rise : (255 - p) * 255 / (256 - rise);
                var frame = new Color[64];
                for (int i = 0; i < 64; i++)
                {
                    rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
                    int lvl = (rng & 0xFF) < env ? (96 + (int)((rng >> 8) % 160)) * env / 255 : 0;
                    int r = 140 * lvl / 255, g = 200 * lvl / 255, b = 255 * lvl / 255;
                    byte S(int v) => (byte)Math.Round(Math.Clamp(v * (brightness / 255.0), 0, 255));
                    frame[i] = Color.FromRgb(S(r), S(g), S(b));
                }
                last = frame;
            }
            return last;
        }
        Color[] Rings(RgbRingFrame f) => f.LeftEye.Concat(f.RightEye).Concat(f.LeftVent).Concat(f.RightVent).ToArray();
        var rgbCommand = new ServoCommand { Servo = ServoNames.RGBCommand, OffsetSeconds = 0, TextValue = "Beam,140,200,255,200,Both,LR,1000,In" };
        foreach (long ms in new long[] { 0, 30, 300, 600, 990 })
        {
            var sim = new ArduinoRgbTimelineSimulator();
            Check(Rings(sim.Evaluate(new[] { rgbCommand }, ms / 1000.0)).SequenceEqual(Expected(1000, true, ms, 200)), $"Ring preview matches the sketch's BEAM sparkle at {ms} ms");
        }
        var done = Rings(new ArduinoRgbTimelineSimulator().Evaluate(new[] { rgbCommand }, 1.05));
        Check(done.All(c => c == Colors.Black), "The BEAM sparkle clears its rings when it ends");
        var beamWithRgb = B(0, "{\"seconds\":1}");
        Check(Rings(new ArduinoRgbTimelineSimulator().Evaluate(new[] { beamWithRgb }, .3)).SequenceEqual(Expected(1000, true, 300, 200)), "A URDF Beam with RGB drives the ring preview");
        Check(Rings(new ArduinoRgbTimelineSimulator().Evaluate(new[] { B(0, "{\"seconds\":1,\"rgb\":false}") }, .3)).All(c => c == Colors.Black), "RGB off leaves the rings alone");
        Check(MainWindow.BeamRgbCommand(beamWithRgb)?.Servo == ServoNames.RGBCommand && MainWindow.BeamRgbCommand(B(0, "{\"rgb\":false}")) == null, "Hardware playback and exports get the ring sparkle as an RGB command");
        // Repo keeps the configuration in Config; a deployed tree keeps it in Config.
        var sketch = File.ReadAllText(Path.GetFullPath("Config/ArduinoOpenSaintRGB/ArduinoOpenSaintRGB.ino"));
        Check(sketch.Contains("command == \"BEAM\"") && sketch.Contains("0x2545F491UL") && sketch.Contains("BEAM_FRAME_MS = 30"), "The Arduino sketch has the BEAM effect with the same seed and frame time");

        // View: hidden, shimmering, then restored exactly
        var request = JsonSerializer.Deserialize<EditorApiRequest>("{\"method\":\"insert_beam\",\"direction\":\"out\",\"particles\":false,\"sound\":\"none\",\"rgb\":false}", EditorApiServer.Json);
        Check(request.Direction == "out" && request.Particles == false && request.Sound == "none" && request.Rgb == false, "insert_beam request fields deserialize");
        var app = new App(); app.InitializeComponent(); DisableTestStartup(app);
        var robot = new RobotHeadView();
        string body = Path.GetFullPath("ServoAnimator/FullBodyModel/johnny5_head.urdf");
        var scene = UrdfScene.Load(body); robot.InstallModel(body, scene, new UrdfFraming());
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var presentation = (Model3DGroup)typeof(RobotHeadView).GetField("_presentationModel", flags).GetValue(robot);
        var root = (Model3DGroup)typeof(RobotHeadView).GetField("_sceneLightingRoot", flags).GetValue(robot);
        var visuals = ((System.Collections.IDictionary)typeof(UrdfScene).GetField("_linkVisuals", flags).GetValue(scene)).Values.Cast<IEnumerable<GeometryModel3D>>().SelectMany(v => v).ToArray();
        var originals = visuals.Select(v => v.Material).ToArray();
        robot.SetTimelineBeam(At(3, beamIn), 3);
        Check(!root.Children.Contains(presentation), "Before beam-in the view hides the robot");
        robot.SetTimelineBeam(mid, 6.75);
        Check(root.Children.Contains(presentation) && scene.BeamActive && visuals.All(v => !ReferenceEquals(v.Material, originals[Array.IndexOf(visuals, v)])), "Mid-beam the robot shows the shimmer material");
        robot.SetTimelineBeam(BeamState.Visible, 9);
        Check(!scene.BeamActive && root.Children.Contains(presentation) && visuals.Select(v => v.Material).SequenceEqual(originals), "After beam-in every original material is restored exactly");
        robot.SetTimelineBeam(At(16, beamIn, beamOut), 16);
        Check(!root.Children.Contains(presentation), "After beam-out the view hides the robot");
        app.Shutdown();
    }
}
