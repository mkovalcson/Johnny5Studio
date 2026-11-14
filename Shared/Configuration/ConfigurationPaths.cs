using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ServoAnimator;

public static class ConfigurationPaths
{
    public static bool IsWithin(string configFolder, string path)
    {
        if (string.IsNullOrWhiteSpace(configFolder) ||
            string.IsNullOrWhiteSpace(path))
            return false;

        try
        {
            string root = Path.GetFullPath(configFolder)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string full = Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(full, root, StringComparison.OrdinalIgnoreCase) ||
                   full.StartsWith(root + Path.DirectorySeparatorChar,
                       StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public static bool TryResolve(string configFolder, string storedPath,
                                      out string fullPath)
    {
        fullPath = null;
        if (string.IsNullOrWhiteSpace(configFolder) ||
            string.IsNullOrWhiteSpace(storedPath))
            return false;

        try
        {
            string candidate = Path.IsPathRooted(storedPath)
                ? storedPath
                : Path.Combine(configFolder, storedPath);
            candidate = Path.GetFullPath(candidate);
            if (!IsWithin(configFolder, candidate)) return false;
            fullPath = candidate;
            return true;
        }
        catch { return false; }
    }

    public static string ToRelative(string configFolder, string path)
    {
        if (!TryResolve(configFolder, path, out string fullPath))
            throw new InvalidOperationException(
                "The selected path must be inside the Configuration folder.");
        return Path.GetRelativePath(Path.GetFullPath(configFolder), fullPath);
    }

    public static string CurrentRoot => FolderSettings.Load()?.ConfigFolderOrDefault ?? AppContext.BaseDirectory;
    // Resolved paths are for file access; displayed and saved paths are relative to Config.
    public static string Resolve(string root, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        if (path.StartsWith("file://", StringComparison.OrdinalIgnoreCase)) path = new Uri(path).LocalPath;
        return Path.GetFullPath(Path.Combine(root, path));
    }
    public static string Reference(string root, string path)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root)) return path ?? "";
        try
        {
            if (!Path.IsPathFullyQualified(path) && !path.StartsWith("file://", StringComparison.OrdinalIgnoreCase)) return path;
            return Path.GetRelativePath(Path.GetFullPath(root), Resolve(root, path));
        }
        catch { return path; }
    }
    public static string Display(string path) => Reference(CurrentRoot, path);
    public static string DisplayCommand(ServoNames servo, string text)
    {
        if (servo == ServoNames.Play) return Display(text);
        if (string.IsNullOrWhiteSpace(text) || !text.TrimStart().StartsWith('{')) return text;
        try { var json = JsonNode.Parse(text); return MakeReferencesRelative(json, CurrentRoot) ? json.ToJsonString() : text; }
        catch (JsonException) { return text; }
    }
    public static string RootFor(string file)
    {
        string current = CurrentRoot;
        if (IsWithin(current, file)) return current;
        for (var folder = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(file))); folder != null; folder = folder.Parent)
            if (folder.Name.Equals("Config", StringComparison.OrdinalIgnoreCase) || File.Exists(Path.Combine(folder.FullName, "ServoConfig.json"))) return folder.FullName;
        return null;
    }
    private static readonly HashSet<string> PathFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "ModelFile", "RobotModelFile", "SelectedFile", "SceneFile", "UrdfBackgroundFile", "BackgroundImage", "Image", "ImageFile", "Sound", "Path",
        "AudioFilePath", "FilePath", "SequencePath", "MoviePath", "LastActivePath", "LastSequenceFolder", "Folder", "ProjectFolder", "ActiveSceneFile", "Sequence"
    };
    public static string Serialize<T>(T value, string root, JsonSerializerOptions options)
    {
        if (string.IsNullOrWhiteSpace(root)) return JsonSerializer.Serialize(value, options);
        var json = JsonSerializer.SerializeToNode(value, options);
        MakeReferencesRelative(json, root);
        return json.ToJsonString(options);
    }
    public static string SerializeFile<T>(T value, string file, JsonSerializerOptions options)
    {
        string root = RootFor(file);
        if (root == null) return JsonSerializer.Serialize(value, options);
        var json = JsonSerializer.SerializeToNode(value, options);
        NormalizeLibraryImage(json, root, file);
        MakeReferencesRelative(json, root);
        return json.ToJsonString(options);
    }
    public static bool NormalizeLibraryImage(JsonNode node, string root, string owner)
    {
        if (node is not JsonObject obj || !obj.Any(p => p.Key.Equals("commands", StringComparison.OrdinalIgnoreCase))) return false;
        string key = obj.Select(p => p.Key).FirstOrDefault(k => k.Equals("image", StringComparison.OrdinalIgnoreCase));
        if (key == null || obj[key] is not JsonValue value || !value.TryGetValue<string>(out string image) || string.IsNullOrWhiteSpace(image)) return false;
        // Older pose images were relative to the pose JSON.
        string local = Resolve(Path.GetDirectoryName(owner), image);
        string full = File.Exists(local) ? local : Resolve(root, image);
        string relative = Reference(root, full);
        if (relative == image) return false;
        obj[key] = relative; return true;
    }
    public static bool MakeReferencesRelative(JsonNode node, string root, string context = "")
    {
        bool changed = false;
        if (node is JsonObject obj)
        {
            bool play = obj.Any(p => p.Key.Equals("servo", StringComparison.OrdinalIgnoreCase) && p.Value is JsonValue v && v.TryGetValue<string>(out string s) && s.Equals("Play", StringComparison.OrdinalIgnoreCase));
            foreach (var pair in obj.ToArray())
            {
                string key = pair.Key;
                if (context.Equals("Framing", StringComparison.OrdinalIgnoreCase) || context.Equals("Sequences", StringComparison.OrdinalIgnoreCase))
                {
                    string relative = Reference(root, key);
                    if (relative != key) { var child = pair.Value; obj.Remove(key); obj[relative] = child; key = relative; changed = true; }
                }
                if (pair.Value is JsonValue value && value.TryGetValue<string>(out string text))
                {
                    if (PathFields.Contains(key) || play && key.Equals("value", StringComparison.OrdinalIgnoreCase))
                    {
                        string relative = Reference(root, text);
                        if (relative != text) { obj[key] = relative; changed = true; }
                    }
                    else if (key.Equals("value", StringComparison.OrdinalIgnoreCase) || key.Equals("TextValue", StringComparison.OrdinalIgnoreCase))
                    {
                        JsonNode embedded = null;
                        if (text.TrimStart().StartsWith('{') || text.TrimStart().StartsWith('['))
                            try { embedded = JsonNode.Parse(text); } catch (JsonException) { }
                        if (embedded != null && MakeReferencesRelative(embedded, root)) { obj[key] = embedded.ToJsonString(); changed = true; }
                    }
                }
                else changed |= MakeReferencesRelative(pair.Value, root, key);
            }
        }
        else if (node is JsonArray array)
            for (int i = 0; i < array.Count; i++)
            {
                if (context.Equals("Sequences", StringComparison.OrdinalIgnoreCase) && array[i] is JsonValue value && value.TryGetValue<string>(out string text))
                { string relative = Reference(root, text); if (relative != text) { array[i] = relative; changed = true; } }
                else changed |= MakeReferencesRelative(array[i], root);
            }
        return changed;
    }
    public static string ResolveAsset(string config, string reference)
    {
        reference = reference.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        // Recover old machine-specific paths when they still identify a Config asset.
        int at = reference.IndexOf("\\Config\\", StringComparison.OrdinalIgnoreCase);
        if (at >= 0) reference = reference[(at + 8)..];
        if (reference.StartsWith("Config\\", StringComparison.OrdinalIgnoreCase)) reference = reference[7..];
        if (Path.IsPathRooted(reference))
        {
            if (File.Exists(reference)) return Path.GetFullPath(reference);
            throw new FileNotFoundException("Asset is on another computer: " + reference);
        }
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(config)) + Path.DirectorySeparatorChar;
        string result = Path.GetFullPath(Path.Combine(root, reference));
        if (!result.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Asset path leaves Config: " + reference);
        return result;
    }
}
