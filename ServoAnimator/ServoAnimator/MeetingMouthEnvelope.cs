namespace ServoAnimator;

// Shared by Meeting configuration and the active meeting. Attack follows the
// newest captured peak; release follows elapsed render time, not a timer count.
internal sealed class MeetingMouthEnvelope
{
    private TimeSpan? _lastFrame;
    private double _level;
    public void Reset() { _lastFrame = null; _level = 0; }
    public double Advance(double peak, MeetingSettings settings, TimeSpan frameTime)
    {
        double elapsedMs = _lastFrame.HasValue ? Math.Max(0, (frameTime - _lastFrame.Value).TotalMilliseconds) : 0;
        _lastFrame = frameTime;
        double target = Math.Clamp((peak <= settings.Gate ? 0 : peak - settings.Gate) * settings.Gain, 0, 1);
        double release = settings.ReleaseMs <= 0 ? 0 : Math.Exp(-elapsedMs / settings.ReleaseMs);
        return _level = Math.Max(target, _level * release);
    }
}
