namespace ServoAnimator;

/// <summary>Numeric scene channels. Distances use centimetres; angles use degrees.</summary>
public static class UrdfCameraCommands
{
    public static readonly ServoNames[] StageChannels = Enum.GetValues<ServoNames>().Where(IsStageCamera).ToArray();
    public static bool IsStageCamera(ServoNames s) => s is ServoNames.URDFBackgroundPanX or
        ServoNames.URDFBackgroundPanY or ServoNames.URDFBackgroundPanZ or ServoNames.URDFBackgroundZoom or
        ServoNames.URDFBackgroundYaw or ServoNames.URDFFollowMargin or ServoNames.URDFFloorHeight;
    public static bool IsCamera(ServoNames s) => IsStageCamera(s) || s is ServoNames.URDFCameraYaw or ServoNames.URDFCameraPitch;
    public static double Read(UrdfStageSettings s, ServoNames name) => name switch
    {
        ServoNames.URDFBackgroundPanX => s.ViewPanX * 100,
        ServoNames.URDFBackgroundPanY => s.ViewPanY * 100,
        ServoNames.URDFBackgroundPanZ => s.ViewPanZ * 100,
        ServoNames.URDFBackgroundZoom => s.ViewZoomPercent,
        ServoNames.URDFBackgroundYaw => s.ViewYawDegrees,
        ServoNames.URDFFollowMargin => s.EdgeMarginPercent,
        ServoNames.URDFFloorHeight => s.FloorHeight * 100,
        _ => 0
    };
    public static void Write(UrdfStageSettings s, ServoNames name, double value)
    {
        switch (name)
        {
            case ServoNames.URDFBackgroundPanX: s.ViewPanX = value / 100; break;
            case ServoNames.URDFBackgroundPanY: s.ViewPanY = value / 100; break;
            case ServoNames.URDFBackgroundPanZ: s.ViewPanZ = value / 100; break;
            case ServoNames.URDFBackgroundZoom: s.ViewZoomPercent = Math.Clamp(value, 10, 1000); break;
            case ServoNames.URDFBackgroundYaw: s.ViewYawDegrees = value; break;
            case ServoNames.URDFFollowMargin: s.EdgeMarginPercent = Math.Clamp(value, 5, 45); break;
            case ServoNames.URDFFloorHeight: s.FloorHeight = value / 100; break;
        }
    }
}
