namespace ServoAnimator;

public static class CasterWheelTravel
{
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
