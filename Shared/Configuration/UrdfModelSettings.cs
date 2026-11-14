using System.IO;
using System.Text.Json;

namespace ServoAnimator;

public sealed class UrdfFraming
{
    public UrdfFraming Clone() => (UrdfFraming)MemberwiseClone();
    public double HorizontalPercent { get; set; }
    public double VerticalPercent { get; set; }
    public double SpinDegrees { get; set; }
    public double RotateDegrees { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public double RotatePercent { get => 0; set => RotateDegrees = Math.Clamp(value * 3.6, -360, 360); }
    public double Yaw { get; set; }
    public double Pitch { get; set; }
    public double Distance { get; set; } = 1.15;
}

public sealed class UrdfModelSettings
{
    public string ActiveSceneFile { get; set; } = "";
    public string ModelFile { get; set; } = "";
    public Dictionary<string, UrdfFraming> Framing { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public static string DefaultModelFile
    {
        get
        {
            return ModelLocations.DefaultRobot(FolderSettings.Load()?.ConfigFolderOrDefault ?? AppContext.BaseDirectory);
        }
    }
    [System.Text.Json.Serialization.JsonIgnore]
    public string SelectedFile => string.IsNullOrWhiteSpace(ModelFile) ? DefaultModelFile : Path.GetFullPath(ModelFile);
    public static UrdfModelSettings Load(string root)
    {
        string path = Path.Combine(root, "UrdfModelSettings.json");
        var settings = File.Exists(path) ? JsonSerializer.Deserialize<UrdfModelSettings>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip }) ?? new() : new();
        if (!string.IsNullOrWhiteSpace(settings.ModelFile))
            settings.ModelFile = Path.GetFullPath(Path.Combine(root, settings.ModelFile));
        else settings.ModelFile = ModelLocations.DefaultRobot(root);
        // Older saves can hold the same model under both a relative and an absolute key; both
        // resolve to one full path, so merge them (the later entry wins) instead of failing.
        var framing = new Dictionary<string, UrdfFraming>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in settings.Framing ?? new())
            framing[Path.GetFullPath(Path.Combine(root, entry.Key))] = entry.Value;
        settings.Framing = framing;
        return settings;
    }
    public void Save(string root)
    {
        string path = Path.Combine(root, "UrdfModelSettings.json");
        var stored = new UrdfModelSettings
        {
            ActiveSceneFile = ConfigurationPaths.Reference(root, ActiveSceneFile),
            ModelFile = string.IsNullOrWhiteSpace(ModelFile) ? "" : Path.GetRelativePath(root, SelectedFile),
            Framing = Framing.ToDictionary(entry => Path.GetRelativePath(root, entry.Key), entry => entry.Value, StringComparer.OrdinalIgnoreCase),
        };
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(stored, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(path + ".tmp", path, true);
    }
}
