using System.IO;
using System.Text.Json;

namespace ServoAnimator;

/// <summary>Explicit Config-root entry points for applications that host multiple scenes/models.</summary>
public static class ConfigurationStore
{
    public static ServoConfiguration LoadServos(string root)
    {
        string file = Path.Combine(root, "ServoConfig.json");
        return File.Exists(file) ? ServoConfiguration.Load(file) : ServoConfiguration.CreateDefault();
    }

    public static UrdfConfiguration LoadUrdf(string root, string modelFile = null)
    {
        using var context = ConfigurationContext.Use(root);
        modelFile ??= UrdfModelSettings.Load(root).SelectedFile;
        string file = Path.Combine(root, "URDFconfig.json");
        var settings = File.Exists(file) ? UrdfConfiguration.Load(file, modelFile) : UrdfConfiguration.CreateDefault(modelFile);
        // Missing stage settings are resolved relative to this Config, not another host's Paths.json.
        if (!File.Exists(file)) settings.Presentation.Stage.ModelFile = ModelLocations.DefaultScenery(root);
        return settings;
    }

    // Read scene data without applying a desktop camera or importing images during VR playback.
    // Editor-side Read/Validate adds the existing complete-scene and asset validation.
    public static BackgroundLibraryItem ReadScene(string root, string reference)
    {
        using var context = ConfigurationContext.Use(root);
        string file = ConfigurationPaths.ResolveAsset(root, reference);
        return JsonSerializer.Deserialize<BackgroundLibraryItem>(File.ReadAllText(file), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        }) ?? throw new InvalidDataException("The scene file is empty.");
    }
}
