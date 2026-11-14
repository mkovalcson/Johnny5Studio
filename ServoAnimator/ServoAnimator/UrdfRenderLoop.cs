using System.Windows.Media;

namespace ServoAnimator;

/// <summary>One ordered visual heartbeat: timeline, targets, motion, background,
/// scene synchronization, then explicit DX11 drawing (WPF draws after the callback).
/// Visual work is capped near 60 Hz; timeline/audio dispatch retains display cadence.
/// A late frame never queues a burst of obsolete updates.</summary>
internal sealed class UrdfRenderLoop
{
    [ThreadStatic] private static UrdfRenderLoop _current;
    internal static UrdfRenderLoop Current => _current ??= new();
    // RenderingTime can be quantized to milliseconds. A small allowance prevents
    // a nominal 60 Hz frame from being skipped for arriving a fraction early.
    private readonly PlaybackFrameCadence _cadence = new(60, earlyToleranceSeconds: .001);
    private readonly bool _attachToRendering;
    private Action<TimeSpan> _animation, _targets, _motion, _background, _synchronize, _render;
    private TimeSpan _lastFrame = TimeSpan.MinValue;
    private bool _subscribed;

    internal UrdfRenderLoop(bool attachToRendering = true) => _attachToRendering = attachToRendering;
    internal int ParticipantCount => Count(_animation) + Count(_targets) + Count(_motion) + Count(_background) + Count(_synchronize) + Count(_render);
    private static int Count(Action<TimeSpan> callbacks) => callbacks?.GetInvocationList().Length ?? 0;

    internal void AddAnimation(Action<TimeSpan> callback) { _animation -= callback; _animation += callback; UpdateSubscription(); }
    internal void RemoveAnimation(Action<TimeSpan> callback) { _animation -= callback; UpdateSubscription(); }
    internal void AddTargets(Action<TimeSpan> callback) { _targets -= callback; _targets += callback; UpdateSubscription(); }
    internal void RemoveTargets(Action<TimeSpan> callback) { _targets -= callback; UpdateSubscription(); }
    internal void AddMotion(Action<TimeSpan> callback) { _motion -= callback; _motion += callback; UpdateSubscription(); }
    internal void RemoveMotion(Action<TimeSpan> callback) { _motion -= callback; UpdateSubscription(); }
    internal void AddBackground(Action<TimeSpan> callback) { _background -= callback; _background += callback; UpdateSubscription(); }
    internal void RemoveBackground(Action<TimeSpan> callback) { _background -= callback; UpdateSubscription(); }
    internal void AddSynchronize(Action<TimeSpan> callback) { _synchronize -= callback; _synchronize += callback; UpdateSubscription(); }
    internal void RemoveSynchronize(Action<TimeSpan> callback) { _synchronize -= callback; UpdateSubscription(); }
    internal void AddRender(Action<TimeSpan> callback) { _render -= callback; _render += callback; UpdateSubscription(); }
    internal void RemoveRender(Action<TimeSpan> callback) { _render -= callback; UpdateSubscription(); }

    private void UpdateSubscription()
    {
        bool needed = ParticipantCount != 0;
        if (_attachToRendering && needed != _subscribed)
        {
            if (needed) CompositionTarget.Rendering += Rendering;
            else CompositionTarget.Rendering -= Rendering;
            _subscribed = needed;
        }
        if (!needed) { _cadence.Reset(); _lastFrame = TimeSpan.MinValue; }
    }

    private void Rendering(object sender, EventArgs e)
    {
        if (e is RenderingEventArgs frame) Advance(frame.RenderingTime);
    }

    internal bool Advance(TimeSpan frameTime)
    {
        if (_lastFrame == frameTime) return false;
        if (frameTime < _lastFrame) _cadence.Reset();
        _lastFrame = frameTime;
        _animation?.Invoke(frameTime);
        if (!_cadence.IsDue(frameTime)) return false;
        _targets?.Invoke(frameTime);
        _motion?.Invoke(frameTime);
        _background?.Invoke(frameTime);
        _synchronize?.Invoke(frameTime);
        _render?.Invoke(frameTime);
        return true;
    }
}
