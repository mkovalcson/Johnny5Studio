using System.Diagnostics;
using System.IO;
using ServoAnimator;

/// <summary>Body collision warnings (arms against the head, each other, the torso, the waist and the tracks) on the real full-body model.
/// Runs headless: the URDF view is created in memory and never shown.</summary>
internal static partial class Program
{
    private static void BodyCollisionChecks()
    {
        var app = new App(); app.InitializeComponent(); DisableTestStartup(app);
        var view = new RobotHeadView();
        view.SetServoConfiguration(ServoConfiguration.CreateDefault());
        view.SetSpeedCalibration(new() { UseInUrdf = false });
        view.SetUrdfDriveEnabled(true);
        string body = Path.GetFullPath("Config/Models/Robots/Johnny5FullBody/johnny5_head.urdf");
        view.InstallModel(body, UrdfScene.Load(body), null);
        // The editor builds the collision allow-list once the view is on screen; do the same here.
        typeof(RobotHeadView).GetMethod("RebuildCollisionBaseline", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Invoke(view, null);
        view.SetCollisionWarningsEnabled(true);
        var scene = view.CurrentModelScene;
        scene?.ResetBodyClearance();
        var build = Stopwatch.StartNew();
        Check(scene != null && scene.BodyClearanceAvailable, "The full-body model provides body collision geometry");
        Console.WriteLine($"Body collision samples built in {build.ElapsedMilliseconds} ms");
        var groupNames = scene.BodyClearanceGroups().Select(g => g.Group).ToArray();
        Check(new[] { "head", "left arm", "right arm", "torso", "waist", "tracks" }.All(groupNames.Contains),
              "Arms are checked against the head, each other, the torso, the waist and the tracks: " + string.Join(", ", groupNames));

        var all = new[] { "B_ShoulderRotate", "B_Elbow", "B_ArmTwist", "B_ShoulderHug", "B_WristPitch", "B_WristRoll",
                          "B_FingerSpreadUpper", "B_FingerSpreadLower", "B_FingerCurlUpper", "B_FingerCurlLower" };
        string[] Body(Dictionary<string, double> pose)
        {
            foreach (string c in all) foreach (string s in new[] { "Left", "Right" })
                view.SetServo(Enum.Parse<ServoNames>(c + s), pose.TryGetValue(c, out double v) ? v : 0);
            foreach (string c in new[] { "B_NeckLift", "NeckNodUp", "NeckTurn", "B_WaistSwivel", "B_WaistLean", "B_ChestTilt", "B_TipToe" })
                view.SetServo(Enum.Parse<ServoNames>(c), pose.TryGetValue(c, out double v) ? v : 0);
            view.RefreshCollisionNow();
            return view.CollisionPairKeys.Where(k => k.StartsWith(UrdfScene.BodyPairPrefix, StringComparison.Ordinal)).ToArray();
        }
        bool ArmArm(string[] keys) => keys.Contains(UrdfScene.BodyPairPrefix + "left arm|right arm");
        bool ArmHead(string[] keys) => keys.Any(k => k.EndsWith("arm|head", StringComparison.Ordinal));
        Dictionary<string, double> Pose(double rot, double elb, double tw, double hug, double wp, double neck = 0, double lift = 100, double nod = 15) => new()
        {
            ["B_ShoulderRotate"] = rot, ["B_Elbow"] = elb, ["B_ArmTwist"] = tw, ["B_ShoulderHug"] = hug, ["B_WristPitch"] = wp,
            ["B_FingerSpreadUpper"] = -20, ["B_FingerSpreadLower"] = -20, ["B_FingerCurlUpper"] = -60, ["B_FingerCurlLower"] = -60,
            ["B_NeckLift"] = lift, ["NeckNodUp"] = nod, ["NeckTurn"] = neck,
        };

        string Parts() => string.Join(", ", scene.DetectBodyHits().Select(h => h.LinkA + "~" + h.LinkB));
        foreach (var g in scene.BodyClearanceGroups()) Console.WriteLine($"Body group {g.Group}: {g.Links} links, {g.Samples} samples");
        Check(Body(new()).Length == 0, "Neutral pose has no body contacts: " + Parts());
        // The Sugar Plum Fairy dance's first overhead pose (fingers crossed and inside the head; seen at 3.5 s and 22.9 s).
        var old = Body(Pose(80, 20, -100, 100, 20));
        Check(ArmArm(old), "Overlapping fingers over the head are reported: " + string.Join(", ", old));
        Check(ArmHead(old), "Hands inside the head are reported: " + string.Join(", ", old));
        Check(view.HasCollision, "Body contacts raise the editor's collision state");

        // The fingertip caps are URDF cylinders; with the earlier intro pose (hug 90, twist -100) they meet.
        Check(ArmArm(Body(Pose(96, 10, -100, 90, -45))), "Fingertip caps touching over the head are reported");
        // Replacement poses (about 2.9 cm between fingertips, 2.7 cm from the head; 3 cm or more for the spins).
        var intro = Body(Pose(96, 10, -90, 80, -45));
        Check(intro.Length == 0, "Overhead pose with the fingertips close but apart is clear: " + Parts());
        foreach (double neck in new[] { -36.0, 0, 36.0 })
        {
            var spin = Body(Pose(96, 14, -75, 85, -45, neck));
            Check(spin.Length == 0, $"Pirouette arms are clear with the neck at {neck}: " + Parts());
        }
        // Raised arms and a 55% neck turn sweep the eyebrow flaps into the forearms.
        var turned = Body(Pose(96, 14, -75, 85, -45, -60));
        Check(ArmHead(turned), "A forearm against the turned head's flap is reported: " + string.Join(", ", turned));

        // Torso, waist and tracks.
        bool Hits(string[] keys, string arm, string group) => keys.Contains(UrdfScene.BodyPairPrefix + arm + "|" + group);
        var back = Body(Pose(-50, 0, 0, 0, 0, 0, 0, 0));
        Check(Hits(back, "left arm", "torso"), "The left upper arm swung back into the shoulder laser is reported: " + Parts());
        Check(Body(Pose(-20, 0, 0, 0, 0, 0, 0, 0)).Length == 0, "A small backward swing clears the laser (15 mm): " + Parts());
        var waistPose = Pose(-63, 18, -27, 66, 19, 0, 0, 0);
        waistPose["B_WristRoll"] = -90; waistPose["B_FingerSpreadUpper"] = -59; waistPose["B_FingerSpreadLower"] = -43;
        waistPose["B_WaistLean"] = -2; waistPose["B_ChestTilt"] = 5; waistPose["B_WaistSwivel"] = -42; waistPose["B_TipToe"] = 99;
        Check(Hits(Body(waistPose), "left arm", "waist"), "Fingertips against the waist column are reported: " + Parts());
        var trackPose = Pose(-44, 3, 11, 62, -36, 0, 0, 0);
        trackPose["B_WristRoll"] = -34; trackPose["B_FingerSpreadUpper"] = -35; trackPose["B_FingerSpreadLower"] = 62;
        trackPose["B_WaistLean"] = -43; trackPose["B_ChestTilt"] = 4; trackPose["B_WaistSwivel"] = 93; trackPose["B_TipToe"] = 96;
        var tracks = Body(trackPose);
        Check(Hits(tracks, "left arm", "tracks"), "Fingertips against the caster are reported: " + Parts());
        Check(view.HasCollision, "A tracks contact raises the editor's collision state");
        // The dance finale: bent to the floor with the arms straight down above the tracks.
        var floor = Pose(22, 0, 0, -60, 0, 0, 100, 40);
        floor["B_WaistLean"] = 100; floor["B_ChestTilt"] = 100; floor["B_FingerSpreadUpper"] = 100; floor["B_FingerSpreadLower"] = 100;
        Check(Body(floor).Length == 0, "The Sugar Plum Fairy finale pose is clear of the tracks and waist: " + Parts());

        Check(Body(new()).Length == 0 && !view.HasCollision, "Warnings clear when the arms return to neutral");

        // Cost of one body check in a contact pose (it runs on every throttled collision refresh).
        Body(Pose(80, 20, -100, 100, 20));
        var watch = Stopwatch.StartNew();
        for (int i = 0; i < 40; i++) scene.DetectBodyHits();
        double ms = watch.Elapsed.TotalMilliseconds / 40;
        Console.WriteLine($"Body collision check: {ms:F2} ms per pose");
        Check(ms < 25, $"Body collision check stays fast ({ms:F2} ms per pose)");
        Body(waistPose);
        watch.Restart();
        for (int i = 0; i < 40; i++) scene.DetectBodyHits();
        ms = watch.Elapsed.TotalMilliseconds / 40;
        Console.WriteLine($"Body collision check against the waist: {ms:F2} ms per pose");
        Check(ms < 25, $"Body collision check against the waist stays fast ({ms:F2} ms per pose)");
    }
}
