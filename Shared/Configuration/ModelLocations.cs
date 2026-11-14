using System.IO;
namespace ServoAnimator;

public static class ModelLocations
{
    public const string Robots = "Models/Robots";
    public const string Scenery = "Models/Scenery";
    public static string Folder(string root, bool scenery) => Path.Combine(root, scenery ? Scenery : Robots);
    public static string DefaultRobot(string root)
    {
        foreach (string relative in new[] { "Models/Robots/Johnny5FullBody/johnny5_head.urdf", "Models/Robots/Johnny5Head/johnny5_head.urdf" })
        {
            string file = Path.Combine(root, relative);
            if (File.Exists(file)) return file;
        }
        string legacy = Path.Combine(AppContext.BaseDirectory, "Models", "johnny5_head.urdf");
        return File.Exists(legacy) ? legacy : Path.Combine(AppContext.BaseDirectory, "johnny5_head.urdf");
    }
    public static string DefaultScenery(string root)
    {
        string file = Path.Combine(root, "Models/Scenery/MatrixConstruct/MatrixConstruct_backdrop.urdf");
        return File.Exists(file) ? file : Path.Combine(AppContext.BaseDirectory, "Models", "MatrixConstruct_backdrop.urdf");
    }
}
