using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace ServoAnimator;

/// <summary>Runtime models live with Config; authoring/source archives stay separate.</summary>
internal static class ModelStorage
{
    internal const string Robots = "Models/Robots";
    internal const string Scenery = "Models/Scenery";
    internal static string Folder(string root, bool scenery) => Path.Combine(root, scenery ? Scenery : Robots);
    internal static string DefaultRobot(string root) => ModelLocations.DefaultRobot(root);
    internal static string DefaultScenery(string root) => ModelLocations.DefaultScenery(root);
    internal static string PackageTarget(string root, string file, XDocument xml)
    {
        if (ConfigPathService.IsWithin(Folder(root, false), file) || ConfigPathService.IsWithin(Folder(root, true), file))
            return Path.GetRelativePath(root, file);
        // Named scenery remains identifiable even when its joints animate.
        string name = Path.GetFileNameWithoutExtension(file);
        bool scenery = name.Contains("backdrop", StringComparison.OrdinalIgnoreCase) ||
            ((string)xml.Root.Attribute("name") ?? "").Contains("backdrop", StringComparison.OrdinalIgnoreCase);
        string folder = name switch
        {
            "MatrixConstruct_backdrop" => "MatrixConstruct",
            "johnny5_backdrop_only" => "Johnny5Backdrop",
            "johnny5_head" => Path.GetFileName(Path.GetDirectoryName(file)).Equals("FullBodyModel", StringComparison.OrdinalIgnoreCase) ? "Johnny5FullBody" : "Johnny5Head",
            _ => name + "-" + ProjectPackage.ModelIdentity(file)[..12].ToLowerInvariant()
        };
        return Path.Combine(scenery ? Scenery : Robots, folder, Path.GetFileName(file));
    }
    internal static string Serialize<T>(T value, string root, JsonSerializerOptions options)
    {
        return ConfigPathService.Serialize(value, root, options);
    }
    internal static void MakeReferencesRelative(JsonNode node, string root)
    {
        ConfigPathService.MakeReferencesRelative(node, root);
    }
}
