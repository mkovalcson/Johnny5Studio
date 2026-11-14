using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ServoAnimator;

/// <summary>Robot Frames export settings for one sequence: a transparent PNG sequence of the URDF robot, rendered
/// frame by frame at exact timeline times (no real-time capture), for compositing over the video in an editor.</summary>
public sealed class RobotFrameExportSettings
{
    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;
    public double FramesPerSecond { get; set; } = 30;
    public double StartSeconds { get; set; }
    /// <summary>Last timeline time to render; null renders to the end of the sequence as it is at export time.</summary>
    public double? EndSeconds { get; set; }
    /// <summary>Output folder; empty uses "&lt;sequence&gt; frames" beside the sequence.</summary>
    public string Folder { get; set; } = "";
    /// <summary>Leave a 3D stage backdrop out so the frames hold only the robot.</summary>
    public bool RobotOnly { get; set; } = true;
    /// <summary>When a Video Clip is in range, frame exactly the area its picture fills, so the robot lines up with the video.</summary>
    public bool MatchVideo { get; set; } = true;

    internal const int MaxSide = 7680;
    internal const string FilePrefix = "frame_";

    public void Validate(double sequenceEnd)
    {
        if (Width < 16 || Height < 16 || Width > MaxSide || Height > MaxSide) throw new InvalidDataException($"Width and height must be 16 to {MaxSide} pixels.");
        if (!double.IsFinite(FramesPerSecond) || FramesPerSecond < 1 || FramesPerSecond > 120) throw new InvalidDataException("Frames per second must be 1 to 120.");
        if (!double.IsFinite(StartSeconds) || StartSeconds < 0) throw new InvalidDataException("Start must be 0 seconds or later.");
        double end = EndFor(sequenceEnd);
        if (!double.IsFinite(end) || end < StartSeconds) throw new InvalidDataException("End must be at or after Start.");
    }

    internal double EndFor(double sequenceEnd) => EndSeconds ?? sequenceEnd;

    /// <summary>Frames from Start through End inclusive, at exact multiples of 1/fps from Start.</summary>
    internal int FrameCount(double sequenceEnd) => (int)Math.Floor((EndFor(sequenceEnd) - StartSeconds) * FramesPerSecond + 1e-6) + 1;

    internal double TimeOfFrame(int index) => ServoCommand.TimeKey(StartSeconds + index / FramesPerSecond);

    internal string FolderFor(string sequencePath, string fallbackFolder)
    {
        if (!string.IsNullOrWhiteSpace(Folder)) return Folder;
        string name = string.IsNullOrWhiteSpace(sequencePath) ? "Unsaved sequence" : Path.GetFileNameWithoutExtension(sequencePath);
        string parent = string.IsNullOrWhiteSpace(sequencePath) ? fallbackFolder : Path.GetDirectoryName(sequencePath);
        return Path.Combine(parent ?? "", name + " frames");
    }

    internal static string FileName(int index) => $"{FilePrefix}{index:D6}.png";

    public RobotFrameExportSettings Clone() => (RobotFrameExportSettings)MemberwiseClone();
}

/// <summary>Remembered Robot Frames settings per sequence, in Configuration\RobotFrameExports.json.</summary>
internal sealed class RobotFrameExportStore
{
    internal const string FileName = "RobotFrameExports.json";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true, DefaultIgnoreCondition = JsonIgnoreCondition.Never };
    public Dictionary<string, RobotFrameExportSettings> Sequences { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Sequences are keyed by their path relative to Configuration (absolute when outside it).</summary>
    internal static string Key(string configRoot, string sequencePath)
    {
        if (string.IsNullOrWhiteSpace(sequencePath)) return "(unsaved)";
        string full = Path.GetFullPath(sequencePath);
        return ConfigPathService.Reference(configRoot, full);
    }

    internal static RobotFrameExportStore Load(string configRoot)
    {
        string path = Path.Combine(configRoot, FileName);
        try
        {
            if (!File.Exists(path)) return new();
            var store = JsonSerializer.Deserialize<RobotFrameExportStore>(File.ReadAllText(path), Json) ?? new();
            var remembered = new Dictionary<string, RobotFrameExportSettings>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in store.Sequences ?? new()) remembered[ConfigPathService.Reference(configRoot, pair.Key)] = pair.Value;
            store.Sequences = remembered;
            foreach (var settings in store.Sequences.Values)
                if (!string.IsNullOrWhiteSpace(settings.Folder)) settings.Folder = ConfigPathService.Resolve(configRoot, settings.Folder);
            return store;
        }
        catch (Exception) { return new(); }   // an unreadable file only loses the remembered choices
    }

    internal void Save(string configRoot)
    {
        Directory.CreateDirectory(configRoot);
        string path = Path.Combine(configRoot, FileName), temp = path + ".tmp";
        File.WriteAllText(temp, ConfigPathService.Serialize(this, configRoot, Json));
        File.Move(temp, path, overwrite: true);
    }

    internal RobotFrameExportSettings For(string configRoot, string sequencePath) =>
        Sequences.TryGetValue(Key(configRoot, sequencePath), out var settings) ? settings.Clone() : new();

    internal void Remember(string configRoot, string sequencePath, RobotFrameExportSettings settings) =>
        Sequences[Key(configRoot, sequencePath)] = settings.Clone();
}
