using System.IO;
using System.Reflection;
using System.Windows.Media.Media3D;
using ServoAnimator;

internal static partial class Program
{
    private static void CasterChecks()
    {
        string body = Path.GetFullPath("Config/Models/Robots/Johnny5FullBody/johnny5_head.urdf");
        var scene = UrdfScene.Load(body);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var visuals = (System.Collections.IDictionary)typeof(UrdfScene).GetField("_linkVisuals", flags).GetValue(scene);
        var linkWorld = typeof(UrdfScene).GetMethod("TreadLinkWorld", flags);
        var pivot = (Point3D)typeof(UrdfScene).GetField("_casterPivot", flags).GetValue(scene);
        Rect3D Bounds(string link)
        {
            var world = (Matrix3D)linkWorld.Invoke(scene, new object[] { link });
            var all = Rect3D.Empty;
            foreach (GeometryModel3D v in (IEnumerable<GeometryModel3D>)visuals[link])
            {
                var m = v.Transform.Value; m.Append(world);
                all.Union(new MatrixTransform3D(m).TransformBounds(v.Geometry.Bounds));
            }
            return all;
        }
        Point3D Centre(Rect3D r) => new(r.X + r.SizeX / 2, r.Y + r.SizeY / 2, r.Z + r.SizeZ / 2);

        // Geometry: the axle trails the swivel axis; the tread reaches the floor; the plate sits in the wheel gap.
        var wheel = Centre(Bounds("body_caster_wheel_l_link"));
        double trail = pivot.X - wheel.X;
        Check(Math.Abs(trail - .095) < .001, $"Caster wheel trails 95 mm behind the swivel axis (got {trail * 1000:F1} mm)");
        double lowest = new[] { "l", "r" }.SelectMany(sd => Enumerable.Range(0, 16).SelectMany(i => new[] { $"0{i:D2}", $"1{i:D2}" })
            .Select(id => Bounds($"body_caster_wheel_{sd}_lug_{id}_link").Z)).Min();
        Check(Math.Abs(lowest - -1.0774) < .0015, $"Caster tread lugs reach the floor (lowest {lowest:F4} m)");
        var plate = Bounds("body_caster_fork_arm_link");
        Check(plate.Y > -.006 && plate.Y + plate.SizeY < .006 && plate.X < wheel.X - .08 && plate.X + plate.SizeX > pivot.X,
            "Trailing plate lies in the wheel gap and runs from the swivel block round behind the wheel");

        // Swivel: every trailing part turns about the axis; the bellows, collar and arm stay put.
        var swivelling = typeof(UrdfScene).GetField("_casterVisuals", flags).GetValue(scene) as System.Collections.ICollection;
        var moving = new[] { "body_caster_fork_arm_link", "body_caster_fork_web_link", "body_caster_axle_link", "body_caster_wheel_r_link",
            "body_caster_wheel_l_lug_003_link", "body_caster_hub_accent_l_link", "body_caster_hubcap_face_r_link", "body_caster_hubcap_plug_l_link" };
        var still = new[] { "body_caster_collar_link", "body_caster_spring_lower_00_link", "body_caster_head_link" };
        var before = moving.Concat(still).ToDictionary(l => l, l => Centre(Bounds(l)));
        scene.SetCasterSwivel(90);
        var axle = Centre(Bounds("body_caster_axle_link"));
        Check(Math.Abs(axle.X - pivot.X) < .001 && Math.Abs(Math.Abs(axle.Y - pivot.Y) - trail) < .001,
            "At 90 degrees the axle swings round to the side of the swivel, still 95 mm from the axis");
        Check(moving.All(l => (Centre(Bounds(l)) - before[l]).Length > .01), "The plate, axle, wheels, lugs and hub detail all swivel");
        Check(still.All(l => (Centre(Bounds(l)) - before[l]).Length < 1e-6), "The bellows, collar and arm head stay fixed");
        Check(swivelling.Count >= moving.Length, "Caster swivel collected its visuals");
        scene.SetCasterSwivel(0);
        Check(moving.All(l => (Centre(Bounds(l)) - before[l]).Length < 1e-6), "Returning to 0 degrees restores the trailing caster exactly");

        // Rolling turns the tire/lugs/hub about the axle, independently of steering.
        var stationaryRoll=new[]{"body_caster_yoke_link","body_caster_fork_arm_link","body_caster_axle_link"}.Concat(still).ToDictionary(l=>l,l=>Centre(Bounds(l)));
        var lugBefore=Centre(Bounds("body_caster_wheel_l_lug_003_link"));
        var rightBefore=Centre(Bounds("body_caster_wheel_r_lug_003_link"));
        double leftDistance=.075*Math.PI/2,rightDistance=.075*Math.PI/4;
        scene.SetCasterSwivel(0,leftDistance,rightDistance);
        var rotation=Matrix3D.Identity;rotation.RotateAt(new Quaternion(new Vector3D(0,1,0),90),wheel);
        Check((Centre(Bounds("body_caster_wheel_l_lug_003_link"))-rotation.Transform(lugBefore)).Length<1e-6,"Tread lugs roll through the correct angle from travel and wheel radius");
        Check((Centre(Bounds("body_caster_wheel_l_link"))-wheel).Length<1e-6,"Rolling keeps the wheel centred on its axle");
        Check(stationaryRoll.All(p=>(Centre(Bounds(p.Key))-p.Value).Length<1e-6),"Wheel rotation leaves fork, axle and fixed support unchanged");
        var rightWheel=Centre(Bounds("body_caster_wheel_r_link"));rotation=Matrix3D.Identity;rotation.RotateAt(new Quaternion(new Vector3D(0,1,0),45),rightWheel);
        Check((Centre(Bounds("body_caster_wheel_r_lug_003_link"))-rotation.Transform(rightBefore)).Length<1e-6,"Twin caster wheels can roll different distances in a turn");
        scene.SetCasterSwivel(90,leftDistance,rightDistance);
        rotation=Matrix3D.Identity;rotation.RotateAt(new Quaternion(new Vector3D(0,1,0),90),wheel);rotation.RotateAt(new Quaternion(new Vector3D(0,0,1),90),pivot);
        Check((Centre(Bounds("body_caster_wheel_l_lug_003_link"))-rotation.Transform(lugBefore)).Length<1e-6,"Wheel rolling and caster steering compose around their separate pivots");
        scene.SetCasterSwivel(0);
        Check((Centre(Bounds("body_caster_wheel_l_lug_003_link"))-lugBefore).Length<1e-6,"Reset restores wheel and tread positions exactly");
        using(var mirror=new Dx11SceneMirror())
        {
            mirror.Synchronize(scene.RootModel);long geometryUpdates=mirror.GeometryUpdates;
            scene.SetCasterSwivel(35,.04,.025);mirror.Synchronize(scene.RootModel);
            Check(mirror.GeometryUpdates==geometryUpdates,"Direct3D caster rolling reuses existing geometry buffers");
            scene.SetCasterSwivel(0);
        }

        // Motion: a trailing caster is rolled round by the robot's travel, through the full circle.
        double L = scene.CasterTrail, r = .45;
        Check(Math.Abs(L - .095) < .001, $"The scene measures the 95 mm trail (got {L * 1000:F1} mm)");
        double Drive(double angle, double metres, double degrees = 0, int frames = 50)
        {
            for (int i = 0; i < frames; i++) angle = CasterSwivel.Roll(angle, metres / frames, degrees / frames, r, L);
            return angle;
        }
        Check(Drive(0, .5) == 0, "Driving forward keeps the wheel trailing straight behind");
        double partly = Drive(0, -.06), halfway = Drive(0, -.4), reversed = Drive(0, -1);
        Check(Math.Abs(partly) is > 5 and < 175, $"Reversing swings the wheel round gradually (after 6 cm: {partly:F0} deg)");
        Check(Math.Abs(halfway) is > 90 and < 170, $"After 40 cm of reversing the wheel is well on its way round ({halfway:F0} deg)");
        Check(Math.Abs(reversed) > 179, $"Reversing ends with the wheel under the arm, 180 degrees round (got {reversed:F1})");
        Check(Math.Abs(Drive(reversed, -.5) - reversed) < 1, "Continuing in reverse keeps it under the arm");
        Check(Math.Abs(Drive(reversed, 1)) < 1, "Driving forward again swings it back behind the swivel");
        double spun = Drive(0, 0, 360, 200), expected = -90 - Math.Asin(L / r) * 180 / Math.PI;
        Check(Math.Abs(spun - expected) < 2, $"Spinning in place settles the wheel across the swivel's circle ({spun:F1}, expected {expected:F1})");
        Check(Math.Abs(Drive(spun, 0, -360, 200) - (-expected)) < 2, "Reversing the spin swings the wheel to the other side");
        Check(CasterSwivel.Roll(37, 0, 0, r, L) == 37, "A stopped robot leaves the caster where it is");
        Check(CasterSwivel.Roll(37, -.5, 0, r, 0) == 37, "Without a trail the rolling model is not used");
        double coarse = Drive(20, -.3, 30, 3), fine = Drive(20, -.3, 30, 300);
        Check(Math.Abs(CasterSwivel.Wrap(coarse - fine)) < 2, $"The result does not depend on frame rate ({coarse:F1} vs {fine:F1})");
        var reverse = new[] { new ServoCommand { Servo = ServoNames.TrackForward, OffsetSeconds = 0, NumericValue = -30 } };
        var path = new TrackForwardPath(reverse, 0, 0, null, null, 0, 0, null, null, r, L);
        Check(Math.Abs(path.CasterAt(4).Angle) > 179 && Math.Abs(path.CasterAt(4).Target) > 179, "The timeline path swings the caster under the arm when reversing");
        Check(path.CasterAt(.05).Angle == new TrackForwardPath(reverse, 0, 0, null, null, 0, 0, null, null, r, L).CasterAt(.05).Angle, "The timeline caster is deterministic");
        var commands=new[]{new ServoCommand{Servo=ServoNames.TrackForward,OffsetSeconds=0,NumericValue=20},new ServoCommand{Servo=ServoNames.TrackForward,OffsetSeconds=1,NumericValue=0}};
        var straight=new TrackForwardPath(commands,0,0,null,null,0,0,null,null,r,L);var rolled=straight.CasterAt(1);
        Check(Math.Abs(rolled.Left-.2)<1e-9&&rolled.Left==rolled.Right,"Forward timeline caster rolling follows travel in metres");
        Check(straight.CasterAt(2)==rolled,"Stopped timeline wheels retain their final rotation");
        Check(straight.CasterAt(.25)==new TrackForwardPath(commands,0,0,null,null,0,0,null,null,r,L).CasterAt(.25),"Backward scrubbing restores caster wheel rotation deterministically");
        Check(path.CasterAt(.02).Left<0&&path.CasterAt(.02).Right<0,"Reverse motion starts by reversing wheel rotation");
        var turning=CasterWheelTravel.Step(-90,-90,0,10,r,.034875);
        Check(turning.Right>turning.Left&&turning.Left>0,"Inside and outside wheels roll separate signed turning distances");
        Check(CasterWheelTravel.Step(179,-179,0,0,r,.034875).Left>-.002,"Steering angle wraparound cannot cause a full wheel-rotation jump");
    }
}
