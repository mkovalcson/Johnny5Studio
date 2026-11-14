using System.IO;
using System.Reflection;
using System.Windows;
using ServoAnimator;

internal static partial class Program
{
    private static void NeckChecks()
    {
        var config = ServoConfiguration.CreateDefault();
        var L = RobotControls.NeckTiltLeft; var R = RobotControls.NeckTiltRight;
        var nodP = ServoNames.NeckNodUp; var tiltP = ServoNames.NeckTiltRight;
        double Pulse(ServoNames parent, RobotControls piston, double value) => ServoPulseMapping.ToPulse(config, parent, config.Get(piston), value);
        bool Near(double a, double b, double tol = 1e-9) => Math.Abs(a - b) <= tol;

        // Mixer
        Check(NeckMix.IsComplementary(config), "Default neck gang directions are complementary (nod together, tilt opposite)");
        for (int v = -100; v <= 100; v += 10)
        {
            var nodOnly = NeckMix.Pistons(config, v, 0); var tiltOnly = NeckMix.Pistons(config, 0, v);
            Check(Near(Pulse(nodP, L, nodOnly.Left), Pulse(nodP, L, v)) && Near(Pulse(nodP, R, nodOnly.Right), Pulse(nodP, R, v)), $"Nod {v} alone gives the old Nod pulses");
            Check(Near(Pulse(nodP, L, tiltOnly.Left), Pulse(tiltP, L, v)) && Near(Pulse(nodP, R, tiltOnly.Right), Pulse(tiltP, R, v)), $"Tilt {v} alone gives the old Tilt pulses");
        }
        var fullUp = NeckMix.Pistons(config, 100, 0); var fullUpTilted = NeckMix.Pistons(config, 100, 50);
        bool leftPinned = Near(fullUpTilted.Left, fullUp.Left), rightPinned = Near(fullUpTilted.Right, fullUp.Right);
        Check(leftPinned ^ rightPinned, "Full nod plus tilt: one piston stays at its limit and only the other moves");
        var (nodEff, tiltEff) = NeckMix.Decompose(config, fullUpTilted.Left, fullUpTilted.Right);
        Check(Near(nodEff, 75) && Near(tiltEff, 25), "Clipped full nod + tilt 50 resolves to nod 75, tilt 25 (head drops slightly, half the tilt)");
        var fullDownTilted = NeckMix.Pistons(config, -100, -50);
        Check(NeckMix.Decompose(config, fullDownTilted.Left, fullDownTilted.Right) == (-75, -25), "Full nod down mirrors that");
        foreach (var (n, t) in new[] { (0.0, 0.0), (40.0, 30.0), (-20.0, 70.0), (90.0, -10.0), (-50.0, -50.0) })
        {
            var p = NeckMix.Pistons(config, n, t); var d = NeckMix.Decompose(config, p.Left, p.Right);
            Check(Near(d.Nod, n) && Near(d.Tilt, t), $"Nod {n} + tilt {t} round-trips through the pistons");
        }
        foreach (var piston in new[] { L, R })
            foreach (var v in new[] { -80.0, 35.0 })
                Check(Near(Pulse(tiltP, piston, v), Pulse(nodP, piston, NeckMix.ToNodUnits(config, tiltP, piston, v))),
                      $"A direct {piston} value under Tilt gives the same pulse in Nod units");
        var state = new NeckMix.State(() => config);
        state.Set(40, 20); state.SetPiston(tiltP, L, 10);
        var before = (state.Left, state.Right); state.SetParent(tiltP, state.Tilt);
        Check(Near(state.Left, before.Left) && Near(state.Right, before.Right), "After a direct piston, re-applying tilt continues without a jump");

        // Export resolution
        ServoCommand C(ServoNames s, double t, int v, RobotControls? c = null, bool disable = false) => new() { Servo = s, OffsetSeconds = t, NumericValue = v, Control = c, Disable = disable };
        var resolved = NeckMix.ResolveCommands(new[] { C(nodP, 1, 50), C(ServoNames.NeckTurn, 1, 10), C(tiltP, 2, 30), C(nodP, 3, 100), C(tiltP, 3, 50),
            C(nodP, 4, 0, disable: true), C(tiltP, 5, 20, L) }, config, gangAdjusted: false);
        int At(double t, RobotControls c) => resolved.Single(x => x.OffsetSeconds == t && x.Control == c && !x.Disable).NumericValue;
        Check(!resolved.Any(x => !x.Disable && NeckMix.IsNeckParent(x.Servo) && !x.Control.HasValue), "No nod/tilt parent commands remain");
        Check(resolved.Any(x => x.Servo == ServoNames.NeckTurn) && resolved.Any(x => x.Disable && x.Servo == nodP), "Other commands and Disable pass through");
        Check(At(1, L) == 50 && At(1, R) == 50, "Nod at 1 s resolves to a piston pair");
        var p2 = NeckMix.Pistons(config, 50, 30); var p3 = NeckMix.Pistons(config, 100, 50);
        Check(At(2, L) == (int)Math.Round(p2.Left) && At(2, R) == (int)Math.Round(p2.Right), "Tilt at 2 s mixes with the held nod");
        Check(resolved.Count(x => x.OffsetSeconds == 3) == 2 && At(3, L) == (int)Math.Round(p3.Left) && At(3, R) == (int)Math.Round(p3.Right), "Same-time nod and tilt form one clipped pair");
        Check(resolved.Count(x => x.OffsetSeconds == 5) == 1 && At(5, L) == (int)Math.Round(NeckMix.ToNodUnits(config, tiltP, L, 20)), "A direct piston command resolves to that piston only");
        var adjusted = NeckMix.ResolveCommands(new[] { C(nodP, 1, 50) }, config, gangAdjusted: true);
        Check(adjusted.Single(x => x.Control == L).NumericValue == 50 && adjusted.Single(x => x.Control == R).NumericValue == -50,
              "Individual export writes gang-adjusted piston values, like expanded ganged commands");

        // URDF view: complementary nod and tilt, and one pose per piston pair
        var app = new App(); app.InitializeComponent(); DisableTestStartup(app);
        string body = Path.GetFullPath("Config/Models/Robots/Johnny5FullBody/johnny5_head.urdf");
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        RobotHeadView Fresh() { var v = new RobotHeadView(); v.InstallModel(body, UrdfScene.Load(body), new UrdfFraming()); v.SetServoConfiguration(config); return v; }
        (double Pitch, double Roll) Pose(RobotHeadView v)
        {
            var scene = v.CurrentModelScene; var joints = (System.Collections.IDictionary)scene.GetType().GetField("_joints", flags).GetValue(scene);
            double J(string n) { var j = joints[n]; return (double)j.GetType().GetProperty("Position", flags).GetValue(j); }
            return (J("NeckNodUp"), J("NeckTiltRight"));
        }
        (double, double) Parents(double n, double t) { var v = Fresh(); v.SetServo(nodP, n); v.SetServo(tiltP, t); return Pose(v); }
        var nod50 = Parents(50, 0); var tilt50 = Parents(0, 50); var both = Parents(50, 50);
        Check(Math.Abs(nod50.Item1) > .05 && Near(nod50.Item2, 0, 1e-9) && Math.Abs(tilt50.Item2) > .05 && Near(tilt50.Item1, 0, 1e-9), "Nod alone pitches, tilt alone rolls");
        Check(Near(both.Item1, nod50.Item1, 1e-9) && Near(both.Item2, tilt50.Item2, 1e-9), "Nod 50 and tilt 50 combine without disturbing each other");
        var clipped = Parents(100, 50);
        Check(Near(clipped.Item1, Parents(75, 0).Item1, 1e-9) && Near(clipped.Item2, Parents(0, 25).Item2, 1e-9), "View shows the clipped full nod + tilt as nod 75, tilt 25");
        var tiltFirst = Fresh(); tiltFirst.SetServo(tiltP, 50); tiltFirst.SetServo(nodP, 50);
        Check(Near(Pose(tiltFirst).Pitch, both.Item1, 1e-9) && Near(Pose(tiltFirst).Roll, both.Item2, 1e-9), "Order of nod and tilt does not matter");
        var viaTiltChildren = Fresh(); viaTiltChildren.SetChildServo(tiltP, L, -50); viaTiltChildren.SetChildServo(tiltP, R, 50);
        Check(Near(Pose(viaTiltChildren).Pitch, nod50.Item1, 1e-9) && Near(Pose(viaTiltChildren).Roll, 0, 1e-9),
              "Pistons commanded through Tilt children give exactly the Nod-parent pose for the same pulses");
        var viaNodChildren = Fresh(); viaNodChildren.SetChildServo(nodP, L, 50); viaNodChildren.SetChildServo(nodP, R, -50);
        Check(Near(Pose(viaNodChildren).Roll, Parents(0, -50).Item2, 1e-9) && Near(Pose(viaNodChildren).Pitch, 0, 1e-9),
              "Pistons commanded through Nod children give exactly the Tilt-parent pose for the same pulses");
        var afterDirect = Fresh(); afterDirect.SetServo(nodP, 40); afterDirect.SetChildServo(nodP, L, 70);
        var poseDirect = Pose(afterDirect); afterDirect.SetServo(tiltP, NeckStateTilt(afterDirect));
        Check(Near(Pose(afterDirect).Pitch, poseDirect.Pitch, 1e-9) && Near(Pose(afterDirect).Roll, poseDirect.Roll, 1e-9), "Re-applying tilt after a direct piston does not jump");
        app.Shutdown();

        double NeckStateTilt(RobotHeadView v)
        {
            var neck = typeof(RobotHeadView).GetProperty("NeckState", flags).GetValue(v);
            return (double)neck.GetType().GetProperty("Tilt", flags).GetValue(neck);
        }
    }
}
