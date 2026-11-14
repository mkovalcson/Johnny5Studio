using System.IO;
using System.Reflection;
using System.Windows;
using ServoAnimator;

internal static partial class Program
{
    private static void PathsAndNamingChecks()
    {
        string root = Path.Combine(Path.GetTempPath(), "J5PathsChecks-" + Guid.NewGuid().ToString("N"));
        string portable = Path.Combine(root, "Portable");
        string adjacent = Path.Combine(portable, "Config");
        string selected = Path.Combine(root, "SelectedConfig");
        string missing = Path.Combine(root, "RemovedWorkspace", "Config");
        Directory.CreateDirectory(adjacent); Directory.CreateDirectory(selected);
        Check(FolderSettings.ResolveConfigFolder(missing, portable) == adjacent, "A stale absolute path recovers to Config beside the executable");
        Check(FolderSettings.ResolveConfigFolder("MissingConfig", portable) == adjacent, "A stale relative path recovers to adjacent Config");
        Check(FolderSettings.ResolveConfigFolder("Config", portable) == adjacent, "Portable Config paths resolve relative to the executable");
        Check(FolderSettings.ResolveConfigFolder(selected, portable) == selected, "An existing selected Configuration folder remains selected");
        Check(FolderSettings.ResolveConfigFolder("", portable) == adjacent, "Unset paths prefer adjacent Config");
        string emptyApp = Path.Combine(root, "EmptyApp"); Directory.CreateDirectory(emptyApp);
        Check(FolderSettings.ResolveConfigFolder(missing, emptyApp) == emptyApp, "When no Config is discoverable, the executable folder remains the final fallback");
        string dev = Path.Combine(root, "Development");
        string devConfig = Path.Combine(dev, "Config");
        string devApp = Path.Combine(dev, "ServoAnimator", "bin", "Release", "net10.0-windows");
        Directory.CreateDirectory(devConfig); Directory.CreateDirectory(devApp);
        Check(FolderSettings.ResolveConfigFolder(missing, devApp) == devConfig, "Development builds retain their workspace configuration");
        string devAdjacent = Path.Combine(devApp, "Config"); Directory.CreateDirectory(devAdjacent);
        Check(FolderSettings.ResolveConfigFolder(missing, devApp) == devAdjacent, "Adjacent Config takes precedence over development discovery");
        var settings = new FolderSettings { ConfigFolder = missing, LastSequenceFolder = Path.Combine("Projects", "MyProject") };
        typeof(FolderSettings).GetMethod("NormalizeLoaded", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { settings });
        Check(Directory.Exists(settings.ConfigFolder), "Loading stale saved paths normalizes the active Configuration folder");
        Check(settings.ProjectFolder == Path.Combine(settings.ConfigFolder, "Projects") && settings.LastSequenceFolder == Path.Combine(settings.ConfigFolder, "Projects", "MyProject"),
            "Project and recent-sequence paths follow the recovered Configuration folder");

        var app = new App(); app.InitializeComponent(); DisableTestStartup(app);
        var main = new MainWindow();
        Check(main.Title.StartsWith("J5 Studio Desktop"), "Main window uses the J5 application name");
        var help = new HelpWindow();
        Check(help.Title.StartsWith("J5 Studio Desktop Help"), "Help navigation retains the J5 application name");
        var model = new UrdfModelWindow();
        Check(model.Title == "URDF Model", "Detached model window has the requested title");
        bool dockRequested = false; model.DockRequested += () => dockRequested = true;
        model.Close();
        Check(dockRequested, "Closing the renamed model window still requests docking");
        model.ForceClose = true; model.Close(); help.Close();
        Check(!main.IsLoaded, "Verification leaves the running editor and hardware untouched");
        // Only remove this uniquely named, test-owned fixture below the OS temp folder.
        Check(Path.GetDirectoryName(root) == Path.TrimEndingDirectorySeparator(Path.GetTempPath()), "Test cleanup stays inside the temporary directory");
        Directory.Delete(root, true);
    }
}
