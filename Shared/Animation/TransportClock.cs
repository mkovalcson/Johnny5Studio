#nullable enable
using System.Diagnostics;

namespace ServoAnimator;

public sealed class TransportClock(Func<double>? timeSource = null)
{
    private readonly Func<double> now = timeSource ?? (() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
    private double accumulated, started;
    public bool IsRunning { get; private set; }
    public double Elapsed => accumulated + (IsRunning ? Math.Max(0, now() - started) : 0);
    public void Start() { if (IsRunning) return; started = now(); IsRunning = true; }
    public void Stop() { accumulated = Elapsed; IsRunning = false; }
    public void Reset() { accumulated = 0; IsRunning = false; }
    public void Restart() { Reset(); Start(); }
}
