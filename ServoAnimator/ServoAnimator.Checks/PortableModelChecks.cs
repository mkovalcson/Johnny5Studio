using System.IO;
using System.Text.Json;
using ServoAnimator;

internal static partial class Program
{
    private static void PortableModelChecks(string packagePath)
    {
        string package = Path.GetFullPath(packagePath), root = Path.Combine(package, "Config");
        var settings = UrdfModelSettings.Load(root);
        string managedRobot = Path.Combine(root, "Models", "Robots", "Johnny5FullBody", "johnny5_head.urdf");
        Check(settings.SelectedFile == (File.Exists(managedRobot) ? managedRobot : Path.Combine(package, "FullBodyModel", "johnny5_head.urdf")), "Packaged robot defaults resolve relative to Config regardless of the current directory");
        Check(settings.Framing.ContainsKey(settings.SelectedFile), "Relative per-model framing keys match the resolved robot path");
        Check(settings.ActiveSceneFile == "", "Excluded Sylogist scene is not selected at startup");
        var scene = UrdfScene.Load(settings.SelectedFile);
        Check(scene != null && scene.RootModel.Children.Count > 0, "Full-body model loads entirely from the packaged files");
        var backdrop = BackgroundLibrary.Read(root, "LibraryBackgrounds/Background-Matrix Construct.json");
        backdrop.Settings.Validate(root);
        var stage = backdrop.Settings.Background.Stage;
        string managedScenery = Path.Combine(root, "Models", "Scenery", "MatrixConstruct", "MatrixConstruct_backdrop.urdf");
        Check(stage.ResolveModelFile(root) == (File.Exists(managedScenery) ? managedScenery : Path.Combine(package, "Models", "MatrixConstruct_backdrop.urdf")), "Packaged stage paths resolve from Config");
        Check(UrdfScene.Load(stage.ResolveModelFile(root)) != null, "Bundled backdrop model and meshes load");
        var savedScene = BackgroundLibrary.Read(root, "LibraryScenes/Scene-Max Headroom.json");
        Check(savedScene.RobotModelFile == settings.SelectedFile, "Scene robot references resolve against the same configuration root");
        Check(savedScene.Settings.Background.CreateBrush(root, System.Windows.Media.Brushes.Black) != null, "Remaining scene images load from the package");
        string temporary = Path.Combine(package, "verification-fixture"); Directory.CreateDirectory(temporary);
        var defaults = new UrdfModelSettings { ModelFile = settings.SelectedFile, Framing = settings.Framing };
        defaults.Save(temporary);
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(temporary, "UrdfModelSettings.json")));
        Check(!Path.IsPathRooted(json.RootElement.GetProperty("ModelFile").GetString()) && !json.RootElement.TryGetProperty("SelectedFile", out _), "Saved robot defaults use relative paths and omit the redundant absolute SelectedFile");
        var reread = UrdfModelSettings.Load(temporary);
        Check(reread.SelectedFile == settings.SelectedFile && reread.Framing.ContainsKey(settings.SelectedFile), "Relative model paths and framing survive save/reopen");
        // The fixture is test-owned and remains inside the explicitly selected package.
        Check(Path.GetDirectoryName(temporary) == package, "Verification cleanup stays inside its package fixture");
        Directory.Delete(temporary, true);
    }
}
