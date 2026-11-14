using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using ServoAnimator;

internal static partial class Program
{
    private static IEnumerable<DependencyObject> SceneDescendants(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var descendant in SceneDescendants(child)) yield return descendant;
    }
    private static void SceneSetupChecks()
    {
        var app = new App(); app.InitializeComponent(); DisableTestStartup(app);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        string output = PreviewDirectory("scene-setup-previews"); Directory.CreateDirectory(output);
        string root = Path.Combine(output, "checks-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        string robot = Path.Combine(root, "robot.urdf");
        File.WriteAllText(robot, "<robot name='test'><link name='base'><visual><geometry><box size='0.2 0.2 0.2'/></geometry></visual></link></robot>");
        var setup = new BackdropCommand { Background = new() { BackgroundMode = UrdfBackgroundMode.Color, BackgroundColor = "#004422" },
            Framing = new() { HorizontalPercent = 13, VerticalPercent = 27, SpinDegrees = 1080, Distance = 2 } };
        setup.Background.Stage.RobotX = 7; setup.Background.Stage.RobotFraming = setup.Framing.Clone();
        var background = new BackgroundLibraryItem { Name = "Scenery", Category = "Test", Description = "Scenery only", Settings = BackdropCommand.Read(setup.ToJson()) };
        BackgroundLibrary.Save(root, background);
        var loadedBackground = BackgroundLibrary.Read(root, background.FileName);
        Check(loadedBackground.Settings.Framing == null && loadedBackground.Settings.Background.Stage.RobotFraming == null, "Background file excludes robot framing");
        Check(loadedBackground.Settings.Background.Stage.RobotX == 0 && loadedBackground.RobotModelFile == "", "Background excludes robot model and position");
        var scene = new BackgroundLibraryItem { Name = "Whole scene", Category = "Test", Description = "Complete setup", RobotModelFile = robot, Settings = setup };
        BackgroundLibrary.Save(root, scene, true);
        var loaded = BackgroundLibrary.Read(root, scene.FileName);
        Check(loaded.RobotModelFile == robot && loaded.Settings.Framing.VerticalPercent == 27 && loaded.Settings.Framing.SpinDegrees == 1080, "Complete scene retains robot and full framing");
        Check(loaded.Settings.Background.Stage.RobotX == 7, "Scene retains room position");
        string stableFile = scene.FileName; scene.Name = "Renamed scene"; BackgroundLibrary.Save(root, scene, true);
        Check(scene.FileName == stableFile, "Rename preserves sequence reference");
        Check(BackgroundLibrary.List(root, out _, true).Count == 1 && BackgroundLibrary.List(root, out _).Count == 1, "Scene and background lists are separate");
        var doc = new AnimationDocument { UrdfBackgroundFile = scene.FileName }; string sequence = Path.Combine(root, "sequence.json"); doc.Save(sequence);
        Check(AnimationDocument.Load(sequence).UrdfBackgroundFile == scene.FileName && doc.Clone().UrdfBackgroundFile == scene.FileName, "Sequence assignment survives save and clone");

        foreach (var kind in Enum.GetValues<ControllerKind>())
        foreach (bool noMux in kind == ControllerKind.Steam ? new[] { false, true } : new[] { false })
        {
            var diagram = new ControllerDiagram(kind) { NoMux = noMux };
            RenderControl(diagram, Path.Combine(output, kind + (noMux ? "-NoMux" : "") + ".png"), (int)Math.Ceiling(diagram.Width), (int)Math.Ceiling(diagram.Height));
            var anchors = (Dictionary<string, Point>)typeof(ControllerDiagram).GetField("_anchors", flags).GetValue(diagram);
            foreach (var input in ControllerCatalog.Inputs(kind, noMux)) Check(anchors.ContainsKey(input.Id), $"{kind}/{noMux}: anchor for {input.Id}");
            if (kind == ControllerKind.Xbox)
            {
                var hits = (Dictionary<Rect, string>)typeof(ControllerDiagram).GetField("_hits", flags).GetValue(diagram);
                double CardY(string id) => hits.Single(p => p.Value == id && p.Key.Width > 100).Key.Y;
                Check(CardY("Back") < CardY("LeftClick"), "View precedes stick click");
                Check(CardY("Start") < CardY("Y"), "Menu precedes Y");
                Check(CardY("Share") > CardY("RightY"), "Share follows right stick Y");
                Check(Math.Abs(anchors["DpadUp"].X - anchors["DpadDown"].X) < .01, "D-pad vertical arrows align");
                Check(Math.Abs(anchors["DpadLeft"].Y - anchors["DpadRight"].Y) < .01, "D-pad horizontal arrows align");
            }
        }
        foreach (string field in new[] { "XboxFace", "XboxShoulders", "SteamFace", "SteamShoulders", "SteamUnderside" })
        {
            var lazy = typeof(ControllerDiagram).GetField(field, BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            Check(lazy.GetType().GetProperty("Value").GetValue(lazy) is ImageSource, "Embedded artwork loaded: " + field);
        }
        var window = new MainWindow(); // Never shown; no hardware polling or output.
        typeof(MainWindow).GetField("_folders", flags).SetValue(window, new FolderSettings { ConfigFolder = root });
        object Call(string method, params object[] args) => typeof(MainWindow).GetMethod(method, flags).Invoke(window, args);
        var view = (RobotHeadView)window.FindName("EmbeddedHeadView");
        Call("ApplyLibraryScene", scene);
        Check(view.LoadedModelFile == robot && view.CaptureCurrentFraming().VerticalPercent == 27, "Scene loads robot and framing");
        Call("ApplyLibraryBackground", loadedBackground);
        Check(view.LoadedModelFile == robot && view.CaptureCurrentFraming().VerticalPercent == 27 && view.CaptureCurrentFraming().SpinDegrees == 1080, "Background load preserves robot and framing");
        string secondRobot = Path.Combine(root, "second-robot.urdf"); File.Copy(robot, secondRobot);
        var secondScene = new BackgroundLibraryItem { Name = "Second", RobotModelFile = secondRobot, Settings = BackdropCommand.Read(setup.ToJson()) };
        secondScene.Settings.Framing.VerticalPercent = -11; BackgroundLibrary.Save(root, secondScene, true);
        typeof(MainWindow).GetField("_doc", flags).SetValue(window, new AnimationDocument { UrdfBackgroundFile = secondScene.FileName });
        Call("ApplyBackdropTimeline");
        Check(view.LoadedModelFile == secondRobot && view.CaptureCurrentFraming().VerticalPercent == -11, "Sequence scene installs its robot and camera");
        typeof(MainWindow).GetField("_doc", flags).SetValue(window, new AnimationDocument()); Call("ApplyBackdropTimeline");
        Check(view.LoadedModelFile == robot && view.CaptureCurrentFraming().VerticalPercent == 27, "Leaving sequence scene restores default robot and camera");
        Call("AssignSequenceScene", secondScene); Call("UndoSteps", 1);
        Check(((AnimationDocument)typeof(MainWindow).GetField("_doc", flags).GetValue(window)).UrdfBackgroundFile == "", "Undo restores sequence scene assignment");
        var menus = SceneDescendants(window).OfType<MenuItem>().ToArray();
        var fileMenu = menus.First(m => Equals(m.Header, "_File")); var configMenu = menus.First(m => Equals(m.Header, "_Config"));
        Check(!fileMenu.Items.OfType<MenuItem>().Any(m => Equals(m.Header, "Enable Codex Connection")) && configMenu.Items.OfType<MenuItem>().Any(m => Equals(m.Header, "Enable Codex Connection")), "Connection option moved to Config");
        Check(configMenu.Items.OfType<MenuItem>().Any(m => Equals(m.Header, "Servo Speed Calibration…")), "Servo Speed Calibration label");
        new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle(); // Create an owner handle without Loaded/hardware activation.
        Exception uiFailure = null; bool opened = false;
        var inspection = new DispatcherFrame();
        window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            Window dialog = app.Windows.OfType<Window>().FirstOrDefault(w => w.Title == "URDF Scene Setup");
            try
            {
                Check(dialog != null, "Scene Setup opens"); opened = true;
                var descendants = SceneDescendants(dialog).ToArray();
                var tabs = descendants.OfType<TabControl>().Single();
                Check(tabs.Items.Count == 5 && tabs.Items.Cast<TabItem>().Select(t => t.Header?.ToString()).SequenceEqual(new[] { "Robot", "Background", "Robot Position", "Camera", "Animation" }), "Five task-focused scene tabs");
                foreach (var button in descendants.OfType<Button>())
                    Check(button.ToolTip is ToolTip tip && !tip.IsHitTestVisible && tip.PlacementTarget == dialog && tip.HorizontalOffset >= 20, "Side tooltip: " + button.Content);
                for (int i = 0; i < 5; i++)
                {
                    tabs.SelectedIndex = i;
                    dialog.UpdateLayout();
                    var content = (FrameworkElement)dialog.Content;
                    RenderControl(content, Path.Combine(output, "Scene-tab-" + i + ".png"), (int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight));
                }
                foreach (string label in new[] { "Apply", "Save Scene", "Save Scene As…", "Close" })
                {
                    var action = SceneDescendants(dialog).OfType<Button>().Single(b => Equals(b.Content, label));
                    var point = action.TransformToAncestor((FrameworkElement)dialog.Content).Transform(new Point());
                    Check(point.Y >= 0 && point.Y + action.ActualHeight <= ((FrameworkElement)dialog.Content).ActualHeight + .1, "Footer action remains visible: " + label);
                }
                var sceneFiles = Directory.GetFiles(BackgroundLibrary.Folder(root, true)).ToDictionary(f => f, File.ReadAllText);
                var commandsBefore = ((AnimationDocument)typeof(MainWindow).GetField("_doc", flags).GetValue(window)).Commands.Count;
                Slider HorizontalSlider(Window setupWindow)
                {
                    var label = SceneDescendants(setupWindow).OfType<TextBlock>().Single(t => t.Text == "Horizontal position (%)");
                    return ((Panel)label.Parent).Children.OfType<Slider>().Single();
                }
                Button SetupButton(Window setupWindow, string label) => SceneDescendants(setupWindow).OfType<Button>().Single(b => Equals(b.Content, label));
                void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                void CloseWithChoice(Window setupWindow, string label)
                {
                    setupWindow.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                    {
                        var prompt = app.Windows.OfType<ScenePreviewCloseWindow>().Single();
                        Click(SetupButton(prompt, label));
                    }));
                    setupWindow.Close();
                }
                HorizontalSlider(dialog).Value = 41;
                Click(SetupButton(dialog, "Apply"));
                Check(view.CaptureCurrentFraming().HorizontalPercent == 41 && UrdfModelSettings.Load(root).Framing[robot].HorizontalPercent == 41, "Apply keeps and persists current framing without a scene save");
                Check(sceneFiles.All(f => File.ReadAllText(f.Key) == f.Value) && Directory.GetFiles(BackgroundLibrary.Folder(root, true)).Length == sceneFiles.Count, "Apply creates or overwrites no scene files");
                Check(((AnimationDocument)typeof(MainWindow).GetField("_doc", flags).GetValue(window)).Commands.Count == commandsBefore, "Apply writes no timeline commands");
                HorizontalSlider(dialog).Value = 55;
                CloseWithChoice(dialog, "Keep Editing");
                Check(ReferenceEquals(typeof(MainWindow).GetField("_sceneSetupWindow", flags).GetValue(window), dialog) && view.CaptureCurrentFraming().HorizontalPercent == 55, "Keep Editing cancels close without losing preview");
                CloseWithChoice(dialog, "Discard Preview");
                Check(view.CaptureCurrentFraming().HorizontalPercent == 41, "Discard restores the last Apply checkpoint");
                Call("UrdfModel_Click", window, new RoutedEventArgs());
                dialog = (Window)typeof(MainWindow).GetField("_sceneSetupWindow", flags).GetValue(window);
                HorizontalSlider(dialog).Value = 63;
                CloseWithChoice(dialog, "Apply and Close");
                Check(view.CaptureCurrentFraming().HorizontalPercent == 63 && UrdfModelSettings.Load(root).Framing[robot].HorizontalPercent == 63, "Apply and Close retains the current setup");
            }
            catch (Exception ex) { uiFailure = ex; }
            finally { dialog?.Close(); inspection.Continue = false; }
        }));
        Call("UrdfModel_Click", window, new RoutedEventArgs());
        Dispatcher.PushFrame(inspection);
        if (uiFailure != null) throw uiFailure;
        Check(opened, "Scene Setup modeless inspection completed: " + ((TextBlock)window.FindName("MovieStatusText")).Text);
        app.Shutdown();
    }
}



