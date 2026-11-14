using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using ServoAnimator;

internal static partial class Program
{
    private static void ProjectPackageChecks()
    {
        string parent = CheckDirectory("package-checks"); Directory.CreateDirectory(parent);
        string fixture = Path.Combine(parent, "checks-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(fixture);
        string source = Path.Combine(fixture, "OriginalConfig"), target = Path.Combine(fixture, "ImportedConfig");
        Directory.CreateDirectory(source); Directory.CreateDirectory(target);
        string Write(string relative, byte[] bytes)
        {
            string file = Path.Combine(source, relative); Directory.CreateDirectory(Path.GetDirectoryName(file)); File.WriteAllBytes(file, bytes);
            File.SetLastWriteTimeUtc(file, new DateTime(2024, 7, 8, 9, 10, 11, DateTimeKind.Utc).AddTicks(1234567)); return file;
        }
        Dictionary<string, string> Snapshot(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(p => Path.GetRelativePath(root, p), p => ProjectPackage.HashFile(p) + "|" + File.GetLastWriteTimeUtc(p).Ticks, StringComparer.OrdinalIgnoreCase);
        void Same(Dictionary<string, string> before, string root, string label)
        { var after = Snapshot(root); Check(before.Count == after.Count && before.All(p => after.TryGetValue(p.Key, out var value) && p.Value == value), label); }
        ProjectPackageManifest Index(string zipFile)
        { using var zip = ZipFile.OpenRead(zipFile); using var input = zip.GetEntry(ProjectPackage.ManifestName).Open(); return JsonSerializer.Deserialize<ProjectPackageManifest>(input, ProjectPackage.JsonOptions); }
        string Tamper(string sourceZip, Action<ProjectPackageManifest, Dictionary<string, byte[]>> edit)
        {
            string output = Path.Combine(fixture, Guid.NewGuid().ToString("N") + ".zip");
            var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            ProjectPackageManifest manifest;
            using (var zip = ZipFile.OpenRead(sourceZip))
            {
                using var input = zip.GetEntry(ProjectPackage.ManifestName).Open(); manifest = JsonSerializer.Deserialize<ProjectPackageManifest>(input, ProjectPackage.JsonOptions);
                foreach (var entry in zip.Entries.Where(e => e.FullName != ProjectPackage.ManifestName))
                { using var stream = entry.Open(); using var bytes = new MemoryStream(); stream.CopyTo(bytes); files.Add(entry.FullName, bytes.ToArray()); }
            }
            edit(manifest, files);
            using (var zip = ZipFile.Open(output, ZipArchiveMode.Create))
            {
                foreach (var pair in files) { using var payload = zip.CreateEntry(pair.Key).Open(); payload.Write(pair.Value); }
                using var indexStream = zip.CreateEntry(ProjectPackage.ManifestName).Open(); JsonSerializer.Serialize(indexStream, manifest, ProjectPackage.JsonOptions);
            }
            return output;
        }
        void Rejected(string zip, string root, ProjectPackageKind kind, string label)
        {
            var before = Snapshot(root); bool rejected = false;
            try { using var plan = ProjectPackageImport.Prepare(zip, root, kind); } catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or JsonException or OverflowException) { rejected = true; }
            Check(rejected, label); Same(before, root, label + " leaves destination files unchanged");
        }
        try
        {
            string audio = Write("Projects/Travel/speech.wav", new byte[] { 1, 2, 3, 4, 5 });
            string hidden = Write("Projects/Travel/hidden.wav", new byte[] { 9, 8, 7, 6 });
            string beamSound = Write("Library/Audio/custom-beam.wav", new byte[] { 7, 5, 3, 1 });
            Write("Library/Audio/Beam_Out.wav", new byte[] { 9, 7, 5 });
            string video = Write("VideoMedia/demo.mp4", new byte[] { 4, 5, 6, 7 });
            string image = Write("UrdfBackgrounds/background.png", Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII="));
            string externalSound = Path.Combine(fixture, "external.wav"); File.WriteAllBytes(externalSound, new byte[] { 12, 13, 14 });
            string modelFolder = Path.Combine(fixture, "Robot Source"); Directory.CreateDirectory(Path.Combine(modelFolder, "Meshes"));
            string mesh = Path.Combine(modelFolder, "Meshes", "body part.obj"); File.WriteAllText(mesh, "v 0 0 0\nv 0 1 0\nv 0 0 1\nf 1 2 3\n");
            string robot = Path.Combine(modelFolder, "transfer-robot.urdf");
            File.WriteAllText(robot, "<robot name='package-check-robot'><link name='base'><visual name='body_visual'><geometry><mesh filename='" + new Uri(mesh).AbsoluteUri + "'/></geometry></visual></link><link name='arm'/><joint name='hinge' type='fixed'><parent link='base'/><child link='arm'/></joint></robot>");
            string identity = ProjectPackage.ModelIdentity(robot);
            string frames = Path.Combine(modelFolder, "Meshes", "Body", "Treads"); Directory.CreateDirectory(frames);
            File.WriteAllText(Path.Combine(frames, "frame.stl"), "solid frame\nfacet normal 0 0 1\nouter loop\nvertex 0 0 0\nvertex 1 0 0\nvertex 0 1 0\nendloop\nendfacet\nendsolid frame\n");
            File.WriteAllText(Path.Combine(frames, "tread_animation.json"), "{\"phases\":2,\"phaseStep_m\":0.1,\"sides\":{\"left\":{\"layers\":[{\"urdfLink\":\"base\",\"urdfVisual\":\"body_visual\",\"frames\":[\"Meshes/Body/Treads/frame.stl\",\"Meshes/Body/Treads/frame.stl\"]},{\"urdfLink\":\"unused_link\",\"urdfVisual\":\"unused_visual\",\"frames\":[\"Meshes/Body/Treads/unused.stl\"]}]}}}");
            string fallApart = Path.Combine(modelFolder, FallApartClip.RelativePath); Directory.CreateDirectory(Path.GetDirectoryName(fallApart));
            var poses = new float[] { 0, 0, 0, 0, 0, 0, 1, 1, 0, 0, 0, 0, 0, 1 }; var poseBytes = new byte[poses.Length * 4]; Buffer.BlockCopy(poses, 0, poseBytes, 0, poseBytes.Length);
            File.WriteAllText(fallApart, JsonSerializer.Serialize(new { version = 1, fps = 30, frameCount = 2, links = new[] { new { link = "arm", poses = Convert.ToBase64String(poseBytes) } } }));
            string exterior = Path.Combine(modelFolder, "Meshes", "ExteriorOptimized"); Directory.CreateDirectory(exterior);
            string mask = Path.Combine(exterior, "used.backfaces.bin"); File.WriteAllBytes(mask, Array.Empty<byte>());
            File.WriteAllText(Path.Combine(exterior, "manifest.json"), JsonSerializer.Serialize(new { algorithm = "closed-opaque-backfaces-v1", urdfSha256 = ProjectPackage.HashFile(robot),
                sources = new Dictionary<string, string> { ["Meshes/body part.obj"] = ProjectPackage.HashFile(mesh) },
                visuals = new Dictionary<string, object> { ["base/body_visual"] = new { source = new Uri(mesh).AbsoluteUri, mesh = "Meshes/ExteriorOptimized/used.backfaces.bin", sha256 = ProjectPackage.HashFile(mask), before = 1, after = 0 } } }));
            foreach (string unused in new[] { "readme.md", "source.step", "unused.obj", "source.blend", "Body/Treads/unused.stl", "ExteriorOptimized/old.backfaces.bin" })
            { string file = Path.Combine(modelFolder, "Meshes", unused); Directory.CreateDirectory(Path.GetDirectoryName(file)); File.WriteAllText(file, "not used at runtime"); }
            var scene = new BackgroundLibraryItem { Name = "Travel scene", RobotModelFile = robot, Settings = new BackdropCommand { Framing = new UrdfFraming(),
                Background = new UrdfPresentationSettings { BackgroundMode = UrdfBackgroundMode.MovingImage, BackgroundImage = Path.GetRelativePath(source, image) } } };
            BackgroundLibrary.Save(source, scene, true);
            var sequence = new AnimationDocument { Description = "First sequence", AudioFilePath = audio, AudioFile = "speech.wav", UrdfBackgroundFile = scene.FileName,
                SplineServos = new() { "NeckTurn" }, Commands = new()
                {
                    new() { Servo = ServoNames.Play, OffsetSeconds = 2, TextValue = audio, AudioTakeId = "take-1", AudioTrim = new AudioTrimSettings { HiddenCommands = new()
                    { new() { Servo = ServoNames.Play, TextValue = hidden, OffsetSeconds = 3 }, new() { Servo = ServoNames.TvShow, TextValue = new TvShowCommand { Path = image }.ToJson() } } } },
                    new() { Servo = ServoNames.VideoClip, TextValue = new VideoClipCommand { Path = video, SourceSeconds = 5 }.ToJson() },
                    new() { Servo = ServoNames.TvShow, TextValue = new TvShowCommand { Path = video }.ToJson() },
                    new() { Servo = ServoNames.LcdShow, TextValue = new LcdShowCommand { Image = image }.ToJson() },
                    new() { Servo = ServoNames.FireLaser, TextValue = new FireLaserCommand { Sound = externalSound }.ToJson() },
                    new() { Servo = ServoNames.UrdfBeam, TextValue = new BeamCommand { Sound = beamSound }.ToJson() },
                    new() { Servo = ServoNames.UrdfBeam, OffsetSeconds = 5, TextValue = new BeamCommand { Direction = BeamDirection.Out }.ToJson() },
                    new() { Servo = ServoNames.FallApart, OffsetSeconds = 10, TextValue = new FallApartCommand().ToJson() },
                    new() { Servo = ServoNames.FallApart, OffsetSeconds = 15, TextValue = new FallApartCommand { Reverse = true, Speed = .5 }.ToJson() },
                    new() { Servo = ServoNames.NeckTurn, NumericValue = 25, OffsetSeconds = 1, GroupId = "group-1", BreakSpline = true }
                } };
            string first = Path.Combine(source, "Projects", "Travel", "A.json"); sequence.Save(first);
            var other = new AnimationDocument { Description = "Second sequence", Commands = new() { new() { Servo = ServoNames.LoadBackdrop, TextValue = new BackdropCommand { Background = new UrdfPresentationSettings { BackgroundMode = UrdfBackgroundMode.Urdf3D, Stage = new UrdfStageSettings { ModelFile = robot } }, Framing = new UrdfFraming() }.ToJson() } } };
            string second = Path.Combine(source, "Projects", "Other", "A.json"); Directory.CreateDirectory(Path.GetDirectoryName(second)); other.Save(second);
            string movie = Path.Combine(source, "Projects", "Travel", "Show.movie.json");
            new MovieDocument { Description = "Portable show", CreatedDate = "2024-03-04", Sequences = new() { Path.GetRelativePath(source, first), second, "Travel/A.json" }, SequenceLoops = new() { true, false, false }, SequenceTriggers = new() { "Ctrl+5", "", "Alt+6" } }.Save(movie);
            var originals = Snapshot(source); string robotBytes = File.ReadAllText(robot);
            string movieZip = Path.Combine(fixture, "show.zip"), sequenceZip = Path.Combine(fixture, "sequence.zip"), modelZip = Path.Combine(fixture, "model.zip");
            int count = ProjectPackage.Export(source, movie, movieZip, ProjectPackageKind.Movie);
            ProjectPackage.Export(source, first, sequenceZip, ProjectPackageKind.Sequence);
            ProjectPackage.Export(source, robot, modelZip, ProjectPackageKind.UrdfModel);
            Same(originals, source, "Export does not change source documents, scenes or media");
            Check(File.ReadAllText(robot) == robotBytes, "URDF export does not change the source model");
            var movieIndex = Index(movieZip);
            var modelIndex = Index(modelZip);
            Check(count == movieIndex.Files.Count && movieIndex.Files.Count(f => f.Role == "Sequence") == 2, "Movie export deduplicates repeated sequences");
            Check(movieIndex.Models.Count == 1 && movieIndex.Files.All(f => f.Role is not ("Urdf" or "ModelAsset")), "Movie ZIP references installed robot/scenery models without bundling them");
            Check(movieIndex.Files.Any(f => f.Path.EndsWith("custom-beam.wav")) && movieIndex.Files.Any(f => f.Path.EndsWith("Beam_Out.wav")), "Custom and built-in transporter sounds are packaged");
            Check(modelIndex.Version == 2 && modelIndex.Files.Count == 7, "Dependency-only URDF export contains only its seven runtime files");
            Check(modelIndex.Files.Any(f => f.Path.EndsWith("body part.obj")) && modelIndex.Files.Any(f => f.Path.EndsWith("tread_animation.json")) && modelIndex.Files.Any(f => f.Path.EndsWith("frame.stl")) && modelIndex.Files.Any(f => f.Path.EndsWith("fall_apart.json")), "URDF ZIP includes meshes, tread frames and disassembly/reassembly data");
            Check(modelIndex.Files.All(f => !new[] { "readme.md", "source.step", "unused.obj", "source.blend", "unused.stl", "old.backfaces.bin" }.Contains(Path.GetFileName(f.Path))), "Documentation, drawings, unused meshes/frames and stale masks are excluded even inside Meshes");
            string backdropModel = Path.Combine(modelFolder, "backdrop.urdf"), backdropZip = Path.Combine(fixture, "backdrop.zip");
            File.WriteAllText(backdropModel, "<robot name='stage'><link name='screen'><visual><geometry><box size='1 1 1'/></geometry></visual></link></robot>");
            ProjectPackage.Export(source, backdropModel, backdropZip, ProjectPackageKind.UrdfModel);
            Check(Index(backdropZip).Files.Count == 1, "A scenery model sharing the robot folder does not collect unrelated robot clips or tread frames");
            string legacyZip = Tamper(modelZip, (m, data) =>
            {
                m.Version = 1; byte[] bytes = Encoding.UTF8.GetBytes("legacy documentation"); string path = Path.GetDirectoryName(m.EntryFile).Replace('\\', '/') + "/Meshes/old-notes.md";
                data.Add("Config/" + path, bytes); m.Files.Add(new ProjectPackageFile { Path = path, Role = "ModelAsset", Size = bytes.Length, ModifiedUtc = DateTimeOffset.UtcNow, Sha256 = ProjectPackage.Hash(bytes) });
            });
            using (var legacy = ProjectPackageImport.Prepare(legacyZip, target, ProjectPackageKind.UrdfModel))
                Check(legacy.Files.Any(f => f.FileName.EndsWith("old-notes.md")), "Original version 1 URDF ZIPs remain importable with their reviewed legacy contents");
            File.Move(robot, robot + ".source"); // Make the source model unavailable, as on a receiving computer.
            Rejected(movieZip, target, ProjectPackageKind.Movie, "Missing installed models prevent movie import before any writes");
            using (var plan = ProjectPackageImport.Prepare(modelZip, target, ProjectPackageKind.UrdfModel))
            {
                Check(!plan.Overwrites.Any(), "First model import has no replacements"); plan.Commit();
                Check(ProjectPackage.ModelIdentity(plan.EntryFile) == identity, "Relocated model identity survives absolute file URI rewriting");
                Check(!File.ReadAllText(plan.EntryFile).Contains(modelFolder.Replace('\\', '/')), "Imported model has portable mesh references");
                string masks = Path.Combine(Path.GetDirectoryName(plan.EntryFile), "Meshes", "ExteriorOptimized", "manifest.json");
                Check(ProjectPackage.Text(ProjectPackage.ReadObject(masks), "UrdfSha256") == ProjectPackage.HashFile(plan.EntryFile), "Exterior manifest follows the portable URDF hash");
                Check(UrdfExteriorMeshes.Load(plan.EntryFile).Count == 1, "Referenced exterior masks and their validation snapshot survive relocation");
                var model = UrdfScene.Load(plan.EntryFile);
                Check(model.FallApartClip?.FrameCount == 2, "Imported disassembly/reassembly clip loads without its authoring files");
                model.SetFallApartFrame(1); model.TryTransformLinkPoint("arm", new System.Windows.Media.Media3D.Point3D(), out var moved);
                Check(model.FallApartActive && Math.Abs(moved.X - 1) < 1e-6, "Imported model can disassemble using the retained baked animation");
                model.SetFallApartFrame(0); model.SetFallApartFrame(null);
                Check(!model.FallApartActive, "Imported model can reassemble and return to joint control");
                var treadJson = ProjectPackage.ReadObject(Path.Combine(Path.GetDirectoryName(plan.EntryFile), "Meshes/Body/Treads/tread_animation.json"));
                Check(((System.Text.Json.Nodes.JsonArray)treadJson["sides"]["left"]["layers"]).Count == 1, "Only tread layers belonging to this URDF remain in the runtime manifest");
                model.SetTreadTravel(.1, .1);
            }
            var installed = Snapshot(target);
            using (var canceled = ProjectPackageImport.Prepare(movieZip, target, ProjectPackageKind.Movie))
            {
                Check(canceled.Files.Any(f => f.File.Path.EndsWith("hidden.wav")) && canceled.Files.Any(f => f.File.Path.EndsWith("external.wav")), "Hidden trimmed commands and external effect audio are included");
            }
            Same(installed, target, "Cancel/dispose imports no files");
            string oldSequence = Path.Combine(target, "Projects", "Travel", "A.json"); Directory.CreateDirectory(Path.GetDirectoryName(oldSequence)); File.WriteAllText(oldSequence, "original sequence");
            string oldAudio = Path.Combine(target, "Projects", "Travel", "speech.wav"); File.WriteAllBytes(oldAudio, new byte[] { 42, 43 });
            string oldMovie = Path.Combine(target, "Projects", "Travel", "Show.movie.json"); File.WriteAllText(oldMovie, "original movie");
            var oldTime = new DateTime(2011, 2, 3, 4, 5, 6, DateTimeKind.Utc);
            foreach (string file in new[] { oldSequence, oldAudio, oldMovie }) File.SetLastWriteTimeUtc(file, oldTime);
            using (var plan = ProjectPackageImport.Prepare(movieZip, target, ProjectPackageKind.Movie))
            {
                Check(plan.Overwrites.Count() == 3 && plan.Overwrites.All(f => f.ModifiedUtc == oldTime), "Review lists every replacement with existing modification dates");
                var audioReview = plan.Files.Single(f => f.Destination == oldAudio);
                Check(audioReview.ExistingSize == "2" && audioReview.IncomingSize == "5" && audioReview.ExistingModified != audioReview.IncomingModified, "Review compares exact existing/incoming sizes and timestamps");
                File.WriteAllText(oldSequence, "edited elsewhere");
                bool stale = false; try { plan.Commit(); } catch (IOException) { stale = true; }
                Check(stale && File.ReadAllText(oldSequence) == "edited elsewhere" && File.ReadAllText(oldMovie) == "original movie", "Changed destinations invalidate approval without overwriting files");
            }
            File.WriteAllText(oldSequence, "original sequence"); File.SetLastWriteTimeUtc(oldSequence, oldTime);
            var beforeRollback = Snapshot(target);
            using (var plan = ProjectPackageImport.Prepare(movieZip, target, ProjectPackageKind.Movie))
            using (var locked = new FileStream(oldMovie, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                bool failed = false; try { plan.Commit(); } catch (IOException) { failed = true; }
                Check(failed, "A locked replacement fails the import"); Same(beforeRollback, target, "Failed import restores overwritten files and removes newly installed files");
            }
            using (var plan = ProjectPackageImport.Prepare(movieZip, target, ProjectPackageKind.Movie))
            {
                plan.Commit();
                Check(plan.Files.All(f => File.GetLastWriteTimeUtc(f.Destination) == f.File.ModifiedUtc.UtcDateTime), "Import preserves incoming modification timestamps exactly");
                var loaded = MovieDocument.Load(plan.EntryFile);
                Check(loaded.Sequences.Count == 3 && loaded.Sequences[0] == loaded.Sequences[2] && loaded.SequenceLoops.SequenceEqual(new[] { true, false, false }) && loaded.SequenceTriggers[2] == "Alt+6", "Movie order, repeated blocks, loops and triggers round-trip");
                var imported = AnimationDocument.Load(oldSequence);
                Check(imported.Description == sequence.Description && imported.SplineServos.Contains("NeckTurn") && imported.Commands.Any(c => c.GroupId == "group-1" && c.BreakSpline), "Sequence editing metadata and commands round-trip");
                var importedScene = SavedSceneCommand.Read(target, imported.UrdfBackgroundFile);
                Check(ConfigPathService.IsWithin(target, importedScene.RobotModelFile) && ProjectPackage.ModelIdentity(importedScene.RobotModelFile) == identity, "Scene binds to the installed destination model");
                var stageSequence = AnimationDocument.Load(Path.Combine(target, loaded.Sequences[1]));
                var backdrop = BackdropCommand.Read(stageSequence.Commands[0].TextValue);
                Check(File.Exists(Path.GetFullPath(Path.Combine(target, backdrop.Background.Stage.ModelFile))), "Inline 3D scenery also binds to an installed model");
                Check(imported.Commands.First(c => c.Servo == ServoNames.Play).AudioTrim.HiddenCommands.Count == 2, "Trimmed hidden commands are retained");
                Check(imported.Commands.Where(c => c.Servo == ServoNames.FallApart).Select(c => c.TextValue).SequenceEqual(sequence.Commands.Where(c => c.Servo == ServoNames.FallApart).Select(c => c.TextValue)), "Disassemble/reassemble timeline commands retain their direction and speed");
                string firingSound = FireLaserCommand.Read(imported.Commands.Single(c => c.Servo == ServoNames.FireLaser).TextValue).SoundFile(target);
                string tvMedia = TvShowCommand.Read(imported.Commands.Single(c => c.Servo == ServoNames.TvShow).TextValue).FullPath(target);
                Check(File.Exists(firingSound) && ProjectPackage.HashFile(firingSound) == ProjectPackage.HashFile(externalSound) && File.Exists(tvMedia) && ProjectPackage.HashFile(tvMedia) == ProjectPackage.HashFile(video), "Movie import preserves the actual laser sound and TV video bytes at their rewritten paths");
            }
            using (var plan = ProjectPackageImport.Prepare(sequenceZip, target, ProjectPackageKind.Sequence))
            { Check(plan.Kind == ProjectPackageKind.Sequence && plan.Files.All(f => f.File.Role != "Movie"), "Sequence packages import independently without a movie"); plan.Commit(); }
            Check(!Directory.GetDirectories(target, ".j5-import-*").Any(), "Successful and rolled-back imports remove transaction folders");
            Rejected(sequenceZip, target, ProjectPackageKind.Movie, "Wrong package type is rejected");
            Rejected(Tamper(movieZip, (m, _) => m.Version = 999), target, ProjectPackageKind.Movie, "Unknown package version is rejected");
            foreach (string bad in new[] { "../escape.txt", "C:/escape.txt", "Projects/a.json:stream", "Projects/CON.json", "Projects/end. /x" })
                Rejected(Tamper(movieZip, (m, data) => { var f = m.Files[0]; byte[] bytes = data["Config/" + f.Path]; data.Remove("Config/" + f.Path); f.Path = bad; data.Add("Config/" + bad, bytes); }), target, ProjectPackageKind.Movie, "Unsafe ZIP path rejected: " + bad);
            Rejected(Tamper(movieZip, (m, data) => data.Add("Config/undeclared.bin", new byte[1])), target, ProjectPackageKind.Movie, "Undeclared ZIP files are rejected");
            Rejected(Tamper(movieZip, (m, data) => { string name = "Config/" + m.Files[0].Path; data.Add(name.ToUpperInvariant(), data[name]); }), target, ProjectPackageKind.Movie, "Case-colliding ZIP entries are rejected");
            Rejected(Tamper(movieZip, (m, data) => { string name = "Config/" + m.Files[0].Path; data[name][0] ^= 1; }), target, ProjectPackageKind.Movie, "Corrupt payload hashes are rejected");
            Rejected(Tamper(movieZip, (m, data) => data.Remove("Config/" + m.Files[0].Path)), target, ProjectPackageKind.Movie, "Missing ZIP payloads are rejected");
            var missing = new AnimationDocument { AudioFilePath = "missing.wav", Commands = new() }; string missingFile = Path.Combine(source, "Projects", "missing.json"); missing.Save(missingFile);
            string untouchedZip = Path.Combine(fixture, "keep.zip"); File.WriteAllText(untouchedZip, "original ZIP"); bool missingFailed = false;
            try { ProjectPackage.Export(source, missingFile, untouchedZip, ProjectPackageKind.Sequence); } catch (FileNotFoundException) { missingFailed = true; }
            Check(missingFailed && File.ReadAllText(untouchedZip) == "original ZIP", "Missing dependencies never replace an existing export with an incomplete ZIP");
            var app = new App(); app.InitializeComponent(); DisableTestStartup(app);
            string reloadImage = Path.Combine(target, "reload.png");
            void PixelFile(byte red, byte blue)
            {
                var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(1, 1, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, new byte[] { blue, 0, red, 255 }, 4);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using var output = File.Create(reloadImage); encoder.Save(output);
            }
            byte[] Pixel(System.Windows.Media.Imaging.BitmapSource bitmap)
            {
                var converted = new System.Windows.Media.Imaging.FormatConvertedBitmap(bitmap, System.Windows.Media.PixelFormats.Bgra32, null, 0);
                var pixel = new byte[4]; converted.CopyPixels(new Int32Rect(0, 0, 1, 1), pixel, 4, 0); return pixel;
            }
            var imageSettings = new UrdfPresentationSettings { BackgroundMode = UrdfBackgroundMode.Image, BackgroundImage = "reload.png" };
            var lcd = new LcdShowCommand { Image = "reload.png", Fit = TvFit.Stretch };
            PixelFile(255, 0);
            var oldBrush = (System.Windows.Media.ImageBrush)imageSettings.CreateBrush(target, System.Windows.Media.Brushes.Black);
            var oldLcd = LcdRenderer.Render(lcd, target, 1);
            PixelFile(0, 255); LcdRenderer.ClearCache();
            var newBrush = (System.Windows.Media.ImageBrush)imageSettings.CreateBrush(target, System.Windows.Media.Brushes.Black);
            var newLcd = LcdRenderer.Render(lcd, target, 1);
            Check(Pixel((System.Windows.Media.Imaging.BitmapSource)oldBrush.ImageSource)[2] == 255 && Pixel((System.Windows.Media.Imaging.BitmapSource)newBrush.ImageSource)[0] == 255,
                "Background reload reads replacement image bytes at the same path");
            Check(Pixel((System.Windows.Media.Imaging.BitmapSource)oldLcd)[2] == 255 && Pixel((System.Windows.Media.Imaging.BitmapSource)newLcd)[0] == 255,
                "LCD reload clears rendered and source image caches after replacement");
            using (var plan = ProjectPackageImport.Prepare(movieZip, target, ProjectPackageKind.Movie))
            {
                var review = new PackageImportReviewWindow(plan);
                var grid = SceneDescendants(review).OfType<DataGrid>().Single();
                Check(grid.Columns.Count == 5 && grid.Items.Count == plan.Files.Count && grid.Items.Cast<PackageImportFile>().Count(f => f.Exists) == plan.Overwrites.Count(), "Overwrite window includes all files and both date/size comparisons");
                Check(SceneDescendants(review).OfType<Button>().Any(b => Equals(b.Content, "Proceed")) && SceneDescendants(review).OfType<Button>().Any(b => Equals(b.Content, "Cancel")), "Import offers Proceed and Cancel");
                string previews = PreviewDirectory("package-previews"); Directory.CreateDirectory(previews);
                RenderControl((FrameworkElement)review.Content, Path.Combine(previews, "ImportReview.png"), 1080, 610);
                Check(grid.Columns[0].ActualWidth >= 340, "Import review keeps filenames readable alongside both comparisons");
                var main = new MainWindow();
                Check(SceneDescendants(main).OfType<MenuItem>().Count(m => m.Header?.ToString()?.Contains("ZIP…") == true) == 6 && !main.IsLoaded, "Movie, Sequence and URDF package commands are available without starting hardware");
            }
            UrdfRuntimePackageChecks(fixture, source);
            app.Shutdown();
        }
        finally { ProjectPackageImport.RemoveWork(fixture, parent, "checks-"); }
    }
}
