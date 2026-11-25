namespace ServoAnimator;

/// <summary>Native command units and physical membership shared by all J5 applications.
/// Configuration files can override calibration; these are the built-in definitions.</summary>
public static class ControlDefinitions
{
    public static (int Min, int Max) NominalRangeFor(ServoNames s) => s == ServoNames.URDFSpin ? (-360, 360) : RangeFor(s);

    public static (int Min, int Max) RangeFor(ServoNames s) => s switch
    {
        ServoNames.B_OpenLeftHand or ServoNames.B_OpenRightHand => (0, 100),
        ServoNames.B_PanelsOpen or ServoNames.B_LaserLower => (0, 100),
        ServoNames.URDFCameraYaw or ServoNames.URDFBackgroundYaw => (-36000, 36000),
        ServoNames.URDFCameraPitch => (-75, 75),
        ServoNames.URDFBackgroundPanX or ServoNames.URDFBackgroundPanY or ServoNames.URDFBackgroundPanZ or ServoNames.URDFFloorHeight => (-100000, 100000),
        ServoNames.URDFBackgroundZoom => (10, 1000),
        ServoNames.URDFFollowMargin => (5, 45),
        ServoNames.TrackForward => (-500, 500),
        ServoNames.URDFStageX or ServoNames.URDFStageY => (-100000, 100000),
        ServoNames.URDFStageHeading => (-36000, 36000),
        ServoNames.URDFSpin => (int.MinValue, int.MaxValue), // Cumulative degrees; no one-turn limit.
        ServoNames.URDFRotate => (-360, 360),
        ServoNames.URDFSlide or ServoNames.URDFVertical => (-200, 200),
        ServoNames.URDFZoom => (10, 209),
        ServoNames.LeftEyePop or ServoNames.RightEyePop
            or ServoNames.BothEyePop => (0, 2000),
        ServoNames.NoseBasket or ServoNames.MFR_UpDown
            or ServoNames.Whip_Antenna_RaiseLower or ServoNames.VentsOpen
            or ServoNames.Microphone_RaiseLower => (0, 100),
        _ => (-100, 100),
    };

    public static bool IsTextValued(ServoNames s) =>
                s == ServoNames.RGBCommand || s == ServoNames.Play || s == ServoNames.LoadBackdrop || s == ServoNames.FallApart || s is ServoNames.TvFront or ServoNames.TvShow or ServoNames.TvReturn or ServoNames.UrdfBeam or ServoNames.LcdShow or ServoNames.HideLaser or ServoNames.ToolboxSwap or ServoNames.FireLaser or ServoNames.VideoClip or ServoNames.CameraShot;

    public static readonly Dictionary<ServoNames, RobotControls[]> GangMap = new()
    {
        [ServoNames.NeckTurn] = new[] { RobotControls.NeckTurn },
        [ServoNames.NeckNodUp] = new[] { RobotControls.NeckTiltLeft, RobotControls.NeckTiltRight },
        [ServoNames.NeckTiltRight] = new[] { RobotControls.NeckTiltLeft, RobotControls.NeckTiltRight },
        [ServoNames.FlapsOpen] = new[] { RobotControls.BrowLeftTopOpen, RobotControls.BrowRightTopOpen,
                                                 RobotControls.BrowLeftBottomOpen, RobotControls.BrowRightBottomOpen },
        [ServoNames.FlapTiltUp] = new[] { RobotControls.BrowLeftTopTilt, RobotControls.BrowRightTopTilt },
        [ServoNames.IrisClose] = new[] { RobotControls.LeftIris, RobotControls.RightIris },
        [ServoNames.EyesVerticalUp] = new[] { RobotControls.LeftLensVertical, RobotControls.RightLensVertical },
        [ServoNames.EyesHorizontalRight] = new[] { RobotControls.LeftLensHorizontal, RobotControls.RightLensHorizontal },
        [ServoNames.VentsOpen] = new[] { RobotControls.LeftEyeVent, RobotControls.RightEyeVent },
        [ServoNames.NoseBody] = new[] { RobotControls.NoseBody },
        [ServoNames.NoseBasket] = new[] { RobotControls.NoseBasket },
        [ServoNames.MFR_UpDown] = new[] { RobotControls.MFR_UpDown },
        [ServoNames.MFR_Rotate] = new[] { RobotControls.MFR_Rotate },
        [ServoNames.Microphone_RaiseLower] = new[] { RobotControls.Microphone_RaiseLower },
        [ServoNames.Whip_Antenna_RaiseLower] = new[] { RobotControls.Whip_Antenna_RaiseLower },
        [ServoNames.Whip_Antenna_Rotate] = new[] { RobotControls.Whip_Antenna_Rotate },
        [ServoNames.LeftEyePop] = new[] { RobotControls.LeftEyePop },
        [ServoNames.RightEyePop] = new[] { RobotControls.RightEyePop },
        [ServoNames.BothEyePop] = new[] { RobotControls.LeftEyePop,
                                                  RobotControls.RightEyePop },
        [ServoNames.RGBCommand] = Array.Empty<RobotControls>(),
        [ServoNames.Play] = Array.Empty<RobotControls>(),
    };

    public static RobotControls[] ControlsFor(ServoNames servo) =>
        GangMap.TryGetValue(servo, out var controls) ? controls : Array.Empty<RobotControls>();

    // BothEyePop has separate stepper direction handling, rather than servo gang calibration.
    public static bool UsesGangDirection(ServoNames servo) =>
        servo != ServoNames.BothEyePop && ControlsFor(servo).Length > 1;
}
