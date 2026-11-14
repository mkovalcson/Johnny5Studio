namespace Johnny5.VrBridge;
internal static class CasterWheelTravel
{
    internal static (double Left,double Right) Step(double before,double after,double forward,double turnDegrees,double rearDistance,double halfWidth)
        => ServoAnimator.CasterWheelTravel.Step(before,after,forward,turnDegrees,rearDistance,halfWidth);
}
