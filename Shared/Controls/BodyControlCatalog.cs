namespace ServoAnimator;

public sealed record BodyControlDefinition(ServoNames Servo, string Joint, double MinRadians, double MaxRadians, string Description);

public static class BodyControlCatalog
{
    public const double NeckLiftBackwardMaximum = .35 + 25 * Math.PI / 180;
    // The shoulder sleeves start 25 degrees from vertical. At +205 degrees
    // they point vertically upward after rotating around the lateral hub axle.
    public const double ShoulderRotateMaximum = 205 * Math.PI / 180;
    /// <summary>The waist panel doors open about 100 degrees on their outer hinges.</summary>
    public const double PanelsOpenMaximum = 1.75;
    /// <summary>The shoulder laser's LaserLower limit (laser_details.py): from 70 degrees up to level, resting on the shoulder.</summary>
    public const double LaserLowerMaximum = 70 * Math.PI / 180;
    /// <summary>TipToe: both track assemblies rotate front-down about the pelvis drum by at most 61.27 degrees, the front-top
    /// tread vertical to the body (see TipToeGeometry). The first release stopped at 45 degrees.</summary>
    public const double TipToeMaximum = 61.27 * Math.PI / 180;
    public const double TipToeFormerMaximum = 45 * Math.PI / 180;
    // Independent v32 joints. The two NeckLift followers are driven from URDF mimic metadata.
    public static readonly BodyControlDefinition[] Controls =
    {
        new(ServoNames.B_WaistLean, "WaistLean", -0.35, 0.35, "Waist: positive leans forward"),
        new(ServoNames.B_ChestTilt, "ChestTilt", -0.35, 0.35, "Chest: positive leans forward"),
        new(ServoNames.B_WaistSwivel, "WaistSwivel", -1.57, 1.57, "Waist swivel: positive turns the upper body to its left"),
        new(ServoNames.B_TipToe, "TipToe", 0, TipToeMaximum, "TipToe: both tracks rotate front-down about the pelvis drum (0 tracks flat, 100 = 61.3 degrees); the body rises, the caster four-bar swings down and the robot tips back onto the caster wheel"),
        new(ServoNames.B_PanelsOpen, "PanelsOpen", 0, PanelsOpenMaximum, "Waist panels: both doors swing open together on their outer hinges to show the LCD (0 closed, 100 fully open)"),
        new(ServoNames.B_LaserLower, "LaserLower", 0, LaserLowerMaximum, "Shoulder laser: 0 up at 70 degrees, 100 lowered level, resting on the shoulder"),
        new(ServoNames.B_NeckLift, "NeckLift", -0.35, NeckLiftBackwardMaximum, "Neck linkage: positive swings backward and raises the head"),
        new(ServoNames.B_ShoulderRotateLeft, "ShoulderRotateLeft", -1, ShoulderRotateMaximum, "Left Shoulder rotate: lateral hub rotation through the raised vertical position"),
        new(ServoNames.B_ShoulderHugLeft, "ShoulderHugLeft", -.5, .5, "Left Shoulder hug: separate upright hinge between the yoke indentations"),
        new(ServoNames.B_ArmTwistLeft, "ArmTwistLeft", -1.57, 1.57, "Left Arm: turn forearm outward"),
        new(ServoNames.B_ElbowLeft, "ElbowLeft", -1, 1.2, "Left Elbow: bend forearm up"),
        new(ServoNames.B_WristPitchLeft, "WristPitchLeft", -1, 1, "Left Wrist: tip hand up"),
        new(ServoNames.B_WristRollLeft, "WristRollLeft", -3.14, 3.14, "Left Wrist: roll hand inward"),
        new(ServoNames.B_FingerSpreadUpperLeft, "FingerSpreadUpperLeft", -0.2, 0.6, "Left Upper spread axle"),
        new(ServoNames.B_FingerSpreadLowerLeft, "FingerSpreadLowerLeft", -0.2, 0.6, "Left Lower spread axle"),
        new(ServoNames.B_FingerCurlUpperLeft, "FingerCurlUpperLeft", -0.3, HandClosingLimits.BaseMaximum, "Left Upper finger base curl"),
        new(ServoNames.B_FingerTipCurlUpperLeft, "FingerTipCurlUpperLeft", -0.3, HandClosingLimits.TipMaximum, "Left Upper fingertip curl"),
        new(ServoNames.B_FingerEndCurlUpperLeft, "FingerEndCurlUpperLeft", -0.3, HandClosingLimits.EndMaximum, "Left Upper finger outer tip curl"),
        new(ServoNames.B_FingerCurlLowerLeft, "FingerCurlLowerLeft", -0.3, HandClosingLimits.BaseMaximum, "Left Lower finger base curl"),
        new(ServoNames.B_FingerTipCurlLowerLeft, "FingerTipCurlLowerLeft", -0.3, HandClosingLimits.TipMaximum, "Left Lower fingertip curl"),
        new(ServoNames.B_FingerEndCurlLowerLeft, "FingerEndCurlLowerLeft", -0.3, HandClosingLimits.EndMaximum, "Left Lower finger outer tip curl"),
        new(ServoNames.B_ThumbCurlLeft, "ThumbCurlLeft", -0.3, HandClosingLimits.ThumbMaximum, "Left Single finger base curl"),
        new(ServoNames.B_ThumbTipCurlLeft, "ThumbTipCurlLeft", -0.3, HandClosingLimits.ThumbTipMaximum, "Left Thumb tip curl"),
        new(ServoNames.B_OpenLeftHand, "OpenHandLeft", 0, 1, "Left hand: 0% closed, 100% fully open"),
        new(ServoNames.B_ShoulderRotateRight, "ShoulderRotateRight", -1, ShoulderRotateMaximum, "Right Shoulder rotate: lateral hub rotation through the raised vertical position"),
        new(ServoNames.B_ShoulderHugRight, "ShoulderHugRight", -.5, .5, "Right Shoulder hug: separate upright hinge between the yoke indentations"),
        new(ServoNames.B_ArmTwistRight, "ArmTwistRight", -1.57, 1.57, "Right Arm: turn forearm outward"),
        new(ServoNames.B_ElbowRight, "ElbowRight", -1, 1.2, "Right Elbow: bend forearm up"),
        new(ServoNames.B_WristPitchRight, "WristPitchRight", -1, 1, "Right Wrist: tip hand up"),
        new(ServoNames.B_WristRollRight, "WristRollRight", -3.14, 3.14, "Right Wrist: roll hand inward"),
        new(ServoNames.B_FingerSpreadUpperRight, "FingerSpreadUpperRight", -0.2, 0.6, "Right Upper spread axle"),
        new(ServoNames.B_FingerSpreadLowerRight, "FingerSpreadLowerRight", -0.2, 0.6, "Right Lower spread axle"),
        new(ServoNames.B_FingerCurlUpperRight, "FingerCurlUpperRight", -0.3, HandClosingLimits.BaseMaximum, "Right Upper finger base curl"),
        new(ServoNames.B_FingerTipCurlUpperRight, "FingerTipCurlUpperRight", -0.3, HandClosingLimits.TipMaximum, "Right Upper fingertip curl"),
        new(ServoNames.B_FingerEndCurlUpperRight, "FingerEndCurlUpperRight", -0.3, HandClosingLimits.EndMaximum, "Right Upper finger outer tip curl"),
        new(ServoNames.B_FingerCurlLowerRight, "FingerCurlLowerRight", -0.3, HandClosingLimits.BaseMaximum, "Right Lower finger base curl"),
        new(ServoNames.B_FingerTipCurlLowerRight, "FingerTipCurlLowerRight", -0.3, HandClosingLimits.TipMaximum, "Right Lower fingertip curl"),
        new(ServoNames.B_FingerEndCurlLowerRight, "FingerEndCurlLowerRight", -0.3, HandClosingLimits.EndMaximum, "Right Lower finger outer tip curl"),
        new(ServoNames.B_ThumbCurlRight, "ThumbCurlRight", -0.3, HandClosingLimits.ThumbMaximum, "Right Single finger base curl"),
        new(ServoNames.B_ThumbTipCurlRight, "ThumbTipCurlRight", -0.3, HandClosingLimits.ThumbTipMaximum, "Right Thumb tip curl"),
        new(ServoNames.B_OpenRightHand, "OpenHandRight", 0, 1, "Right hand: 0% closed, 100% fully open"),
        new(ServoNames.B_ShoulderRotate, "ShoulderRotate", -1, ShoulderRotateMaximum, "Ganged shoulders: symmetric rotate using both calibrated joints"),
        new(ServoNames.B_ShoulderHug, "ShoulderHug", -.5, .5, "Ganged shoulders: symmetric hug using both calibrated joints"),
        new(ServoNames.B_Elbow, "Elbow", -1, 1.2, "Ganged elbows: symmetric bend using both calibrated joints"),
        new(ServoNames.B_WristPitch, "WristPitch", -1, 1, "Ganged wrists: symmetric pitch using both calibrated joints"),
        new(ServoNames.B_WristRoll, "WristRoll", -3.14, 3.14, "Ganged wrists: symmetric roll using both calibrated joints"),
    };
    // Preserve control-type order while showing each left/right pair together in picklists.
    public static IEnumerable<BodyControlDefinition> PairedPicklistControls => Controls
        .GroupBy(c => c.Joint.EndsWith("Left", StringComparison.Ordinal) ? c.Joint[..^4]
            : c.Joint.EndsWith("Right", StringComparison.Ordinal) ? c.Joint[..^5] : c.Joint)
        .SelectMany(pair => pair);

