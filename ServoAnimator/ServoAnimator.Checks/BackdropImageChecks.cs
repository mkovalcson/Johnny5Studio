using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ServoAnimator;

internal static partial class Program
{
    private static void BackdropImageChecks()
    {
        string parent = CheckDirectory("backdrop-image-checks"); Directory.CreateDirectory(parent);
        string fixture = Path.Combine(parent, "checks-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(fixture);
        string root = Path.Combine(fixture, "Config"), external = Path.Combine(fixture, "Photos"), moved = Path.Combine(fixture, "MovedConfig");
        Directory.CreateDirectory(root); Directory.CreateDirectory(external);
        void Image(string file, byte value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            var bitmap = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { value, 0, 255, 255 }, 4);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var output = File.Create(file); encoder.Save(output);
        }
        Dictionary<string, string> Snapshot(string folder) => Directory.GetFiles(folder, "*", SearchOption.AllDirectories).ToDictionary(f => Path.GetRelativePath(folder, f), f => ProjectPackage.HashFile(f) + "|" + File.GetLastWriteTimeUtc(f).Ticks);
        bool Same(Dictionary<string, string> snapshot, string folder) { var current = Snapshot(folder); return snapshot.Count == current.Count && snapshot.All(p => current.TryGetValue(p.Key, out string value) && value == p.Value); }
        try
        {
            string photo = Path.Combine(external, "Room photo.png"); Image(photo, 1); var original = Snapshot(external);
            string imported = UrdfPresentationSettings.ImportImage(root, photo);
            Check(imported.StartsWith("Backdrops" + Path.DirectorySeparatorChar) && !Path.IsPathRooted(imported) && File.Exists(Path.Combine(root, imported)), "Selected external image is copied to Config/Backdrops and referenced relatively");
            Check(ProjectPackage.HashFile(photo) == ProjectPackage.HashFile(Path.Combine(root, imported)) && Same(original, external), "Image bytes and original source file remain unchanged");
            Check(File.GetLastWriteTimeUtc(photo) == File.GetLastWriteTimeUtc(Path.Combine(root, imported)), "Imported backdrop preserves its source modification date");
            Check(UrdfPresentationSettings.ImportImage(root, photo) == imported && Directory.GetFiles(Path.Combine(root, "Backdrops")).Length == 1, "Selecting the same unchanged image reuses its Config copy");
            Check(UrdfPresentationSettings.ImportImage(root, imported) == imported && UrdfPresentationSettings.ImportImage(root, Path.Combine(root, imported)) == imported, "Relative and absolute selections already in Backdrops are reused");
            Check(UrdfPresentationSettings.ImportImage(root, new Uri(photo).AbsoluteUri) == imported, "File URI image selections resolve to the same Config copy");
            string other = Path.Combine(external, "Other", "Room photo.png"); Image(other, 2);
            string otherImport = UrdfPresentationSettings.ImportImage(root, other);
            Check(otherImport != imported && ProjectPackage.HashFile(Path.Combine(root, imported)) == ProjectPackage.HashFile(photo), "Different images with the same filename cannot overwrite one another");
            string originalHash = ProjectPackage.HashFile(Path.Combine(root, imported)); Image(photo, 3);
            string updatedImport = UrdfPresentationSettings.ImportImage(root, photo);
            Check(updatedImport != imported && ProjectPackage.HashFile(Path.Combine(root, imported)) == originalHash, "Changed source bytes create a separate attachment and preserve older scene images");
            var beforeBad = Snapshot(root); string bad = Path.Combine(external, "bad.png"); File.WriteAllText(bad, "not an image");
            bool rejected = false; try { UrdfPresentationSettings.ImportImage(root, bad); } catch { rejected = true; }
            Check(rejected && Same(beforeBad, root), "Invalid image selection leaves Config files unchanged");
            rejected = false; try { UrdfPresentationSettings.ImportImage(root, Path.Combine(external, "missing.png")); } catch (FileNotFoundException) { rejected = true; }
            Check(rejected && Same(beforeBad, root), "Missing image selection leaves Config files unchanged");
            string legacy = Path.Combine(root, "UrdfBackgrounds", "legacy.png"); Image(legacy, 4);
            var active = new UrdfPresentationSettings { BackgroundMode = UrdfBackgroundMode.MovingImage, BackgroundImage = "UrdfBackgrounds/legacy.png", BackgroundSeed = 9 };
            active.EnsureLocalImage(root);
            Check(active.BackgroundImage.StartsWith("Backdrops") && active.BackgroundSeed == 9 && File.Exists(legacy), "Using an older backdrop copies it to Backdrops without deleting its original or changing motion settings");
            var unused = new UrdfPresentationSettings { BackgroundMode = UrdfBackgroundMode.Color, BackgroundImage = "missing-unused.png" }; unused.EnsureLocalImage(root);
            Check(unused.BackgroundImage == "missing-unused.png", "Inactive image fields do not import unused or missing files");
            var config = UrdfConfiguration.CreateDefault(); config.Presentation = new() { BackgroundMode = UrdfBackgroundMode.Image, BackgroundImage = photo };
            string configFile = Path.Combine(root, "URDFconfig.json"); config.Save(configFile);
            Check(UrdfConfiguration.Load(configFile).Presentation.BackgroundImage == updatedImport, "Saved editor defaults reference the copied Config backdrop");
            string model = Path.Combine(root, "robot.urdf"); File.WriteAllText(model, "<robot name='test'><link name='base'><visual><geometry><box size='0.2 0.2 0.2'/></geometry></visual></link></robot>");
            var scene = new BackgroundLibraryItem { Name = "Photo scene", RobotModelFile = model, Settings = new() { Framing = new(), Background = new() { BackgroundMode = UrdfBackgroundMode.Image, BackgroundImage = other } } };
            BackgroundLibrary.Save(root, scene, scenes: true);
            Check(SavedSceneCommand.Read(root, scene.FileName).Settings.Background.BackgroundImage == otherImport, "Saved scenes store and reuse the canonical backdrop reference");
            var oldScene = new BackgroundLibraryItem { Name = "Legacy scene", RobotModelFile = model, Settings = new() { Framing = new(), Background = new() { BackgroundMode = UrdfBackgroundMode.Image, BackgroundImage = "UrdfBackgrounds/legacy.png" } } };
            string oldSceneFile = Path.Combine(BackgroundLibrary.Folder(root, true), "Legacy.json"); File.WriteAllText(oldSceneFile, JsonSerializer.Serialize(oldScene)); string oldSceneBytes = File.ReadAllText(oldSceneFile);
            Check(SavedSceneCommand.Read(root, oldSceneFile).Settings.Background.BackgroundImage.StartsWith("Backdrops") && File.ReadAllText(oldSceneFile) == oldSceneBytes, "Opening a legacy scene uses the Config copy without rewriting its saved scene file");
            var doc = new AnimationDocument { Commands = new()
            {
                new() { Servo = ServoNames.LoadBackdrop, TextValue = new BackdropCommand { Background = new() { BackgroundMode = UrdfBackgroundMode.Image, BackgroundImage = photo } }.ToJson() },
                new() { Servo = ServoNames.Play, TextValue = "", AudioTrim = new() { HiddenCommands = new() { new() { Servo = ServoNames.LoadBackdrop, TextValue = new BackdropCommand { Background = new() { BackgroundMode = UrdfBackgroundMode.MovingImage, BackgroundImage = other } }.ToJson() } } } }
            } };
            ConfigPathService.MakeDocumentPathsRelative(doc, root);
            Check(BackdropCommand.Read(doc.Commands[0].TextValue).Background.BackgroundImage == updatedImport && BackdropCommand.Read(doc.Commands[1].AudioTrim.HiddenCommands[0].TextValue).Background.BackgroundImage == otherImport,
                "Saving sequences relocates embedded backdrop image paths, including archived trim commands");
            Directory.CreateDirectory(moved);
            foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            { string destination = Path.Combine(moved, Path.GetRelativePath(root, file)); Directory.CreateDirectory(Path.GetDirectoryName(destination)); File.Copy(file, destination); }
            Check(UrdfConfiguration.Load(Path.Combine(moved, "URDFconfig.json")).Presentation.CreateBrush(moved, Brushes.Black) is ImageBrush, "Relative backdrop references still render after moving the Config folder");
            string exportImage = Path.Combine(root, "OldMedia", "export-only.png"); Image(exportImage, 8);
            string exportSequence = Path.Combine(root, "Projects", "Backdrop.json"); Directory.CreateDirectory(Path.GetDirectoryName(exportSequence));
            new AnimationDocument { Commands = new() { new() { Servo = ServoNames.LoadBackdrop, TextValue = new BackdropCommand { Background = new() { BackgroundMode = UrdfBackgroundMode.Image, BackgroundImage = "OldMedia/export-only.png" } }.ToJson() } } }.Save(exportSequence);
            var beforeExport = Snapshot(root); string zipFile = Path.Combine(fixture, "new.zip");
            ProjectPackage.Export(root, exportSequence, zipFile, ProjectPackageKind.Sequence);
            Check(Same(beforeExport, root), "ZIP export localizes backdrop references inside the package without changing source Config files");
            using (var plan = ProjectPackageImport.Prepare(zipFile, moved, ProjectPackageKind.Sequence))
            {
                Check(plan.Files.Any(f => f.FileName.StartsWith("Backdrops/")) && plan.Files.All(f => !f.FileName.StartsWith("OldMedia/")), "New packages review and install backdrop images under Config/Backdrops");
                plan.Commit(); var importedDoc = AnimationDocument.Load(plan.EntryFile);
                var background = BackdropCommand.Read(importedDoc.Commands[0].TextValue).Background;
                Check(background.BackgroundImage.StartsWith("Backdrops") && background.CreateBrush(moved, Brushes.Black) is ImageBrush, "Imported package backdrop references render from their managed destination");
            }
            string legacyZip = Path.Combine(fixture, "legacy.zip");
            using (var zip = ZipFile.Open(legacyZip, ZipArchiveMode.Create))
            {
                var manifest = new ProjectPackageManifest { Kind = "Sequence", EntryFile = "Projects/LegacyBackdrop.json" };
                foreach (var entry in new[] { ("Projects/LegacyBackdrop.json", exportSequence, "Sequence"), ("OldMedia/export-only.png", exportImage, "Media") })
                {
                    byte[] content = File.ReadAllBytes(entry.Item2); using (var output = zip.CreateEntry("Config/" + entry.Item1).Open()) output.Write(content);
                    manifest.Files.Add(new() { Path = entry.Item1, Role = entry.Item3, Size = content.Length, ModifiedUtc = File.GetLastWriteTimeUtc(entry.Item2), Sha256 = ProjectPackage.Hash(content) });
                }
                using var index = zip.CreateEntry(ProjectPackage.ManifestName).Open(); JsonSerializer.Serialize(index, manifest, ProjectPackage.JsonOptions);
            }
            var beforePrepare = Snapshot(moved);
            using (var legacyPlan = ProjectPackageImport.Prepare(legacyZip, moved, ProjectPackageKind.Sequence))
                Check(legacyPlan.Files.Any(f => f.FileName.StartsWith("OldMedia/")) && Same(beforePrepare, moved), "Older ZIP backdrop paths remain importable and preparation does not write to destination Config");
            var app = new App(); app.InitializeComponent(); DisableTestStartup(app);
            var editor = new BackdropEditorWindow(new BackdropCommand { Background = new() { BackgroundMode = UrdfBackgroundMode.Image, BackgroundImage = photo } }, root);
            editor.Loaded += (_, _) => editor.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                SceneDescendants(editor).OfType<Button>().Single(b => Equals(b.Content, "Apply changes")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (editor.IsVisible) editor.Close();
            }));
            Check(editor.ShowDialog() == true && editor.Result.Background.BackgroundImage == updatedImport, "Applying a manually entered backdrop image path imports it before accepting the settings");
            var preview = new RobotHeadView(deferModelLoad: true); preview.ConfigureMeetingPreview(null, root);
            preview.InstallModel(model, UrdfScene.Load(model), new());
            preview.SetTimelineBackground(new() { BackgroundMode = UrdfBackgroundMode.Image, BackgroundImage = other });
            Check(preview.CaptureBackground().BackgroundImage == otherImport && preview.Background is ImageBrush, "Using a backdrop directly in the viewport references the managed Config copy");
            var window = new MainWindow(); const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(MainWindow).GetField("_folders", flags).SetValue(window, new FolderSettings { ConfigFolder = root });
            var request = new EditorApiRequest { Method = "set_backdrop", RequestId = Guid.NewGuid().ToString("N"), Backdrop = new() { Background = new() { BackgroundMode = UrdfBackgroundMode.Image, BackgroundImage = photo } } };
            typeof(MainWindow).GetMethod("HandleBackdropApi", flags).Invoke(window, new object[] { request, "backdrop-image-check" });
            var apiDoc = (AnimationDocument)typeof(MainWindow).GetField("_doc", flags).GetValue(window);
            Check(BackdropCommand.Read(apiDoc.Commands.Single(c => c.Servo == ServoNames.LoadBackdrop).TextValue).Background.BackgroundImage == updatedImport && !window.IsLoaded,
                "API backdrop settings also import direct image paths without starting hardware");
            app.Shutdown();
            Check(!Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories).Any(), "Backdrop import leaves no incomplete copy files");
        }
        finally { ProjectPackageImport.RemoveWork(fixture, parent, "checks-"); }
    }
}
