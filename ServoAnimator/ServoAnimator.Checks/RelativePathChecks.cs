using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Controls;
using ServoAnimator;

internal static partial class Program
{
    private static void NormalizeConfigPaths(string root, string backup)
    {
        if (System.Diagnostics.Process.GetProcessesByName("AnimationEditorPlayer").Length > 0) throw new InvalidOperationException("Close the editor before normalizing saved files.");
        root = Path.GetFullPath(root); backup = Path.GetFullPath(backup);
        if (!Directory.Exists(root) || ConfigPathService.IsWithin(root, backup)) throw new InvalidOperationException("Use a backup outside Config.");
        var changes = new List<(string Path, byte[] Original, byte[] Updated, DateTime Modified)>();
        foreach (string file in ProjectPackage.FilesIn(root).Where(f => !ConfigPathService.IsWithin(Path.Combine(root, "Models"), f)))
        {
            if (!Path.GetExtension(file).Equals(".json", StringComparison.OrdinalIgnoreCase) && !Path.GetFileName(file).EndsWith("ControllerMapping.selected.txt", StringComparison.OrdinalIgnoreCase)) continue;
            byte[] original = File.ReadAllBytes(file); byte[] updated = null;
            if (Path.GetExtension(file).Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                var json = JsonNode.Parse(original, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                bool changed = ConfigPathService.NormalizeLibraryImage(json, root, file);
                changed |= ConfigPathService.MakeReferencesRelative(json, root);
                if (changed) updated = ProjectPackage.Bytes(json);
            }
            else if (Path.GetFileName(file).EndsWith("ControllerMapping.selected.txt", StringComparison.OrdinalIgnoreCase))
            {
                string value = File.ReadAllText(file).Trim(), relative = ConfigPathService.Reference(root, value);
                if (relative != value) updated = System.Text.Encoding.UTF8.GetBytes(relative);
            }
            if (updated != null) changes.Add((file, original, updated, File.GetLastWriteTimeUtc(file)));
        }
        Directory.CreateDirectory(backup);
        foreach (var item in changes)
        {
            string copy = ProjectPackage.Destination(backup, Path.GetRelativePath(root, item.Path));
            Directory.CreateDirectory(Path.GetDirectoryName(copy));
            using (var output = new FileStream(copy, FileMode.CreateNew)) output.Write(item.Original);
            File.SetLastWriteTimeUtc(copy, item.Modified);
        }
        foreach (var item in changes) if (ProjectPackage.HashFile(item.Path) != ProjectPackage.Hash(item.Original)) throw new IOException("A saved file changed during preparation.");
        var installed = new List<(string Path, byte[] Original, DateTime Modified)>();
        try
        {
            foreach (var item in changes)
            {
                string temporary = item.Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try { File.WriteAllBytes(temporary, item.Updated); File.Replace(temporary, item.Path, null); installed.Add((item.Path, item.Original, item.Modified)); }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
        }
        catch
        {
            foreach (var item in installed) { File.WriteAllBytes(item.Path, item.Original); File.SetLastWriteTimeUtc(item.Path, item.Modified); }
            throw;
        }
        File.WriteAllText(Path.Combine(backup, "normalized-files.json"), JsonSerializer.Serialize(changes.Select(c => Path.GetRelativePath(root, c.Path)), ProjectPackage.JsonOptions));
        Console.WriteLine($"Normalized {changes.Count} saved files; originals preserved in {backup}");
    }

    private static void RelativePathChecks()
    {
        var app = new App(); app.InitializeComponent(); DisableTestStartup(app);
        string parent = CheckDirectory("relative-path-checks"); Directory.CreateDirectory(parent);
        string fixture = Path.Combine(parent, "checks-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(fixture);
        string root = Path.Combine(fixture, "Config"); Directory.CreateDirectory(root);
        string Full(string path) => Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
        void Write(string path, string value) { Directory.CreateDirectory(Path.GetDirectoryName(Full(path))); File.WriteAllText(Full(path), value); }
        try
        {
            Write("Projects/voice.wav", "audio");
            string audio = Full("Projects/voice.wav"), scene = Full("LibraryScenes/Scene.json"), model = Full("Models/Robots/Test/test.urdf");
            Check(ConfigPathService.Reference(root, audio) == Path.Combine("Projects", "voice.wav"), "Display/persistence paths are Config-relative");
            Check(ConfigPathService.Resolve(root, "Projects/voice.wav") == audio, "Relative text resolves from Config rather than the working directory");
            Check(ConfigPathService.Reference(root, new Uri(audio).AbsoluteUri) == Path.Combine("Projects", "voice.wav"), "Legacy file URIs become relative");
            var document = new AnimationDocument { AudioFilePath = audio, UrdfBackgroundFile = scene, Description = audio, Commands = new()
            {
                new() { Servo = ServoNames.Play, TextValue = audio, AudioTrim = new() { HiddenCommands = new() { new() { Servo = ServoNames.Play, TextValue = audio } } } },
                new() { Servo = ServoNames.LoadBackdrop, TextValue = JsonSerializer.Serialize(new { SceneFile = scene }) },
                new() { Servo = ServoNames.TvShow, TextValue = JsonSerializer.Serialize(new { Path = Full("TvMedia/movie.mp4") }) },
                new() { Servo = ServoNames.FireLaser, TextValue = JsonSerializer.Serialize(new { Sound = audio }) }
            } };
            string path = Full("Projects/Sequence.json"); document.Save(path);
            var stored = AnimationDocument.Load(path);
            Check(!Path.IsPathFullyQualified(stored.AudioFilePath) && !Path.IsPathFullyQualified(stored.UrdfBackgroundFile), "Sequence audio and scene references save relatively");
            Check(stored.Commands[0].TextValue == Path.Combine("Projects", "voice.wav") && stored.Commands[0].AudioTrim.HiddenCommands[0].TextValue == Path.Combine("Projects", "voice.wav"), "Play and archived commands save relatively");
            Check(!string.Join("", stored.Commands.Select(c => c.TextValue)).Contains(root.Replace("\\", "\\\\")), "Nested scene, TV and sound command paths save relatively");
            Check(document.AudioFilePath == audio && document.Commands[0].TextValue == audio && stored.Description == audio, "Saving preserves live paths and free text");
            var movie = new MovieDocument { Sequences = new() { path } }; movie.Save(Full("Projects/Movie.json"));
            Check(MovieDocument.Load(Full("Projects/Movie.json")).Sequences[0] == Path.Combine("Projects", "Sequence.json"), "Movies store Config-relative sequence paths");
            var profile = ControllerProfile.Defaults(ControllerKind.Xbox);
            ControllerProfileStore.SaveSelected(root, profile, Full("Custom Xbox.json"));
            Check(File.ReadAllText(Full("XboxControllerMapping.selected.txt")) == "Custom Xbox.json" && ControllerProfileStore.ActivePath(root, ControllerKind.Xbox) == Full("Custom Xbox.json"), "Controller selection saves relatively and resolves on reopen");
            File.WriteAllText(Full("XboxControllerMapping.selected.txt"), Full("Custom Xbox.json"));
            Check(ControllerProfileStore.ActivePath(root, ControllerKind.Xbox) == Full("Custom Xbox.json"), "Legacy absolute controller selections still load");
            var frames = new RobotFrameExportStore(); frames.Remember(root, path, new() { Folder = Full("Exports/Frames") }); frames.Save(root);
            Check(!File.ReadAllText(Full(RobotFrameExportStore.FileName)).Contains(root.Replace("\\", "\\\\")), "Frame export preferences store relative folders");
            Check(RobotFrameExportStore.Load(root).For(root, path).Folder == Full("Exports/Frames"), "Frame export folders resolve on reopen");
            var legacyFrames = ProjectPackage.ReadObject(Full(RobotFrameExportStore.FileName));
            var legacySequences = (JsonObject)ProjectPackage.Get(legacyFrames, "Sequences");
            var remembered = legacySequences.First(); legacySequences.Remove(remembered.Key); legacySequences[path] = remembered.Value;
            File.WriteAllBytes(Full(RobotFrameExportStore.FileName), ProjectPackage.Bytes(legacyFrames));
            var legacyStore = RobotFrameExportStore.Load(root);
            Check(legacyStore.For(root, path).Folder == Full("Exports/Frames"), "Legacy absolute frame-export keys remain readable");
            legacyStore.Save(root);
            var export = new ExportAnimationWindow(new[] { 50 }, false, 50, false, Full("Projects/export.json"), root);
            var output = (TextBox)export.FindName("PathBox");
            Check(output.Text == Path.Combine("Projects", "export.json") && export.FilePath == Full("Projects/export.json"), "Export form displays relative paths and returns a resolved destination");
            Write("Library/Command/pose.png", "image");
            AnimationDocument.SaveLibraryCommand(Full("Library/Command/Pose.json"), new[] { new ServoCommand() }, imageFile: "pose.png");
            Check(AnimationDocument.LoadLibraryItem(Full("Library/Command/Pose.json")).ImageFile == Path.Combine("Library", "Command", "pose.png"), "Pose artwork saves relative to Config");
            Check(LibraryItemInfo.Scan(Full("Library/Command"), false).Single().ImagePath == Full("Library/Command/pose.png"), "New artwork references resolve through the library browser");
            Check(MainWindow.ResolveApiLibraryName(Full("Library/Command"), "Library/Command/Pose.json") == Full("Library/Command/Pose.json"), "Config-relative controller library selections resolve correctly");
            Check(MainWindow.ResolveApiLibraryName(Full("Library/Command"), "Pose.json") == Full("Library/Command/Pose.json"), "Legacy library names remain readable");
            Write("Legacy.json", JsonSerializer.Serialize(new { ModelFile = model, Description = "preserve" }));
            string prior = File.ReadAllText(Full("Legacy.json")); string backup = Path.Combine(fixture, "Backup");
            NormalizeConfigPaths(root, backup);
            Check(File.ReadAllText(Path.Combine(backup, "Legacy.json")) == prior && !ProjectPackage.Text(ProjectPackage.ReadObject(Full("Legacy.json")), "ModelFile").Contains(root), "Existing settings normalize with exact original backups");
            string moved = Path.Combine(fixture, "MovedConfig"); Directory.Move(root, moved);
            Check(ControllerProfileStore.ActivePath(moved, ControllerKind.Xbox) == Path.Combine(moved, "Custom Xbox.json"), "Controller selection follows relocated Config");
            Check(RobotFrameExportStore.Load(moved).For(moved, Path.Combine(moved, "Projects/Sequence.json")).Folder == Path.GetFullPath(Path.Combine(moved, "Exports/Frames")), "Frame export preferences follow relocated Config");
        }
        finally { ProjectPackageImport.RemoveWork(fixture, parent, "checks-"); }
    }
}
