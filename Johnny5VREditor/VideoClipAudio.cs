using NAudio.Wave;

namespace ServoAnimator;

/// <summary>A video clip's sound on its own output device, alongside the voice audio, so the two mix instead of
/// taking turns. Nothing here feeds the mouth amplitude, which reads only the voice sources. Like the voice output,
/// every start uses a fresh reader and device (see MainWindow.DisposeAudioDevice for why).</summary>
internal sealed class VideoClipAudio : IDisposable
{
    // Re-seek only past this drift: small corrections are inaudible as drift but audible as clicks.
    private const double DriftSeconds = .12;
    private WaveOutEvent _output;
    private AudioFileReader _reader;
    private ServoCommand _clip;
    private double _origin, _rate = 1, _soundEndsAt;
    private int _failedStarts;
    private ServoCommand _failedClip;

    /// <summary>Timeline time the device has played up to, or null when nothing is playing. Lets the playback clock
    /// follow the video's sound when no voice audio is playing.</summary>
    internal double? TimelinePosition
    {
        get
        {
            if (_output?.PlaybackState != PlaybackState.Playing) return null;
            try { return _origin + _output.GetPosition() / (double)_output.OutputWaveFormat.AverageBytesPerSecond * _rate; }
            catch { return null; }
        }
    }

    /// <summary>Play the clip's sound at timeline time <paramref name="t"/> while the transport runs; silent while paused,
    /// scrubbing, muted, or when the file has no sound.</summary>
    internal void Sync(VideoClipState state, string configRoot, double t, bool running, double rate, float masterVolume)
    {
        var settings = state?.Clip.Settings;
        if (!running) _failedStarts = 0;   // every new run retries a file that failed before
        if (!running || settings == null || settings.Muted || settings.Volume <= 0) { Stop(); return; }
        float volume = (float)Math.Clamp(masterVolume * settings.Volume / 100, 0, 1);
        if (ReferenceEquals(_clip, state.Clip.Command) && Math.Abs(rate - _rate) < 1e-9)
        {
            if (_output != null) _output.Volume = volume;
            // A sound track can be a little shorter than the picture: stay silent until the clip changes.
            if (t >= _soundEndsAt - .05) return;
            if (TimelinePosition is double played && Math.Abs(played - t) <= DriftSeconds) return;
        }
        // A file without sound (or an unsupported codec) fails to open; give up on it after a few tries per clip.
        if (ReferenceEquals(_failedClip, state.Clip.Command) && _failedStarts >= 3) { Stop(); return; }
        try { Start(state, configRoot, t, rate, volume); _failedClip = null; _failedStarts = 0; }
        catch
        {
            Stop();
            if (!ReferenceEquals(_failedClip, state.Clip.Command)) { _failedClip = state.Clip.Command; _failedStarts = 0; }
            _failedStarts++;
        }
    }

    private void Start(VideoClipState state, string configRoot, double t, double rate, float volume)
    {
        Stop();
        var settings = state.Clip.Settings;
        _reader = new AudioFileReader(settings.FullPath(configRoot));
        double position = Math.Clamp(state.MediaSeconds, 0, _reader.TotalTime.TotalSeconds);
        long bytes = (long)(position * _reader.WaveFormat.AverageBytesPerSecond);
        bytes -= bytes % Math.Max(1, _reader.WaveFormat.BlockAlign);   // frame-aligned, as for the voice audio
        _reader.Position = Math.Min(bytes, _reader.Length);
        double from = _reader.CurrentTime.TotalSeconds;
        double end = Math.Min(settings.EndSeconds ?? double.MaxValue, _reader.TotalTime.TotalSeconds);
        _origin = state.Clip.Start + from - settings.SkipSeconds;
        _soundEndsAt = state.Clip.Start + end - settings.SkipSeconds;
        _rate = rate; _clip = state.Clip.Command;
        _output = new WaveOutEvent { DesiredLatency = 100, Volume = volume };
        _output.Init(new NAudio.Wave.SampleProviders.WdlResamplingSampleProvider(
            new RateSampleProvider(new LimitedAudioSamples(_reader, Math.Max(0, end - from)), rate), _reader.WaveFormat.SampleRate));
        _output.Play();
    }

    internal void Stop()
    {
        if (_output != null)
        {
            try { _output.Stop(); } catch { /* device may already be gone */ }
            _output.Dispose(); _output = null;
        }
        _reader?.Dispose(); _reader = null; _clip = null;
    }

    public void Dispose() => Stop();
}
