namespace Johnny5VR;
internal static class CasterSwivel
{
    internal static double Target(double currentTarget,double forwardMotion,double turnMotion,double rearDistance) => ServoAnimator.CasterSwivel.Target(currentTarget,forwardMotion,turnMotion,rearDistance);
    internal static double Advance(double angle,double target,double seconds) => ServoAnimator.CasterSwivel.Advance(angle,target,seconds);
    internal static double? TravelDirection(double forwardMotion,double turnMotion,double rearDistance) => ServoAnimator.CasterSwivel.TravelDirection(forwardMotion,turnMotion,rearDistance);
    internal static double Roll(double angle,double forwardMotion,double turnMotion,double rearDistance,double trail) => ServoAnimator.CasterSwivel.Roll(angle,forwardMotion,turnMotion,rearDistance,trail);
    internal static double Wrap(double degrees) => ServoAnimator.CasterSwivel.Wrap(degrees);
}
