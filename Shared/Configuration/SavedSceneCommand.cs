using System.IO;
using System.Text.Json;

namespace ServoAnimator;

public static class SavedSceneCommand
{
    public static BackgroundLibraryItem Read(string root, string file)
    {
        string path = Path.GetFullPath(Path.IsPathRooted(file) ? file : Path.Combine(root, file));
        var item = ConfigurationStore.ReadScene(root, path);
        if (item.Settings?.Framing == null || string.IsNullOrWhiteSpace(item.RobotModelFile))
            throw new InvalidDataException("Choose a complete saved scene containing a robot model, background and framing.");
        if (!string.IsNullOrWhiteSpace(item.Settings.SceneFile))
            throw new InvalidDataException("A saved scene must contain settings, not another scene reference.");
        item.Settings.Background?.EnsureLocalImage(root);
        item.Settings.Validate(root);
        item.RobotModelFile = Path.GetFullPath(Path.IsPathRooted(item.RobotModelFile)
            ? item.RobotModelFile : Path.Combine(root, item.RobotModelFile));
        if (!File.Exists(item.RobotModelFile)) throw new FileNotFoundException("Scene robot model is missing.", item.RobotModelFile);
        item.FileName = ConfigurationPaths.IsWithin(root, path) ? Path.GetRelativePath(root, path) : path;
        return item;
    }
}
