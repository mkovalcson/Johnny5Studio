using System.Text.RegularExpressions;

namespace ServoAnimator;

public static class AnimationNames
{
    public static string Words(string name)
    {
        name=ControlLabels.Text(name);
        if (name == "LPiston") return "Left piston";
        if (name == "RPiston") return "Right piston";
        if (name.StartsWith("B_")) name = name[2..];
        var words = Regex.Replace(Regex.Replace(name.TrimStart('_').Replace('_', ' '), "([A-Z]+)([A-Z][a-z])", "$1 $2"), "([a-z0-9])([A-Z])", "$1 $2");
        return words.Length == 0 ? words : char.ToUpperInvariant(words[0]) + words[1..];
    }
    public static string Category(string value) => string.IsNullOrWhiteSpace(value) ? "none" :
        value.Trim().Equals("none", StringComparison.OrdinalIgnoreCase) ? "none" : value.Trim();
}
