using System.IO;
using System.Text.Json;

namespace ServoAnimator;

public sealed class BackgroundLibraryItem
{
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
    public string RobotModelFile { get; set; } = "";
    public BackdropCommand Settings { get; set; } = new();
    [System.Text.Json.Serialization.JsonIgnore]
    public string FileName { get; set; }
    public override string ToString() => string.IsNullOrWhiteSpace(Category) ? Name : $"{Category} / {Name}";
}

public static class BackgroundLibrary
{
    public static string Folder(string root, bool scenes = false) => Path.Combine(root, scenes ? "LibraryScenes" : "LibraryBackgrounds");
    public static BackgroundLibraryItem Read(string root, string file, bool forUse = true)
    {
        string path = Path.IsPathRooted(file) ? file : Path.Combine(root, file);
        var item = ConfigurationStore.ReadScene(root, path);
        if (item.Settings == null) throw new InvalidDataException("Background settings are missing.");
        if (forUse) item.Settings.Background?.EnsureLocalImage(root);
        item.Settings.Validate(root);
        if (!string.IsNullOrWhiteSpace(item.RobotModelFile))
        {
            item.RobotModelFile = Path.GetFullPath(Path.IsPathRooted(item.RobotModelFile) ? item.RobotModelFile : Path.Combine(root, item.RobotModelFile));
            if (!File.Exists(item.RobotModelFile)) throw new FileNotFoundException("Scene robot model is missing.", item.RobotModelFile);
        }
        item.FileName = Path.GetRelativePath(root, path);
        return item;
    }
    public static List<BackgroundLibraryItem> List(string root, out string errors, bool scenes = false)
    {
        var items = new List<BackgroundLibraryItem>(); var failures = new List<string>();
        if (Directory.Exists(Folder(root, scenes)))
            foreach (var file in Directory.GetFiles(Folder(root, scenes), "*.json"))
                try { items.Add(Read(root, file, forUse: false)); }
                catch (Exception ex) { failures.Add($"{Path.GetFileName(file)}: {ex.Message}"); }
        errors = string.Join(Environment.NewLine, failures);
        return items.OrderBy(i => i.Category).ThenBy(i => i.Name).ToList();
    }
    // A reusable background must not carry a robot pose or camera snapshot.
    public static BackdropCommand SceneryOnly(BackdropCommand source)
    {
        var result = BackdropCommand.Read(source.ToJson()); result.Framing = null;
        if (result.Background.Stage != null)
        {
            result.Background.Stage.RobotFraming = null;
            result.Background.Stage.RobotX = result.Background.Stage.RobotY = result.Background.Stage.HeadingDegrees = 0;
        }
        return result;
    }
    public static void Save(string root, BackgroundLibraryItem item, bool scenes = false)
    {
        if (string.IsNullOrWhiteSpace(item.Name)) throw new InvalidDataException("Enter a background name.");
        if (scenes && (string.IsNullOrWhiteSpace(item.RobotModelFile) || !File.Exists(item.RobotModelFile)))
            throw new InvalidDataException("Choose an existing robot URDF model for the scene.");
        if (!scenes)
        {
            item.RobotModelFile = "";
            item.Settings = SceneryOnly(item.Settings);
        }
        if (scenes && item.Settings.Framing == null) throw new InvalidDataException("A complete scene requires robot framing settings.");
        item.Settings.Background?.EnsureLocalImage(root);
        item.Settings.Validate(root);
        Directory.CreateDirectory(Folder(root, scenes));
        // Stable filenames keep sequence references intact when names/categories change.
        if (item.FileName == null)
        {
            string name = string.Concat(item.Name.Trim().Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).TrimEnd('.', ' ');
            if (string.IsNullOrWhiteSpace(name)) name = "Background";
            // Prefix avoids reserved Windows device names (CON, NUL, etc.).
            string leaf = (scenes ? "Scene-" : "Background-") + name;
            if (leaf.Length > 100) leaf = leaf[..100];
            string candidate = leaf + ".json";
            for (int n = 2; File.Exists(Path.Combine(Folder(root, scenes), candidate)); n++) candidate = leaf + "-" + n + ".json";
            item.FileName = Path.Combine(scenes ? "LibraryScenes" : "LibraryBackgrounds", candidate);
        }
        string path = Path.Combine(root, item.FileName);
        File.WriteAllText(path + ".tmp", ConfigurationPaths.Serialize(item, root, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(path + ".tmp", path, true);
    }
}
