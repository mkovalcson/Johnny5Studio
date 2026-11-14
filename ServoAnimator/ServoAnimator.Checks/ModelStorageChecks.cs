using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using ServoAnimator;

internal static partial class Program
{
    private static void VerifyManagedModelMigration(string reportFile)
    {
        var app = new App(); app.InitializeComponent(); DisableTestStartup(app);
        var report = ProjectPackage.ReadObject(reportFile);
        string root = ProjectPackage.Text(report, "ConfigRoot");
        foreach (var file in ((JsonArray)ProjectPackage.Get(report, "Files")).OfType<JsonObject>())
            Check(ProjectPackage.HashFile(ProjectPackage.Destination(root, ProjectPackage.Text(file, "Path"))) == ProjectPackage.Text(file, "Sha256"), "Installed migration file matches its verified bytes: " + ProjectPackage.Text(file, "Path"));
        foreach (var original in (JsonObject)ProjectPackage.Get(report, "OriginalRuntimeSources"))
            Check(ProjectPackage.HashFile(original.Key) == original.Value.GetValue<string>(), "Original runtime source remains unchanged: " + original.Key);
        foreach (var model in ((JsonArray)ProjectPackage.Get(report, "Models")).OfType<JsonObject>())
        {
            string file = ProjectPackage.Destination(root, ProjectPackage.Text(model, "Target"));
            Check(ProjectPackage.ModelIdentity(file) == ProjectPackage.Text(model, "Identity"), "Installed model identity is unchanged");
            Check(UrdfScene.Load(file) != null, "Installed runtime model renders: " + file);
        }
        var defaults = UrdfModelSettings.Load(root);
        Check(ConfigPathService.IsWithin(ModelStorage.Folder(root, false), defaults.SelectedFile), "Active robot defaults select the managed robot");
        Check(defaults.Framing.Keys.All(f => ConfigPathService.IsWithin(ModelStorage.Folder(root, false), f)), "Every framing key points to managed robot models");
        void Inspect(JsonNode node)
        {
            void Reference(string text)
            {
                if (!text.EndsWith(".urdf", StringComparison.OrdinalIgnoreCase)) return;
                Check(ConfigPathService.TryResolve(root, text, out string full) && ConfigPathService.IsWithin(Path.Combine(root, "Models"), full) && File.Exists(full), "Saved URDF reference resolves inside Config: " + text);
            }
            if (node is JsonObject obj) foreach (var pair in obj) { Reference(pair.Key); Inspect(pair.Value); }
            else if (node is JsonArray array) foreach (var child in array) Inspect(child);
            else if (node is JsonValue value && value.TryGetValue<string>(out string text))
            {
                Reference(text);
                if (text.TrimStart().StartsWith('{') || text.TrimStart().StartsWith('['))
                {
                    JsonNode embedded = null; try { embedded = JsonNode.Parse(text); } catch (JsonException) { }
                    if (embedded != null) Inspect(embedded);
                }
            }
        }
        foreach (string file in ProjectPackage.FilesIn(root).Where(f => Path.GetExtension(f).Equals(".json", StringComparison.OrdinalIgnoreCase) && !ConfigPathService.IsWithin(Path.Combine(root, "Models"), f)))
            Inspect(JsonNode.Parse(File.ReadAllText(file)));
        foreach (bool scenes in new[] { false, true })
        {
            var library = BackgroundLibrary.List(root, out string errors, scenes);
            Check(string.IsNullOrEmpty(errors) && library.Count > 0, "Existing saved scenes/backgrounds resolve all dependencies: " + errors);
        }
        RuntimeModelFeatureChecks(Path.Combine(root, "Models/Robots/Johnny5FullBody/johnny5_head.urdf"), Path.Combine(root, "Models/Scenery/MatrixConstruct/MatrixConstruct_backdrop.urdf"));
    }
    private static void ModelStorageChecks()
    {
        string parent = CheckDirectory("model-storage-checks"); Directory.CreateDirectory(parent);
        string fixture = Path.Combine(parent, "checks-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(fixture);
        string root = Path.Combine(fixture, "Config"), source = Path.Combine(fixture, "Originals"); Directory.CreateDirectory(root); Directory.CreateDirectory(source);
        string Write(string folder, string file, string content)
        { string path = Path.Combine(folder, file); Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, content); return path; }
        Dictionary<string, string> Snapshot(string folder) => ProjectPackage.FilesIn(folder).ToDictionary(f => Path.GetRelativePath(folder, f), f => ProjectPackage.HashFile(f) + "|" + File.GetLastWriteTimeUtc(f).Ticks);
        void Same(Dictionary<string, string> before, string folder, string label)
        { var after = Snapshot(folder); Check(before.Count == after.Count && before.All(p => after.TryGetValue(p.Key, out string value) && value == p.Value), label); }
        try
        {
            string robot = Write(source, "robot.urdf", "<robot name='robot'><link name='base'><visual><geometry><mesh filename='Meshes/used.obj'/></geometry></visual></link></robot>");
            Write(source, "Meshes/used.obj", "v 0 0 0\nv 0 1 0\nv 0 0 1\nf 1 2 3\n");
            Write(source, "Meshes/unused.obj", "unused"); Write(source, "SourceCAD/original.step", "source CAD"); Write(source, "documentation.md", "preserve documentation");
            string scenery = Write(source, "test_backdrop.urdf", "<robot name='backdrop'><link name='backdrop_root_link'><visual><geometry><box size='1 1 .1'/></geometry></visual></link></robot>");
            const string robotTarget = "Models/Robots/TestRobot/robot.urdf", sceneryTarget = "Models/Scenery/TestRoom/test_backdrop.urdf";
            string missingAlias = Path.Combine(fixture, "OldInstall", "robot.urdf");
            Write(root, "UrdfModelSettings.json", JsonSerializer.Serialize(new { ModelFile = robot, Framing = new Dictionary<string, UrdfFraming>
            { [robot] = new() { HorizontalPercent = 12 }, [missingAlias] = new() { HorizontalPercent = -9 } } }));
            var embedded = new { Background = new { BackgroundMode = "Urdf3D", Stage = new { ModelFile = scenery } } };
            Write(root, "Projects/Sequence.json", JsonSerializer.Serialize(new { commands = new[] { new { servo = "LoadBackdrop", value = JsonSerializer.Serialize(embedded), audioTrim = new { hiddenCommands = new[] { new { value = JsonSerializer.Serialize(embedded) } } } } } }));
            Write(root, "Projects/Movie.json", JsonSerializer.Serialize(new { sequences = new[] { "Projects/Sequence.json" }, unknownMetadata = new { RobotModelFile = robot } }));
            Write(root, "LibraryScenes/Scene.json", JsonSerializer.Serialize(new { RobotModelFile = robot, Settings = embedded }));
            Write(root, "MeetingSettings.json", JsonSerializer.Serialize(new { retained = "custom", Setup = embedded }));
            Write(root, "EditorRecovery.json", JsonSerializer.Serialize(new { Scene = embedded }));
            var originalConfig = Snapshot(root); var originalSources = Snapshot(source);
            var request = new ModelMigrationRequest { ConfigRoot = root, BackupFolder = Path.Combine(fixture, "Backup"), Models = new()
            { new() { Source = robot, Target = robotTarget }, new() { Source = scenery, Target = sceneryTarget } }, Aliases = new() { [missingAlias] = robotTarget } };
            ModelStorageMigration.Run(request);
            Same(originalConfig, root, "Preparing migration leaves Config unchanged");
            Check(!Directory.Exists(request.BackupFolder), "Preparing migration does not create backups or destination models");
            request.Commit = true; ModelStorageMigration.Run(request);
            Same(originalSources, source, "All source models, STEP files, unused meshes, docs and timestamps are preserved");
            Check(Directory.GetFiles(Path.Combine(root, "Models"), "*", SearchOption.AllDirectories).Length == 3, "Runtime copies contain only URDFs and required meshes");
            Check(ProjectPackage.ModelIdentity(robot) == ProjectPackage.ModelIdentity(Path.Combine(root, robotTarget)), "Copied robot has the same canonical model identity");
            Check(File.Exists(Path.Combine(request.BackupFolder, "COMPLETED.txt")), "Migration retains a completed report and original documents");
            foreach (var file in originalConfig)
                Check(ProjectPackage.HashFile(Path.Combine(request.BackupFolder, file.Key)) + "|" + File.GetLastWriteTimeUtc(Path.Combine(request.BackupFolder, file.Key)).Ticks == file.Value, "Original document backup: " + file.Key);
            var stored = UrdfModelSettings.Load(root);
            Check(stored.SelectedFile == Path.Combine(root, robotTarget.Replace('/', '\\')) && stored.Framing.Count == 1 && stored.Framing[stored.SelectedFile].HorizontalPercent == 12, "Historical framing aliases merge while retaining the selected model's framing");
            var sequence = ProjectPackage.ReadObject(Path.Combine(root, "Projects/Sequence.json"));
            string json = sequence.ToJsonString();
            Check(!json.Contains("Originals") && !json.Contains("OldInstall"), "Sequence and hidden embedded commands reference managed models");
            var meeting = ProjectPackage.ReadObject(Path.Combine(root, "MeetingSettings.json"));
            Check(ProjectPackage.Text(meeting, "retained") == "custom", "Unknown settings survive migration");
            var live = new { RobotModelFile = Path.Combine(root, robotTarget), Settings = new { Stage = new { ModelFile = Path.Combine(root, sceneryTarget) } } };
            string serialized = ModelStorage.Serialize(live, root, ProjectPackage.JsonOptions);
            Check(!serialized.Contains(fixture.Replace("\\", "\\\\")) && serialized.Contains("Models"), "Saving managed model references uses Config-relative paths");
            Check(live.RobotModelFile == Path.Combine(root, robotTarget), "Saving does not modify live absolute model references");
            string moved = Path.Combine(fixture, "RelocatedConfig"); Directory.Move(root, moved);
            var reopened = UrdfModelSettings.Load(moved);
            Check(File.Exists(reopened.SelectedFile) && UrdfScene.Load(reopened.SelectedFile) != null, "Moved Config resolves and renders its robot without the original model location");
            var migrated = ProjectPackage.ReadObject(Path.Combine(moved, "LibraryScenes/Scene.json"));
            var embeddedStage = (JsonObject)ProjectPackage.Get((JsonObject)ProjectPackage.Get((JsonObject)ProjectPackage.Get(migrated, "Settings"), "Background"), "Stage");
            Check(UrdfScene.Load(Path.Combine(moved, ProjectPackage.Text(embeddedStage, "ModelFile"))) != null, "Moved Config renders its scenery independently");
            string zip = Path.Combine(fixture, "model.zip"); ProjectPackage.Export(moved, scenery, zip, ProjectPackageKind.UrdfModel);
            string importRoot = Path.Combine(fixture, "ImportConfig"); Directory.CreateDirectory(importRoot);
            using (var import = ProjectPackageImport.Prepare(zip, importRoot, ProjectPackageKind.UrdfModel))
            { Check(ConfigPathService.IsWithin(ModelStorage.Folder(importRoot, true), import.EntryFile), "New scenery ZIP imports into Models/Scenery"); import.Commit(); }
            ProjectPackage.Export(moved, robot, zip, ProjectPackageKind.UrdfModel);
            using (var import = ProjectPackageImport.Prepare(zip, importRoot, ProjectPackageKind.UrdfModel))
                Check(ConfigPathService.IsWithin(ModelStorage.Folder(importRoot, false), import.EntryFile), "New robot ZIP imports into Models/Robots");
            // Force a document write failure after assets are installed to exercise rollback.
            string failing = Path.Combine(fixture, "FailureConfig"); Directory.CreateDirectory(failing);
            string lockedFile = Write(failing, "UrdfModelSettings.json", JsonSerializer.Serialize(new { ModelFile = robot }));
            var failingBefore = Snapshot(failing);
            var failingRequest = new ModelMigrationRequest { ConfigRoot = failing, BackupFolder = Path.Combine(fixture, "FailureBackup"), Commit = true, Models = new() { new() { Source = robot, Target = robotTarget } } };
            bool rejected = false;
            using (var locked = new FileStream(lockedFile, FileMode.Open, FileAccess.Read, FileShare.Read))
                try { ModelStorageMigration.Run(failingRequest); } catch (IOException) { rejected = true; }
            Check(rejected, "A locked destination stops migration"); Same(failingBefore, failing, "A failed migration rolls back copied runtime files and retains original documents");
            Write(failing, robotTarget, "different existing model"); var collisionBefore = Snapshot(failing);
            failingRequest.BackupFolder = Path.Combine(fixture, "CollisionBackup"); rejected = false;
            try { ModelStorageMigration.Run(failingRequest); } catch (IOException) { rejected = true; }
            Check(rejected, "Different existing runtime models are never silently overwritten"); Same(collisionBefore, failing, "Conflicting migration leaves every original file unchanged");
        }
        finally { ProjectPackageImport.RemoveWork(fixture, parent, "checks-"); }
    }
}
