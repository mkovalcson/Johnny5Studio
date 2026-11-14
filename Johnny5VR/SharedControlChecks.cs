using ServoAnimator;

namespace Johnny5VR;

internal static class SharedControlChecks
{
    internal static void Run(Action<bool, string> check)
    {
        check(typeof(ServoNames).Assembly.GetName().Name == "J5Studio.Controls", "VR Player loads the shared command identities.");
        var oldGangs = new HashSet<ServoNames> {
            ServoNames.NeckNodUp, ServoNames.NeckTiltRight, ServoNames.FlapsOpen,
            ServoNames.FlapTiltUp, ServoNames.IrisClose, ServoNames.EyesHorizontalRight,
            ServoNames.EyesVerticalUp, ServoNames.VentsOpen
        };
        foreach (var servo in Enum.GetValues<ServoNames>())
            check(ControlDefinitions.UsesGangDirection(servo) == oldGangs.Contains(servo),
                servo + " retains its original calibration direction lookup.");
        foreach (var servo in Enum.GetValues<ServoNames>().Where(BodyControlCatalog.IsBody))
            check(AnimationRig.BodyName(servo.ToString()[2..]) == BodyControlCatalog.Canonical(servo).ToString()[2..],
                servo + " maps to the common body joint definition.");
        check(AnimationRig.BodyName("CustomJoint") == "CustomJoint", "Custom robot joints retain their names.");
        check(!ControlDefinitions.UsesGangDirection(ServoNames.BothEyePop), "Eye-pop steppers retain their separate direction handling.");
    }
}
