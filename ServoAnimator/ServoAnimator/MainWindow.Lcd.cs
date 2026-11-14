namespace ServoAnimator;

public partial class MainWindow
{
    /// <summary>The waist LCD follows the latest LCD Show at the cursor (default message otherwise).</summary>
    private void ApplyLcdTimeline()
    {
        var show = LcdTimeline.StateAt(_doc.Commands, _cursorTime);
        ForEachHeadView(view => view.SetLcd(show, ConfigRoot));
    }
}
