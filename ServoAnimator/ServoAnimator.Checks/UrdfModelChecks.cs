using System.Reflection;
using System.Windows;
using ServoAnimator;

internal static partial class Program
{
    private static void UrdfModelChecks()
    {
        var app = new App(); app.InitializeComponent(); DisableTestStartup(app); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(Application).GetField("_startupUri", flags).SetValue(app, null);
        var view = new RobotHeadView();
        var original = view.CurrentModelScene; var originalPath = view.LoadedModelFile; var frame = view.CaptureFraming();
        string path = System.IO.Path.GetFullPath("ServoAnimator/FullBodyModel/johnny5_head.urdf");
        var body = UrdfScene.Load(path);
        view.InstallModel(path, body, new UrdfFraming { VerticalPercent = 40 });
        Check(view.LoadedModelFile == path && ReferenceEquals(view.CurrentModelScene, body), "Full body replaces the active scene immediately");
        Check(view.CaptureFraming().VerticalPercent == 40, "Framing applies to newly loaded model");
        var viewport = (System.Windows.Controls.Viewport3D)typeof(RobotHeadView).GetField("_viewport", flags).GetValue(view);
        Check(viewport.Children.Count == 1, "Replacement does not stack old and new scenes");
        view.InstallModel(originalPath, original, frame);
        Check(ReferenceEquals(view.CurrentModelScene, original) && view.CaptureFraming().VerticalPercent == frame.VerticalPercent, "Previous model and framing restore");
        Check(!view.IsLoaded, "Model checks never activate the editor or hardware");
    }
}

