using System.Text.Json;
using System.Text.Json.Nodes;

namespace ServoAnimator;

// App-facing compatibility facade. Document/UI-specific work stays in this application.
internal static partial class ConfigPathService
{
    internal static string CurrentRoot => ConfigurationPaths.CurrentRoot;
    internal static string Resolve(string root, string path) => ConfigurationPaths.Resolve(root, path);
    internal static string Reference(string root, string path) => ConfigurationPaths.Reference(root, path);
    internal static string Display(string path) => ConfigurationPaths.Display(path);
    internal static string DisplayCommand(ServoNames servo, string text) => ConfigurationPaths.DisplayCommand(servo, text);
    internal static string RootFor(string file) => ConfigurationPaths.RootFor(file);
    internal static string Serialize<T>(T value, string root, JsonSerializerOptions options) => ConfigurationPaths.Serialize(value, root, options);
    internal static string SerializeFile<T>(T value, string file, JsonSerializerOptions options) => ConfigurationPaths.SerializeFile(value, file, options);
    internal static bool NormalizeLibraryImage(JsonNode node, string root, string owner) => ConfigurationPaths.NormalizeLibraryImage(node, root, owner);
    internal static bool MakeReferencesRelative(JsonNode node, string root, string context = "") => ConfigurationPaths.MakeReferencesRelative(node, root, context);
}
