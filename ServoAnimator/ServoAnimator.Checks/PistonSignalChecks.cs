using ServoAnimator;

internal static partial class Program
{
    private static void PistonSignalChecks()
    {
        var settings = new PistonSignalSettings();
        void Expect(double pitch, double roll, double left, double right)
        {
            var result = settings.Evaluate(pitch, roll);
            Check(Math.Abs(result.Left - left) < 1e-9 && Math.Abs(result.Right - right) < 1e-9,
                $"Pistons at {pitch}/{roll}: expected {left}/{right}, got {result.Left}/{result.Right}");
        }
        Expect(0, 0, 0, 0); Expect(20, 0, 1, 1); Expect(-20, 0, -1, -1);
        Expect(0, 20, -.5, .5); Expect(0, -20, .5, -.5); Expect(10, 0, .5, .5);
        Expect(10, 10, .25, .75); Expect(40, 0, 1, 1); Expect(20, 20, 0, 1);
        settings.ClipMode = PistonClipMode.Independent; Expect(20, 20, .5, 1);
        settings.ClipMode = PistonClipMode.PreserveTilt; Expect(-20, -20, 0, -1);
        settings.RollAuthority = 1; Expect(0, 20, -1, 1);
        settings.RollAuthority = 0; Expect(10, 20, .5, .5);
        settings.RollAuthority = .5; settings.PitchGain = 0; Expect(20, 20, -.5, .5);
        settings.PitchGain = 1; settings.ReversePitch = true; Expect(20, 0, -1, -1);
        settings.ReversePitch = false; settings.ReverseRoll = true; Expect(0, 20, .5, -.5);
        settings.ReverseRoll = false;
        foreach (var mode in Enum.GetValues<PistonClipMode>())
        foreach (double pitch in new[] { -40.0, -20, -10, 0, 10, 20, 40 })
        foreach (double roll in new[] { -40.0, -20, -10, 0, 10, 20, 40 })
        {
            settings.ClipMode = mode;
            var a = settings.Evaluate(pitch, roll); var b = settings.Evaluate(-pitch, -roll);
            Check(Math.Abs(a.Left + b.Left) < 1e-9 && Math.Abs(a.Right + b.Right) < 1e-9, "Piston symmetry");
        }
        settings.PitchRange = 0;
        bool rejected = false; try { settings.Validate(); } catch (System.IO.InvalidDataException) { rejected = true; }
        Check(rejected, "Zero pitch range rejected");
        var mappings = new FaceTrackingSettings { Mappings = new()
        {
            new() { Enabled = true, Signal = "LPiston", Target = FaceTrackingSettings.NeckLeftTarget, InputMin = -1, InputMax = 1 },
            new() { Enabled = true, Signal = "RPiston", Target = FaceTrackingSettings.NeckRightTarget, InputMin = -1, InputMax = 1 }
        } };
        mappings.Validate(); Check(!mappings.PistonWarnings().Any(), "Independent piston targets accepted");
        mappings.Pistons.PitchNeutral = 5; mappings.Pistons.RollNeutral = 3;
        var values = mappings.SignalValues(new FacePacket { Values = new() { ["headPitch"] = 15, ["headRoll"] = 13 } }, out _);
        Check(Math.Abs(values["LPiston"] - .25) < 1e-9 && Math.Abs(values["RPiston"] - .75) < 1e-9, "Stored neutral applied before piston mixing");
        mappings.Mappings.Add(new() { Enabled = true, Signal = "headPitch", Target = "Servo:NeckNodUp" });
        rejected = false; try { mappings.Validate(); } catch (System.IO.InvalidDataException ex) { rejected = ex.Message.Contains("row 1") && ex.Message.Contains("row 3"); }
        Check(rejected, "Parent conflict identifies both mapping rows");
    }
}
