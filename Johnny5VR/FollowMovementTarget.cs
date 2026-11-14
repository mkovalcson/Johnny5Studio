namespace Johnny5VR;
internal enum FollowMovementTarget { Robot, Viewer }
internal static class FollowMovement
{
    internal static string Label(FollowMovementTarget target)=>target==FollowMovementTarget.Viewer?"Move Me":"Move Robot";
    internal static FollowToggle Cue(FollowMovementTarget target)=>target==FollowMovementTarget.Viewer?FollowToggle.MoveMe:FollowToggle.MoveRobot;
    internal static bool DrivesViewer(bool following,FollowMovementTarget target)=>!following||target==FollowMovementTarget.Viewer;
}
