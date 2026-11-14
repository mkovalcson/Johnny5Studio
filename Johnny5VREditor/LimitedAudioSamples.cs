using NAudio.Wave;

namespace ServoAnimator;

/// <summary>Stop audio at the trim endpoint in the audio thread, independent of render/timer cadence.</summary>
internal sealed class LimitedAudioSamples : ISampleProvider
{
    private readonly ISampleProvider _source;
    private long _remaining;
    public LimitedAudioSamples(ISampleProvider source, double seconds)
    {
        _source = source;
        _remaining = (long)Math.Max(0, Math.Round(seconds * source.WaveFormat.SampleRate)) * source.WaveFormat.Channels;
    }
    public WaveFormat WaveFormat => _source.WaveFormat;
    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, (int)Math.Min(count, _remaining));
        _remaining -= read; return read;
    }
}
