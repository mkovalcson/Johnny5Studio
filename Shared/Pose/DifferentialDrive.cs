namespace ServoAnimator;
public static class DifferentialDrive
{
    public static (double Left, double Right) BeltTravel(double forwardMetres, double turnDegrees, double gaugeMetres)
    {
        double arc = turnDegrees * Math.PI / 180 * gaugeMetres / 2;
        return (forwardMetres - arc, forwardMetres + arc);
    }
}
