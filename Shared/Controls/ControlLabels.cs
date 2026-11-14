using System.Text.RegularExpressions;
namespace ServoAnimator;
public static class ControlLabels
{
    // Keep legacy persisted control identities compatible; normalize visible names.
    public static string Text(string value)=>Regex.Replace(value??"",@"\bBrow(?=[A-Z]|\b)","Flap");
}
