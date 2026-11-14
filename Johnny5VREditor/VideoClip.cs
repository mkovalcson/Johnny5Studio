using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.MediaFoundation;

namespace ServoAnimator;

/// <summary>Video Clip: a video drawn behind the robot for its span on the timeline, covering whatever
/// backdrop is in effect (a 3D stage is hidden while it plays). Its sound plays on its own output and
/// never drives the mouth, hardware or exports.</summary>
public sealed class VideoClipCommand
{
    internal const string MediaFolder = "VideoMedia";
    /// <summary>Video path relative to Configuration (or absolute).</summary>
    public string Path { get; set; } = "";
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TvFit Fit { get; set; } = TvFit.Contain;
    /// <summary>Video sound volume, 0-100.</summary>
    public double Volume { get; set; } = 100;
    public bool Muted { get; set; }
    /// <summary>Video position shown at the clip's start ("Skip to").</summary>
    public double SkipSeconds { get; set; }
    /// <summary>Video position where the clip ends ("End at"); null plays to the end of the file.</summary>
    public double? EndSeconds { get; set; }
    /// <summary>Length of the whole video file, recorded at import so the clip keeps its span when the file is missing.</summary>
    public double SourceSeconds { get; set; }

    /// <summary>Timeline seconds the clip occupies.</summary>
    [JsonIgnore] public double PlayedSeconds => Math.Max(0, (EndSeconds ?? SourceSeconds) - SkipSeconds);

    public static VideoClipCommand Read(string text) { var c = TvJson.Read<VideoClipCommand>(text); c.Validate(); return c; }
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Path)) throw new InvalidDataException("Video Clip needs a video file.");
        if (!TvShowCommand.VideoExtensions.Contains(System.IO.Path.GetExtension(Path).ToLowerInvariant()))
            throw new InvalidDataException("Video Clip supports " + string.Join(" ", TvShowCommand.VideoExtensions) + ".");
        if (!double.IsFinite(Volume) || Volume < 0 || Volume > 100) throw new InvalidDataException("Video Clip volume must be between 0 and 100.");
        if (!double.IsFinite(SourceSeconds) || SourceSeconds <= 0) throw new InvalidDataException("Video Clip needs the video's length.");
        if (!double.IsFinite(SkipSeconds) || SkipSeconds < 0 || SkipSeconds >= SourceSeconds) throw new InvalidDataException("Video Clip Skip to must be at least 0 and before the end of the video.");
        if (EndSeconds is double end && (!double.IsFinite(end) || end <= SkipSeconds || end > SourceSeconds + .001))
            throw new InvalidDataException("Video Clip End at must be after Skip to and no later than the end of the video.");
    }
    public string ToJson() => JsonSerializer.Serialize(this, TvJson.Options);
    public VideoClipCommand Clone() => Read(ToJson());
    internal string FullPath(string configRoot) => System.IO.Path.IsPathRooted(Path) ? Path : System.IO.Path.GetFullPath(System.IO.Path.Combine(configRoot ?? "", Path));
    internal string Label() => "Video " + System.IO.Path.GetFileName(Path);

    /// <summary>Copy a video into Configuration\VideoMedia and return its portable relative path. Videos are large, so an
    /// earlier copy of the same file (same name, size and modified time) is reused instead of copied again.</summary>
    internal static string Import(string configRoot, string source)
    {
        string full = System.IO.Path.GetFullPath(source);
        if (!File.Exists(full)) throw new FileNotFoundException("Video not found.", full);
        if (ConfigPathService.IsWithin(configRoot, full)) return System.IO.Path.GetRelativePath(configRoot, full);
        string folder = System.IO.Path.Combine(configRoot, MediaFolder); Directory.CreateDirectory(folder);
        var info = new FileInfo(full);
        string stem = System.IO.Path.GetFileNameWithoutExtension(full), ext = System.IO.Path.GetExtension(full);
        var existing = new DirectoryInfo(folder).EnumerateFiles(stem + "-*" + ext)
            .FirstOrDefault(f => f.Length == info.Length && f.LastWriteTimeUtc == info.LastWriteTimeUtc);
        if (existing != null) return System.IO.Path.GetRelativePath(configRoot, existing.FullName);
        string destination = System.IO.Path.Combine(folder, stem + "-" + Guid.NewGuid().ToString("N") + ext);
        File.Copy(full, destination);   // keeps the modified time, which the reuse test relies on
        return System.IO.Path.GetRelativePath(configRoot, destination);
    }

    /// <summary>Length of a video file in seconds, read by Media Foundation (works for videos without sound).</summary>
    internal static double ProbeSeconds(string path)
    {
        MediaFoundationApi.Startup();
        MediaFoundationInterop.MFCreateSourceReaderFromURL(path, null, out IMFSourceReader reader);
        IntPtr variant = Marshal.AllocHGlobal(Marshal.SizeOf<PropVariant>());
        try
        {
            reader.GetPresentationAttribute(MediaFoundationInterop.MF_SOURCE_READER_MEDIASOURCE, MediaFoundationAttributes.MF_PD_DURATION, variant);
            double seconds = Convert.ToInt64(Marshal.PtrToStructure<PropVariant>(variant).Value) / 1e7;   // 100 ns units
            if (!double.IsFinite(seconds) || seconds <= 0) throw new InvalidDataException("The video reports no length.");
            return seconds;
        }
        finally { PropVariant.Clear(variant); Marshal.FreeHGlobal(variant); Marshal.ReleaseComObject(reader); }
    }
}

