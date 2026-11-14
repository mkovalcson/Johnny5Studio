namespace ServoAnimator;

// Data only: reading/writing Config and talking to hardware remain application responsibilities.
public sealed class ServoChannelDefaults
{
    public RobotControls Control { get; set; }
    public int MaestroPort { get; set; }
    public bool Reversed { get; set; }
    public int DefaultPwm { get; set; }
    public int MinPwm { get; set; }
    public int MaxPwm { get; set; }
    public int[] Speeds { get; set; }
    public int[] Accels { get; set; }
}
public sealed class GangDirectionDefaults
{
    public ServoNames Servo { get; set; }
    public RobotControls Control { get; set; }
    public bool Reversed { get; set; }
}
public sealed class BuiltInControlDefaults
{
    public List<ServoChannelDefaults> Servos { get; set; }
    public List<GangDirectionDefaults> GangDirections { get; set; }
}
public static class ControlDefaults
{
    // Every call returns independent channel arrays for editable user configurations.
    public static BuiltInControlDefaults Create()
    {
        ServoChannelDefaults E(RobotControls c, bool rev, int def, int min, int max,
                           int[] sp, int[] ac) => new()
                           {
                               Control = c,
                               MaestroPort = (int)c,
                               Reversed = rev,
                               DefaultPwm = def,
                               MinPwm = min,
                               MaxPwm = max,
                               Speeds = (int[])sp.Clone(),
                               Accels = (int[])ac.Clone()
                           };

        int[] nose = { 100, 50, 0, 50 }, noseA = { 30, 15, 0, 15 };
        int[] neck = { 20, 15, 0, 10 }, neckA = { 10, 6, 0, 5 };
        int[] neckR = { 90, 30, 0, 45 }, neckRA = { 10, 10, 0, 15 };
        int[] gaze = { 0, 60, 0, 20 }, gazeA = { 0, 25, 0, 5 };
        int[] iris = { 90, 90, 0, 10 }, irisA = { 15, 15, 0, 5 };
        int[] brows = { 60, 10, 0, 2 }, browsA = { 15, 5, 0, 3 };
        int[] bBrows = { 40, 10, 0, 2 }, bBrowsA = { 10, 5, 0, 3 };
        int[] vent = { 40, 10, 0, 10 }, ventA = { 20, 5, 0, 5 };
        int[] mfrH = { 0, 1, 0, 10 }, mfrHA = { 0, 1, 0, 20 };
        int[] mfrV = { 100, 50, 0, 50 }, mfrVA = { 30, 25, 0, 25 };
        int[] whipV = { 100, 20, 0, 50 }, whipVA = { 30, 5, 0, 25 };
        int[] whipH = { 0, 0, 0, 10 }, whipHA = { 0, 0, 0, 20 };
        int[] mic = { 0, 0, 0, 50 }, micA = { 0, 0, 0, 25 };

        GangDirectionDefaults G(ServoNames s,
            RobotControls c, bool rev) => new() { Servo = s, Control = c, Reversed = rev };

        return new BuiltInControlDefaults
        {
            // Direction of each control RELATIVE to its gang. These
            // values are the built-in ServoConfig defaults used when no
            // external ServoConfig.json is available. The neck pair still
            // supports independent gang direction for tilt versus nod.
            GangDirections = new List<GangDirectionDefaults>
                    {
                        G(ServoNames.FlapsOpen, RobotControls.BrowLeftTopOpen, true),
                        G(ServoNames.FlapsOpen, RobotControls.BrowRightTopOpen, true),
                        G(ServoNames.FlapsOpen, RobotControls.BrowLeftBottomOpen, false),
                        G(ServoNames.FlapsOpen, RobotControls.BrowRightBottomOpen, false),
                        G(ServoNames.FlapTiltUp, RobotControls.BrowLeftTopTilt, false),
                        G(ServoNames.FlapTiltUp, RobotControls.BrowRightTopTilt, false),
                        G(ServoNames.IrisClose, RobotControls.LeftIris, false),
                        G(ServoNames.IrisClose, RobotControls.RightIris, false),
                        G(ServoNames.EyesHorizontalRight, RobotControls.LeftLensHorizontal, true),
                        G(ServoNames.EyesHorizontalRight, RobotControls.RightLensHorizontal, true),
                        G(ServoNames.EyesVerticalUp, RobotControls.LeftLensVertical, true),
                        G(ServoNames.EyesVerticalUp, RobotControls.RightLensVertical, true),
                        G(ServoNames.VentsOpen, RobotControls.LeftEyeVent, true),
                        G(ServoNames.VentsOpen, RobotControls.RightEyeVent, false),
                        G(ServoNames.NeckTiltRight, RobotControls.NeckTiltLeft, true),
                        G(ServoNames.NeckTiltRight, RobotControls.NeckTiltRight, true),
                        G(ServoNames.NeckNodUp, RobotControls.NeckTiltLeft, false),
                        G(ServoNames.NeckNodUp, RobotControls.NeckTiltRight, true),
                    },
            Servos = new List<ServoChannelDefaults>
                    {
                        E(RobotControls.NoseBasket, false, 850, 850, 1150, nose, noseA),
                        E(RobotControls.NoseBody, true, 1200, 1000, 1500, nose, noseA),
                        E(RobotControls.NeckTiltLeft, false, 1533, 1445, 1594, neck, neckA),
                        E(RobotControls.NeckTiltRight, true, 1480, 1400, 1564, neck, neckA),
                        E(RobotControls.NeckTurn, false, 1325, 850, 1740, neckR, neckRA),
                        E(RobotControls.LeftLensHorizontal, false, 1450, 650, 2250, gaze, gazeA),
                        E(RobotControls.RightLensHorizontal, false, 1450, 650, 2250, gaze, gazeA),
                        E(RobotControls.LeftLensVertical, true, 1400, 600, 2200, gaze, gazeA),
                        E(RobotControls.RightLensVertical, false, 1520, 720, 2320, gaze, gazeA),
                        E(RobotControls.LeftIris, false, 1575, 1350, 1950, iris, irisA),
                        E(RobotControls.RightIris, false, 975, 750, 1350, iris, irisA),
                        E(RobotControls.BrowLeftTopOpen, true, 1523, 920, 1770, brows, browsA),
                        E(RobotControls.BrowRightTopOpen, false, 866, 637, 1417, brows, browsA),
                        E(RobotControls.BrowLeftTopTilt, true, 1800, 910, 1900, bBrows, bBrowsA),
                        E(RobotControls.BrowRightTopTilt, false, 1100, 1000, 1990, bBrows, bBrowsA),
                        E(RobotControls.BrowLeftBottomOpen, false, 1750, 940, 2050, brows, browsA),
                        E(RobotControls.BrowRightBottomOpen, true, 1100, 700, 1910, brows, browsA),
                        E(RobotControls.LeftEyeVent, false, 1765, 1765, 2086, vent, ventA),
                        E(RobotControls.RightEyeVent, true, 1125, 835, 1125, vent, ventA),
                        E(RobotControls.MFR_Rotate, true, 1320, 800, 1865, mfrH, mfrHA),
                        E(RobotControls.MFR_UpDown, true, 550, 550, 1070, mfrV, mfrVA),
                        E(RobotControls.Whip_Antenna_RaiseLower, true, 2200, 1620, 2200, whipV, whipVA),
                        E(RobotControls.Whip_Antenna_Rotate, false, 1400, 500, 2300, whipH, whipHA),
                        E(RobotControls.Microphone_RaiseLower, false, 1625, 1625, 2298, mic, micA),
                    },
        };
    }
}
