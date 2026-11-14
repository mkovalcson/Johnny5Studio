using System.Globalization;
using System.Windows.Data;

namespace ServoAnimator;

internal static class UiNames
{
    public static string Words(string name) => AnimationNames.Words(name);
    public static string Action(string name) => name == "Default pose" ? "Zero numeric controls" : name;
    public static string Target(string target)
    {
        if (ControllerTargets.TryServo(target, out var servo, out var child))
            return ServoCommand.DisplayName(servo) + (child.HasValue ? " → " + Words(child.Value.ToString()) : "");
        return target.StartsWith("Action:") ? Action(target[7..]) : target;
    }
}

internal sealed class UiNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => UiNames.Words(value?.ToString() ?? "");
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
