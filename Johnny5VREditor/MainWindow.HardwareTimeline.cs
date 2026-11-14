namespace ServoAnimator;

public partial class MainWindow
{
    private TimelineHardwarePose _timelineHardwarePose;
    private TimelineHardwareChanges _timelineHardwareChanges = new();
    private long _timelinePoseSegment;
    private bool _timelineHardwareWasAllowed;

    private bool QueueTimelineHardwarePose(double time, bool initial = false)
    {
        if (_timelineHardwarePose == null) return true;
        var pose = _timelineHardwarePose.Evaluate(time)
            .Where(c => !RecordingOwns(c.Servo, c.Control)).ToArray();
        // Validate before either output changes, including URDF-only playback.
        if (_controllerPlaybackSource.HasValue && !AllowControllerTargets(pose)) { PausePlayback(); return false; }
        if (!LiveDrive || !_hw.Connected || !PlaybackOutputAllowed(true))
        { _timelineHardwareWasAllowed = false; return true; }
        if (!_timelineHardwareWasAllowed) _timelineHardwareChanges = new TimelineHardwareChanges();
        _timelineHardwareWasAllowed = true;
        bool controllerOwned = _controllerPlaybackSource.HasValue;
        long epoch = Interlocked.Read(ref _controllerOutputEpoch);
        var changes = _timelineHardwareChanges;
        Action send = () =>
        {
            if (controllerOwned && (!_controllerPlaybackPhysicalAllowed || epoch != Interlocked.Read(ref _controllerOutputEpoch))) return;
            DispatchHardwareBatch(changes.Take(pose));
        };
        // Start poses and authored boundaries retain their ordering. Between
        // boundaries, slow output devices need the newest pose, not a backlog.
        if (initial) { _timelinePoseSegment++; _hardwarePlaybackQueue.Enqueue(send); }
        else _hardwarePlaybackQueue.EnqueueLatest($"timeline-pose:{_timelinePoseSegment}", send);
        return true;
    }
}
