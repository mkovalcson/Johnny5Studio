// Preserve names, numeric identities and namespace for existing commands and integrations.
namespace ServoAnimator;

public enum ServoNames
{
    NeckTurn,
    NeckNodUp,
    NeckTiltRight,
    FlapsOpen,
    FlapTiltUp,
    IrisClose,
    EyesVerticalUp,
    EyesHorizontalRight,
    VentsOpen,
    NoseBody,
    NoseBasket,
    MFR_UpDown,
    MFR_Rotate,
    Microphone_RaiseLower,
    Whip_Antenna_RaiseLower,
    Whip_Antenna_Rotate,
    LeftEyePop,
    RightEyePop,

    /// <summary>Ganged eye pop: drives LeftEyePop AND RightEyePop
    /// together (both Tic steppers).</summary>
    BothEyePop,

    RGBCommand,

    /// <summary>Pseudo-servo used ONLY in exported animation files: a
    /// "Play" command carries the audio file path so the playback
    /// hardware knows what to start and when. It never appears in the
    /// servo grid or the command editor.</summary>
    Play,
    URDFSpin,
    URDFRotate,
    URDFSlide,
    URDFVertical,
    URDFZoom,
    B_WaistLean,
    B_ChestTilt,
    B_NeckLift,
    B_ShoulderRotateLeft,
    B_ShoulderRaiseLeft,
    B_ArmTwistLeft,
    B_ElbowLeft,
    B_WristPitchLeft,
    B_WristRollLeft,
    B_FingerSpreadUpperLeft,
    B_FingerSpreadLowerLeft,
    B_FingerCurlUpperLeft,
    B_FingerTipCurlUpperLeft,
    B_FingerCurlLowerLeft,
    B_FingerTipCurlLowerLeft,
    B_ThumbCurlLeft,
    B_ShoulderRotateRight,
    B_ShoulderRaiseRight,
    B_ArmTwistRight,
    B_ElbowRight,
    B_WristPitchRight,
    B_WristRollRight,
    B_FingerSpreadUpperRight,
    B_FingerSpreadLowerRight,
    B_FingerCurlUpperRight,
    B_FingerTipCurlUpperRight,
    B_FingerCurlLowerRight,
    B_FingerTipCurlLowerRight,
    B_ThumbCurlRight,
    LoadBackdrop,
    URDFStageX,
    URDFStageY,
    URDFStageHeading,
    TrackForward,
    FallApart,
    TvFront,
    TvShow,
    TvReturn,
    UrdfBeam,
    URDFCameraYaw,
    URDFCameraPitch,
    URDFBackgroundPanX,
    URDFBackgroundPanY,
    URDFBackgroundPanZ,
    URDFBackgroundZoom,
    URDFBackgroundYaw,
    URDFFollowMargin,
    URDFFloorHeight,
    // Appended rather than placed with the other body controls: a few places use the numeric value.
    // Picklists and the URDF Configuration screen follow BodyControlCatalog's order instead.
    B_WaistSwivel,
    B_ThumbTipCurlLeft,
    B_ThumbTipCurlRight,
    B_FingerEndCurlUpperLeft,
    B_FingerEndCurlUpperRight,
    B_FingerEndCurlLowerLeft,
    B_FingerEndCurlLowerRight,
    B_OpenLeftHand,
    B_OpenRightHand,
    B_ShoulderHugLeft,
    B_ShoulderHugRight,
    // Keep old serialized names readable. Distinct appended values avoid
    // changing numeric identities or the new names emitted by ToString().
    B_ShoulderPitchLeft,
    B_ShoulderPitchRight,
    B_ShoulderPivotLeft,
    B_ShoulderPivotRight,
    /// <summary>Opens both hinged waist panels together (PanelsOpen joint; the right door mimics it).</summary>
    B_PanelsOpen,
    /// <summary>URDF-only text command: picture or text shown on the waist LCD.</summary>
    LcdShow,
    /// <summary>URDF-only text command: hide (true) or show (false) the optional shoulder laser.</summary>
    HideLaser,
    /// <summary>Lowers the optional shoulder laser on its hinge (LaserLower joint): 0 up, 100 resting on the shoulder.</summary>
    B_LaserLower,
    /// <summary>URDF-only text command: show the red toolbox (true) or the laser (false) on the laser's hinge.</summary>
    ToolboxSwap,
    /// <summary>URDF-only text command: a burst of laser bolts from the shoulder laser into the URDF scene.</summary>
    FireLaser,
    /// <summary>URDF-only text command: a video behind the robot for the clip's span, with its own sound that never
    /// drives the mouth. Like Play it has a timeline bar instead of a servo-grid row.</summary>
    VideoClip,
    // Appended: retain every existing serialized numeric identity.
    B_ShoulderRotate,
    B_ShoulderHug,
    B_Elbow,
    B_WristPitch,
    B_WristRoll,
    // Appended 2026-11-12: both tracks rotate front-down about the pelvis drum (URDF only).
    B_TipToe,
}

public enum ServoSpeed
{
    // Stored as "N/C" in ServoCommand JSON.  This deliberately sits
    // outside the 0..3 indices used by ServoConfigEntry speed/accel
    // arrays so it can never be mistaken for a hardware profile.
    NoChange = 4,
    Default = 0,
    Slow = 1,
    Fast = 2,
    Crawl = 3,
}

public enum RobotControls
{
    None = 1000,

    // Left
    LeftEyeVent = 0,
    LeftIris = 1,
    LeftLensHorizontal = 2,
    LeftLensVertical = 3,
    NeckTiltLeft = 4,
    BrowLeftBottomOpen = 5,

    // Nose
    BrowLeftTopOpen = 6,
    BrowLeftTopTilt = 7,
    NoseBody = 8,
    NoseBasket = 9,
    BrowRightTopTilt = 10,
    BrowRightTopOpen = 11,

    // Right
    BrowRightBottomOpen = 12,
    NeckTiltRight = 13,
    RightLensVertical = 14,
    RightLensHorizontal = 15,
    RightIris = 16,
    RightEyeVent = 17,

    // Misc
    Whip_Antenna_RaiseLower = 18,
    Whip_Antenna_Rotate = 19,
    MFR_UpDown = 20,
    MFR_Rotate = 21,
    NeckTurn = 22,
    Microphone_RaiseLower = 23,

    // Tic T249 eye-pop steppers (not Maestro PWM channels)
    LeftEyePop = 200,
    RightEyePop = 201,
    BothEyePop = 203,

    // Arduino RGB rings (identification only)
    LeftEyeRGBLightFront = 101,
    LeftEyeRGBLightBack = 102,
    RightEyeRGBLightFront = 103,
    RightEyeRGBLightBack = 104,
}
