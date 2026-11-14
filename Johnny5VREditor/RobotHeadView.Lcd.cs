namespace ServoAnimator;

public sealed partial class RobotHeadView
{
    private UrdfScene _lcdScene;
    private string _lcdKey;

    /// <summary>Show this LCD Show content on the robot's waist LCD (no-op without one).</summary>
    internal void SetLcd(LcdShowCommand show, string configRoot)
    {
        if (_scene == null || !_scene.HasLcd || show == null) return;
        string key = show.ToJson() + "|" + configRoot;
        if (ReferenceEquals(_lcdScene, _scene) && key == _lcdKey) return;
        _lcdScene = _scene; _lcdKey = key;
        _scene.SetLcdImage(LcdRenderer.Render(show, configRoot, _scene.LcdAspect));
    }

    internal bool HasLcd => _scene?.HasLcd == true;
}
