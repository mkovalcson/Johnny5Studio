namespace ServoAnimator;

internal static class TimelineLanes
{
    internal static int For(ServoNames servo) => BodyControlCatalog.IsBody(servo) ? 1 : ServoCommand.IsUrdfOnly(servo) ? 2 : 0;
    internal static readonly string[] Names = { "Head\nNeck\nAudio", "Body", "URDF\nScene" };
}
