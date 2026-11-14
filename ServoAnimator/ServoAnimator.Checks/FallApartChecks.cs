using System.IO;
using System.Text.Json;
using System.Windows.Media.Media3D;
using ServoAnimator;

internal static partial class Program
{
    private static void FallApartChecks()
    {
        // Command settings
        var defaults = FallApartCommand.Read("");
        Check(!defaults.Reverse && defaults.Speed == 1, "Empty Fall Apart text means forwards at normal speed");
        var reverse = FallApartCommand.Read("{\"reverse\":true,\"speed\":2}");
        Check(reverse.Reverse && reverse.Speed == 2 && FallApartCommand.Read(reverse.ToJson()).Reverse, "Fall Apart settings round-trip");
        Check(Throws(() => FallApartCommand.Read("{\"speed\":0}")), "Zero speed is rejected");
        Check(defaults.ToJson() == "{\"reverse\":false,\"speed\":1}", "Saved text holds only reverse and speed");
        Check(FallApartCommand.Read("{\"reverse\":true,\"speed\":1,\"describe\":\"Reassemble\"}").Reverse, "Text saved by 1.37.1 (with describe) still reads");
        Check(Throws(() => FallApartCommand.Read("{\"speed\":1,\"colour\":1}")), "Unknown settings are rejected");
        Check(ServoCommand.IsTextValued(ServoNames.FallApart) && ServoCommand.IsUrdfOnly(ServoNames.FallApart), "Fall Apart is a URDF-only text command (never hardware or exports)");
        Check(ServoCommand.DisplayName(ServoNames.FallApart) == "Disassemble", "Fall Apart shows as Disassemble");

        // Timeline evaluation: 30 fps, 105 frames (3.467 s)
        ServoCommand Cmd(double at, string text, bool disable = false) => new() { Servo = ServoNames.FallApart, OffsetSeconds = at, TextValue = text, Disable = disable };
        double? At(double cursor, params ServoCommand[] commands) => FallApartTimeline.FrameAt(commands, cursor, 30, 105);
        var forward = Cmd(10, "{}");
        Check(At(9.99, forward) == null, "Before the command the robot is whole");
        Check(At(10, forward) == 0, "Forwards starts at frame 0");
        Check(Math.Abs(At(11, forward).Value - 30) < 1e-9, "One second plays 30 frames");
        Check(At(60, forward) == 104, "Forwards holds the final frame");
        var back = Cmd(20, "{\"reverse\":true}");
        Check(At(20, forward, back) == 104, "The latest command wins; reverse starts in pieces");
        Check(Math.Abs(At(21, forward, back).Value - 74) < 1e-9, "Reverse counts frames down");
        Check(At(20 + 104 / 30.0 + .01, forward, back) == null, "Reassembly releases the override");
        Check(Math.Abs(At(11, Cmd(10, "{\"speed\":2}")).Value - 60) < 1e-9, "Speed scales playback");
        Check(At(11, Cmd(10, "{}", disable: true)) == null, "Disabled commands are ignored");
        Check(At(11, Cmd(10, "not json")) == null, "Invalid settings are ignored during playback");
        Check(FallApartTimeline.BlendWeight(0, 15) == 0 && FallApartTimeline.BlendWeight(15, 15) == 1 &&
              Math.Abs(FallApartTimeline.BlendWeight(7.5, 15) - .5) < 1e-12, "Blend eases in over the shudder");

        // Rigid matrix helpers
        var rng = new Random(5);
        for (int i = 0; i < 50; i++)
        {
            var q = new Quaternion(rng.NextDouble() - .5, rng.NextDouble() - .5, rng.NextDouble() - .5, rng.NextDouble() - .5); q.Normalize();
            var r = FallApartMath.Rotation(FallApartMath.Compose(new Vector3D(1, 2, 3), q));
            Check(Math.Abs(Math.Abs(q.X * r.X + q.Y * r.Y + q.Z * r.Z + q.W * r.W) - 1) < 1e-9, "Matrix to quaternion round-trips");
        }

        // API request accepts the new fields
        var request = JsonSerializer.Deserialize<EditorApiRequest>("{\"method\":\"insert_fall_apart\",\"reverse\":true,\"speed\":1.5}", EditorApiServer.Json);
        Check(request.Reverse == true && request.Speed == 1.5, "insert_fall_apart request fields deserialize");

        // Baked clip against the real model
        string modelPath = Path.GetFullPath("ServoAnimator/FullBodyModel/johnny5_head.urdf");
        Check(FallApartClip.Load(Path.GetTempPath()) == null, "A model without a clip has no effect and no exception");
        var clip = FallApartClip.Load(Path.GetDirectoryName(modelPath));
        Check(clip != null && clip.Fps == 30 && clip.FrameCount == 105 && clip.ShudderFrames == 15, "Clip timing loads");
        Check(clip.Links.Count > 650, "Clip covers every visual link");
        var scene = UrdfScene.Load(modelPath, optimizeExterior: false);
        Point3D World(string link) { scene.TryTransformLinkPoint(link, new Point3D(), out var p); return p; }
        Point3D Clip(string link, double frame) { var m = clip.Sample(link, frame); return new Point3D(m.OffsetX, m.OffsetY, m.OffsetZ); }
        var links = clip.Links.Where(l => l != "body_root_link").ToArray();
        var rest = links.ToDictionary(l => l, World);
        Check(links.All(l => (rest[l] - Clip(l, 0)).Length < 1e-5), "Frame 0 is the model's zero pose");

        scene.SetFallApartFrame(104);
        Check(scene.FallApartActive, "Override activates");
        double worst = links.Max(l => (World(l) - Clip(l, 104)).Length);
        Check(worst < 1e-6, $"Every link shows its baked final pose (worst {worst:E1} m)");
        Check(links.Any(l => (World(l) - rest[l]).Length > .2), "Parts have actually moved");
        // Link origins can sit far from their meshes, so test the visual geometry: every mesh centre rests on or above the floor.
        double lowest = links.Min(l => { var p = scene.GetWorldVisualBoundsPoints(new[] { l }); return p.Count == 0 ? 0 : p.Average(q => q.Z); });
        Check(lowest > -1.0774 - .01, $"Nothing falls through the floor (lowest mesh centre z={lowest:F4} m)");

        scene.SetJoint("ElbowLeft", .6);                       // timeline keeps posing while in pieces
        Check((World("body_forearm_left_link") - Clip("body_forearm_left_link", 104)).Length < 1e-6, "Joint motion is ignored while in pieces");
        scene.SetFallApartFrame(0);
        var bent = World("body_forearm_left_link");
        scene.SetFallApartFrame(null);
        Check(!scene.FallApartActive && (World("body_forearm_left_link") - bent).Length < 1e-6, "Frame 0 blends fully to the live pose, so release is seamless");
        Check((World("body_forearm_left_link") - rest["body_forearm_left_link"]).Length > .01, "Released model follows the live joints again");
        scene.SetJoint("ElbowLeft", 0);
        Check(links.All(l => (World(l) - rest[l]).Length < 1e-9), "Release restores the original transforms exactly");
    }

    private static bool Throws(Action action)
    {
        try { action(); return false; } catch { return true; }
    }
}
