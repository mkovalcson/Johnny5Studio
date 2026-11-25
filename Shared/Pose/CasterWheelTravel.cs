namespace ServoAnimator;

public static class CasterWheelTravel
{
    /// <summary>
    /// TipToe contributes longitudinal caster travel independently of tread motion. Evaluate it
    /// from the neutral pose so raising, lowering and seeking all follow the same path.
    /// </summary>
    public static (double Angle, double Left, double Right) DuringTipToe(double degrees, double travel, double trail)
    {
        if (travel == 0) return (degrees, 0, 0);

        double heading = CasterSwivel.Wrap(degrees);
        if (heading == 0) return (degrees, travel, travel);
        if (Math.Abs(heading) == 180) return (degrees, -travel, -travel);

        // Integrate d(angle)/ds = -sin(angle)/trail. The wheel travels only
        // the part of the caster's longitudinal motion along its rolling plane.
        double steeringLength = trail > 0 ? trail : .075;
        double angle = heading * Math.PI / 180;
        double halfSin = Math.Sin(angle / 2), halfCos = Math.Cos(angle / 2);
        double decay = Math.Exp(-Math.Abs(travel) / steeringLength);
        double after = 2 * Math.Atan2(halfSin * decay, halfCos);
        double rolled = Math.CopySign(1, travel) *
            (Math.Abs(travel) + steeringLength * Math.Log(halfCos * halfCos + halfSin * halfSin * decay * decay));
        const double wheelHalfSpacing = .034875;
        double swing = wheelHalfSpacing * (after - angle);
        return (after * 180 / Math.PI, rolled - swing, rolled + swing);
    }

    // Project contact travel onto the wheel plane. The twin wheels also see
    // opposite distances as the fork turns around the swivel axis.
    public static (double Left,double Right) Step(double before,double after,double forward,double turnDegrees,double rearDistance,double halfWidth)
    {
        double swing=(after-before)%360;if(swing>180)swing-=360;if(swing<=-180)swing+=360;
        double middle=(before+swing*.5)*Math.PI/180,turn=turnDegrees*Math.PI/180;
        double centre=forward*Math.Cos(middle)-rearDistance*turn*Math.Sin(middle);
        double differential=halfWidth*(turn+swing*Math.PI/180);
        return(centre-differential,centre+differential);
    }
}
