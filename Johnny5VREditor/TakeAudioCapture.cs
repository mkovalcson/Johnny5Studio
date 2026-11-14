using System.IO;
using NAudio.Wave;

namespace ServoAnimator;

internal sealed class TakeAudioCapture : IDisposable
{
    private readonly object _gate = new();
    private WaveInEvent _input;
    private WaveFileWriter _writer;
    private float _peak;
    public string Error { get; private set; }
    public bool Clipped { get; private set; }
    public float TakePeak() { lock (_gate) { float peak = _peak; _peak = 0; return peak; } }
    public void ClearClip() { lock (_gate) Clipped = false; }
    public void Monitor(int device, int bufferMilliseconds = 30)
    {
        Dispose(); Error = null; Clipped = false;
        lock (_gate) _peak = 0;
        _input = new WaveInEvent { DeviceNumber = device, WaveFormat = new WaveFormat(44100, 16, 1), BufferMilliseconds = Math.Clamp(bufferMilliseconds, 10, 100) };
        _input.DataAvailable += (_, e) =>
        {
            lock (_gate)
            {
                for (int i = 0; i + 1 < e.BytesRecorded; i += 2)
                {
                    float sample = Math.Abs((int)BitConverter.ToInt16(e.Buffer, i)) / 32768f;
                    _peak = Math.Max(_peak, sample); if (sample >= .999f) Clipped = true;
                }
                try { _writer?.Write(e.Buffer, 0, e.BytesRecorded); }
                catch (Exception ex) { Error = ex.Message; }
            }
        };
        _input.RecordingStopped += (sender, e) =>
        {
            if (ReferenceEquals(sender, _input)) Error = e.Exception?.Message ?? "Microphone capture stopped. Select the microphone again.";
        };
        try { _input.StartRecording(); } catch { Dispose(); throw; }
    }
    internal static (string Path, FileStream Stream) CreateTakeFile(string folder, string sequence)
    {
        Directory.CreateDirectory(folder);
        foreach (char c in Path.GetInvalidFileNameChars()) sequence = sequence.Replace(c, '_');
        if (string.IsNullOrWhiteSpace(sequence)) sequence = "Sequence";
        for (int take = 1; ; take++)
        {
            string path = Path.Combine(folder, $"{sequence}_Take{take}.wav");
            try { return (path, new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read)); }
            catch (IOException) when (File.Exists(path)) { }
        }
    }
    public string BeginTake(string folder, string sequence)
    {
        if (_input == null || Error != null) throw new InvalidOperationException(Error ?? "Select a microphone first.");
        lock (_gate)
        {
            if (_writer != null) throw new InvalidOperationException("Audio is already recording.");
            var file = CreateTakeFile(folder, sequence);
            try { _writer = new WaveFileWriter(file.Stream, new WaveFormat(44100, 16, 1)); }
            catch { file.Stream.Dispose(); File.Delete(file.Path); throw; }
            Clipped = false; return file.Path;
        }
    }
    public void EndTake() { lock (_gate) { var writer = _writer; _writer = null; writer?.Dispose(); } }
    public void Dispose()
    {
        var input = _input; _input = null;
        input?.Dispose(); EndTake();
    }
}