    // Keep saved commands and controller mappings readable without showing old
    // names in new picklists. Each legacy name maps to the same physical axis.
    private static readonly BodyControlDefinition[] LegacyControls =
    {
        new(ServoNames.B_ShoulderRaiseLeft, "ShoulderRaiseLeft", -.1, 1.2, "Legacy left shoulder pivot alias"),
        new(ServoNames.B_ShoulderRaiseRight, "ShoulderRaiseRight", -.1, 1.2, "Legacy right shoulder pivot alias"),
        new(ServoNames.B_ShoulderPivotLeft, "ShoulderPivotLeft", -.5, .5, "Legacy left shoulder hug alias"),
        new(ServoNames.B_ShoulderPivotRight, "ShoulderPivotRight", -.5, .5, "Legacy right shoulder hug alias"),
        new(ServoNames.B_ShoulderPitchLeft, "ShoulderPitchLeft", -1, ShoulderRotateMaximum, "Legacy left shoulder rotate alias"),
        new(ServoNames.B_ShoulderPitchRight, "ShoulderPitchRight", -1, ShoulderRotateMaximum, "Legacy right shoulder rotate alias"),
    };
    private static readonly Dictionary<ServoNames, BodyControlDefinition> ByServo = Controls.Concat(LegacyControls).ToDictionary(c => c.Servo);
    public static bool IsBody(ServoNames servo) => ByServo.ContainsKey(servo);
    public static BodyControlDefinition Find(ServoNames servo) => ByServo.GetValueOrDefault(servo);
    public static bool IsLegacyShoulder(ServoNames servo) => servo is
        ServoNames.B_ShoulderRaiseLeft or ServoNames.B_ShoulderRaiseRight or
        ServoNames.B_ShoulderPivotLeft or ServoNames.B_ShoulderPivotRight or
        ServoNames.B_ShoulderPitchLeft or ServoNames.B_ShoulderPitchRight;
    public static ServoNames Canonical(ServoNames servo) => servo switch
    {
        ServoNames.B_ShoulderRaiseLeft or ServoNames.B_ShoulderPivotLeft => ServoNames.B_ShoulderHugLeft,
        ServoNames.B_ShoulderRaiseRight or ServoNames.B_ShoulderPivotRight => ServoNames.B_ShoulderHugRight,
        ServoNames.B_ShoulderPitchLeft => ServoNames.B_ShoulderRotateLeft,
        ServoNames.B_ShoulderPitchRight => ServoNames.B_ShoulderRotateRight,
        _ => servo,
    };
    public static ServoNames[] PairedMembers(ServoNames servo) => servo switch
    {
        ServoNames.B_ShoulderRotate => [ServoNames.B_ShoulderRotateLeft,ServoNames.B_ShoulderRotateRight],
        ServoNames.B_ShoulderHug => [ServoNames.B_ShoulderHugLeft,ServoNames.B_ShoulderHugRight],
        ServoNames.B_Elbow => [ServoNames.B_ElbowLeft,ServoNames.B_ElbowRight],
        ServoNames.B_WristPitch => [ServoNames.B_WristPitchLeft,ServoNames.B_WristPitchRight],
        ServoNames.B_WristRoll => [ServoNames.B_WristRollLeft,ServoNames.B_WristRollRight],
        _ => []
    };
    public static bool IsPairedGang(ServoNames servo) => PairedMembers(servo).Length==2;
    public static bool IsGang(ServoNames servo) => IsPairedGang(servo)||IsHandGang(servo);
    public static bool IsHandGang(ServoNames servo) => servo is ServoNames.B_OpenLeftHand or ServoNames.B_OpenRightHand;
    public static IEnumerable<BodyControlDefinition> HandMembers(ServoNames servo)
    {
        if (!IsHandGang(servo)) return Enumerable.Empty<BodyControlDefinition>();
        string side = servo == ServoNames.B_OpenLeftHand ? "Left" : "Right";
        return Controls.Where(c => c.Joint.EndsWith(side, StringComparison.Ordinal)
            && (c.Joint.StartsWith("Finger", StringComparison.Ordinal) || c.Joint.StartsWith("Thumb", StringComparison.Ordinal)));
    }
}

