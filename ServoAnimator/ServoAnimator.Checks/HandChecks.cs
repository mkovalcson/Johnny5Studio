using System.IO;
using System.Windows.Media.Media3D;
using ServoAnimator;

internal static partial class Program
{
    private static void HandChecks()
    {
        string body = Path.GetFullPath("ServoAnimator/FullBodyModel/johnny5_head.urdf");
        var urdf = System.Xml.Linq.XDocument.Load(body).Root;
        System.Xml.Linq.XElement Joint(string name) => urdf.Elements("joint").Single(j => (string)j.Attribute("name") == name);
        var scene = UrdfScene.Load(body);
        Point3D World(string link) { scene.TryTransformLinkPoint(link, new Point3D(), out var p); return p; }
        Point3D Pivot(string joint) => World((string)Joint(joint).Element("child").Attribute("link"));
        var jointTable = (System.Collections.IDictionary)typeof(UrdfScene).GetField("_joints", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(scene);
        double Position(string joint) { var j = jointTable[joint]; return (double)j.GetType().GetProperty("Position").GetValue(j); }

        foreach (var (side, S) in new[] { ("left", "Left"), ("right", "Right") })
        {
            // The thumb is hard-mounted on the palm, centred between the two paired fingers.
            Check((string)Joint($"ThumbCurl{S}").Element("parent").Attribute("link") == $"body_seg_hand_{side}_link",
                $"{S} thumb hangs from the palm, not a spread axle");
            var f0 = World($"body_finger_base_axle_{side}_0_link"); var f1 = World($"body_finger_base_axle_{side}_1_link");
            var across = f1 - f0; across.Normalize(); var mid = new Point3D((f0.X + f1.X) / 2, (f0.Y + f1.Y) / 2, (f0.Z + f1.Z) / 2);
            double Lane(Point3D p) => Vector3D.DotProduct(p - mid, across);
            Check(Math.Abs(Lane(Pivot($"ThumbCurl{S}"))) < 1e-4, $"{S} thumb lies on the midline between the fingers");
            Check((string)Joint($"body_palm_bar_{side}_thumb_fixed").Element("parent").Attribute("link") == $"body_seg_hand_{side}_link",
                $"{S} thumb bar is fixed to the palm");

            // Segment lengths from the closed-hand CAD drawing.
            double Len(string a, string b) => (Pivot(b) - Pivot(a)).Length;
            foreach (var UL in new[] { "Upper", "Lower" })
                Check(Math.Abs(Len($"FingerCurl{UL}{S}", $"FingerTipCurl{UL}{S}") - .050) < 1e-4
                      && Math.Abs(Len($"FingerTipCurl{UL}{S}", $"FingerEndCurl{UL}{S}") - .038) < 1e-4,
                    $"{S} {UL.ToLower()} finger: 50 mm base to knuckle, 38 mm knuckle to bend");
            Check(Math.Abs(Len($"ThumbCurl{S}", $"ThumbTipCurl{S}") - .054) < 1e-4, $"{S} thumb: 54 mm base to bend");

            // URDF closing limits match the editor's.
            double Upper(string j) => double.Parse((string)Joint(j).Element("limit").Attribute("upper"), System.Globalization.CultureInfo.InvariantCulture);
            Check(Math.Abs(Upper($"FingerCurlUpper{S}") - HandClosingLimits.BaseMaximum) < 1e-9 && Math.Abs(Upper($"FingerTipCurlLower{S}") - HandClosingLimits.TipMaximum) < 1e-9
                  && Math.Abs(Upper($"FingerEndCurlUpper{S}") - HandClosingLimits.EndMaximum) < 1e-9 && Math.Abs(Upper($"ThumbCurl{S}") - HandClosingLimits.ThumbMaximum) < 1e-9
                  && Math.Abs(Upper($"ThumbTipCurl{S}") - HandClosingLimits.ThumbTipMaximum) < 1e-9, $"{S} URDF closing limits match the editor");

            // Spreading finger 0 no longer swings the thumb.
            var thumb = Pivot($"ThumbCurl{S}"); var finger = Pivot($"FingerTipCurlUpper{S}");
            scene.SetJoint($"FingerSpreadUpper{S}", .5);
            Check((Pivot($"ThumbCurl{S}") - thumb).Length < 1e-9 && (Pivot($"FingerTipCurlUpper{S}") - finger).Length > .005,
                $"{S} FingerSpreadUpper moves finger 0 but not the thumb");
            scene.SetJoint($"FingerSpreadUpper{S}", 0);

            // The CAD closed pose is reachable, and the digits stay in their own lanes.
            var closed = new Dictionary<string, double> { [$"FingerCurlUpper{S}"] = 0, [$"FingerTipCurlUpper{S}"] = .92, [$"FingerEndCurlUpper{S}"] = .53,
                [$"FingerCurlLower{S}"] = 0, [$"FingerTipCurlLower{S}"] = .92, [$"FingerEndCurlLower{S}"] = .53, [$"ThumbCurl{S}"] = -.30, [$"ThumbTipCurl{S}"] = 1.15 };
            foreach (var (j, v) in closed) scene.SetJoint(j, v);
            Check(closed.All(kv => Math.Abs(Position(kv.Key) - kv.Value) < 1e-9), $"{S} hand reaches the closed pose from the CAD drawing");
            double thumbLane = new[] { $"body_seg_thumb_{side}_link", $"body_seg_thumbtip_{side}_link", $"body_thumb_tip_{side}_round_link" }.Max(l => Math.Abs(Lane(World(l))));
            double fingerLane = new[] { 0, 1 }.SelectMany(fi => new[] { $"body_seg_fingertip{fi}_{side}_link", $"body_seg_fingerend{fi}_{side}_link", $"body_finger_tip_{side}_{fi}_round_link" })
                .Min(l => Math.Abs(Lane(World(l))));
            Check(thumbLane < 1e-4 && fingerLane > .0205, $"{S} closed: thumb stays on the midline, fingers in their own lanes ({fingerLane * 1000:F1} mm out)");
            foreach (var j in closed.Keys) scene.SetJoint(j, 0);
        }

        // Closing is limited per joint only; the old shared-plane stop is gone.
        Check(HandClosingLimits.ConstrainFinger(2, 2, 2) == (HandClosingLimits.BaseMaximum, HandClosingLimits.TipMaximum, HandClosingLimits.EndMaximum)
              && HandClosingLimits.ConstrainFinger(.5, .9, .5) == (.5, .9, .5) && HandClosingLimits.ConstrainThumb(-1, 2) == (-.3, HandClosingLimits.ThumbTipMaximum),
            "Hand curls clamp to their own limits, with no coupled stop");
        Check(BodyControlCatalog.Find(ServoNames.B_FingerTipCurlUpperLeft).MaxRadians == HandClosingLimits.TipMaximum
              && BodyControlCatalog.Find(ServoNames.B_ThumbTipCurlRight).MaxRadians == HandClosingLimits.ThumbTipMaximum, "Body controls use the new closing range");

        // A saved stock stop from the old closing plane is raised; a custom range is kept.
        var config = new UrdfConfiguration();
        var baseControl = BodyControlCatalog.Find(ServoNames.B_FingerCurlUpperLeft); var thumbControl = BodyControlCatalog.Find(ServoNames.B_ThumbCurlLeft);
        config.BodyMotions[baseControl.Joint] = new BodyMotionSetting { MinDegrees = -17.188733853924695, MaxDegrees = HandClosingLimits.FormerBaseMaximum * 180 / Math.PI };
        config.BodyMotions[thumbControl.Joint] = new BodyMotionSetting { MinDegrees = -10, MaxDegrees = 12 };
        Check(Math.Abs(config.BodySetting(baseControl).MaxDegrees - HandClosingLimits.BaseMaximum * 180 / Math.PI) < 1e-9, "Saved stock finger stop upgrades to the new range");
        Check(config.BodySetting(thumbControl).MaxDegrees == 12, "A custom thumb range is preserved");
    }
}
