namespace ServoAnimator;

public sealed partial class RobotHeadView
{
    private object _fallApartScene;
    private double? _fallApartFrame;

    internal FallApartClip FallApartClip => _scene?.FallApartClip;

    /// <summary>Timeline-driven fall-apart frame for this view; null shows the live, servo-driven robot.</summary>
    internal void SetTimelineFallApart(double? frame)
    {
        if (_scene == null) return;
        if (ReferenceEquals(_fallApartScene, _scene) && _fallApartFrame == frame) return;
        _fallApartScene = _scene; _fallApartFrame = frame;
        _scene.SetFallApartFrame(frame);
    }
}