public sealed class BodyMotionSetting
{
    public double MinDegrees { get; set; }
    public double MaxDegrees { get; set; }
    public bool Reverse { get; set; }
    public BodyMotionSetting Clone() => (BodyMotionSetting)MemberwiseClone();
    public static BodyMotionSetting Default(BodyControlDefinition control) => new()
    { MinDegrees = control.MinRadians * 180 / Math.PI, MaxDegrees = control.MaxRadians * 180 / Math.PI };
    public double Map(double value, BodyControlDefinition control)
    {
        double minimum = double.IsFinite(MinDegrees) ? MinDegrees / 180 * Math.PI : control.MinRadians;
        double maximum = double.IsFinite(MaxDegrees) ? MaxDegrees / 180 * Math.PI : control.MaxRadians;
        if (minimum >= maximum) { minimum = control.MinRadians; maximum = control.MaxRadians; }
        double center = Math.Clamp(0, minimum, maximum);
        double position = Math.Clamp(Reverse ? -value : value, -100, 100) / 100;
        return position < 0 ? center + -position * (minimum - center) : center + position * (maximum - center);
    }
    public double CommandForAngle(double angle, BodyControlDefinition control)
    {
        double a = Map(-100, control), b = Map(100, control);
        double minimum = Math.Min(a, b), maximum = Math.Max(a, b), center = Math.Clamp(0, minimum, maximum);
        angle = Math.Clamp(angle, minimum, maximum);
        double value = angle < center
            ? -100 * (center - angle) / Math.Max(1e-12, center - minimum)
            : 100 * (angle - center) / Math.Max(1e-12, maximum - center);
        return Reverse ? -value : value;
    }
}