/// <summary>One Video Clip command on the timeline with its parsed settings.</summary>
internal sealed record VideoClipSpan(ServoCommand Command, VideoClipCommand Settings)
{
    internal double Start => Command.OffsetSeconds;
    internal double End => Command.OffsetSeconds + Settings.PlayedSeconds;
    internal string Name => System.IO.Path.GetFileName(Settings.Path);
}

/// <summary>What the video backdrop shows at one timeline instant.</summary>
internal sealed record VideoClipState(VideoClipSpan Clip, double MediaSeconds);

internal static class VideoClipTimeline
{
    /// <summary>Enabled clips with valid settings, in start order. Invalid settings are skipped during playback.</summary>
    internal static List<VideoClipSpan> Clips(IEnumerable<ServoCommand> commands)
    {
        var clips = new List<VideoClipSpan>();
        foreach (var c in commands.Where(c => c.Servo == ServoNames.VideoClip && !c.Disable))
        {
            try { clips.Add(new(c, VideoClipCommand.Read(c.TextValue))); }
            catch (Exception) { /* invalid settings are ignored during playback */ }
        }
        return clips.OrderBy(c => c.Start).ToList();
    }

    /// <summary>The clip whose span contains the cursor and its video position, or null. Clips never overlap when edited
    /// in the editor; for a hand-edited file the later start wins, like audio clips.</summary>
    internal static VideoClipState At(IEnumerable<ServoCommand> commands, double cursor) => At(Clips(commands), cursor);

    /// <summary>As above, from clips parsed earlier (playback evaluates every frame).</summary>
    internal static VideoClipState At(IEnumerable<VideoClipSpan> clips, double cursor)
    {
        var clip = clips.Where(c => cursor >= c.Start - 1e-9 && cursor < c.End).MaxBy(c => c.Start);
        return clip == null ? null : new(clip, clip.Settings.SkipSeconds + Math.Max(0, cursor - clip.Start));
    }

    /// <summary>The first pair of clips whose spans overlap, or null.</summary>
    internal static (VideoClipSpan First, VideoClipSpan Second)? FindOverlap(IEnumerable<ServoCommand> commands)
    {
        var clips = Clips(commands);
        for (int i = 1; i < clips.Count; i++)
            if (clips[i].Start < clips[i - 1].End - 1e-6) return (clips[i - 1], clips[i]);
        return null;
    }

    /// <summary>The clip that a span [start, start + seconds) would overlap, ignoring <paramref name="except"/>.</summary>
    internal static VideoClipSpan Blocking(IEnumerable<ServoCommand> commands, double start, double seconds, ServoCommand except = null) =>
        Clips(commands).FirstOrDefault(c => c.Command != except && start < c.End - 1e-6 && c.Start < start + seconds - 1e-6);

    internal static string Describe((VideoClipSpan First, VideoClipSpan Second) overlap) =>
        $"{overlap.First.Name} ({overlap.First.Start:F3}–{overlap.First.End:F3} s) and {overlap.Second.Name} ({overlap.Second.Start:F3}–{overlap.Second.End:F3} s)";
}
