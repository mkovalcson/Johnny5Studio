using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;

namespace ServoAnimator;

internal enum ProjectPackageKind { Movie, Sequence, UrdfModel }

internal sealed class ProjectPackageManifest
{
    public string Format { get; set; } = "J5 Animation Package";
    public int Version { get; set; } = 1;
    public string Kind { get; set; }
    public string EntryFile { get; set; }
    public List<ProjectPackageFile> Files { get; set; } = new();
    public List<ProjectPackageModel> Models { get; set; } = new();
}

internal sealed class ProjectPackageFile
{
    public string Path { get; set; }
    public string Role { get; set; }
    public long Size { get; set; }
    public DateTimeOffset ModifiedUtc { get; set; }
    public string Sha256 { get; set; }
}

internal sealed class ProjectPackageModel
{
    public string Reference { get; set; }
    public string Name { get; set; }
    public string Identity { get; set; }
    public string RelativeHint { get; set; }
}

/// <summary>Portable, editable documents and their dependencies. URDFs are separate packages;
/// movie/sequence scene references are rebound to matching installed models during import.</summary>
internal static partial class ProjectPackage
{
    internal const string ManifestName = "J5Package.json";
    internal static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    internal static JsonObject ReadObject(string file) => ParseObject(File.ReadAllText(file));
    internal static JsonObject ParseObject(string text) => JsonNode.Parse(text, documentOptions: new JsonDocumentOptions
        { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }) as JsonObject
        ?? throw new InvalidDataException("Expected a JSON object.");
    internal static JsonNode Get(JsonObject obj, string key) => obj?.FirstOrDefault(p => p.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value;
    internal static string Text(JsonObject obj, string key) => Get(obj, key)?.GetValue<string>() ?? "";
    internal static void Set(JsonObject obj, string key, string value) => obj[obj.Select(p => p.Key).FirstOrDefault(k => k.Equals(key, StringComparison.OrdinalIgnoreCase)) ?? key] = value;
    internal static byte[] Bytes(JsonNode node) => Encoding.UTF8.GetBytes(node.ToJsonString(JsonOptions));
    internal static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    internal static string HashFile(string path) => ConfigurationAssets.HashFile(path);

    internal static string SafeRelative(string path) => ConfigurationAssets.SafeRelative(path);
    internal static string Destination(string root, string relative) => ConfigurationAssets.Destination(root, relative);
    internal static IEnumerable<string> FilesIn(string folder)
    {
        if (!Directory.Exists(folder)) yield break;
        foreach (string file in Directory.EnumerateFiles(folder).Order(StringComparer.OrdinalIgnoreCase))
        {
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Cannot package linked files: " + file);
            yield return file;
        }
        foreach (string child in Directory.EnumerateDirectories(folder).Order(StringComparer.OrdinalIgnoreCase))
            if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                foreach (string file in FilesIn(child)) yield return file;
    }
    private static XDocument ReadUrdf(string file)
    {
        using var reader = XmlReader.Create(file, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        var xml = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        if (xml.Root?.Name.LocalName != "robot") throw new InvalidDataException("Choose a URDF robot/model file: " + file);
        return xml;
    }
    internal static string ModelIdentity(string file)
    {
        var xml = ReadUrdf(file);
        string MeshName(string value) => Path.GetFileName((value.StartsWith("file://", StringComparison.OrdinalIgnoreCase) ? new Uri(value).LocalPath : value).Replace('\\', '/'));
        XElement Canonical(XElement element) => new(element.Name,
            element.Attributes().OrderBy(a => a.Name.ToString(), StringComparer.Ordinal).Select(a => new XAttribute(a.Name,
                a.Name.LocalName == "filename" && element.Name.LocalName is "mesh" or "texture"
                    ? MeshName(a.Value) : a.Value)),
            element.Nodes().Select(n => n is XElement child ? (object)Canonical(child) : n is XText text && !string.IsNullOrWhiteSpace(text.Value) ? new XText(text.Value.Trim()) : null));
        return Hash(Encoding.UTF8.GetBytes(Canonical(xml.Root).ToString(SaveOptions.DisableFormatting)));
    }

    internal static int Export(string root, string entryFile, string zipFile, ProjectPackageKind kind)
    {
        var builder = new Builder(root);
        string entry = builder.Collect(entryFile, kind);
        if (builder.Sources.Values.Any(s => string.Equals(s.Source, Path.GetFullPath(zipFile), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("The ZIP cannot replace a source file included in the package.");
        string temporary = Path.GetFullPath(zipFile) + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create))
            {
                var manifest = new ProjectPackageManifest { Version = kind == ProjectPackageKind.UrdfModel ? 2 : 1, Kind = kind.ToString(), EntryFile = entry, Models = builder.Models.Values.ToList() };
                foreach (var source in builder.Sources.Values.OrderBy(s => s.Target, StringComparer.OrdinalIgnoreCase))
                {
                    var item = zip.CreateEntry("Config/" + source.Target, CompressionLevel.Optimal);
                    item.LastWriteTime = new DateTimeOffset(Math.Clamp(source.ModifiedUtc.UtcDateTime.Ticks,
                        new DateTime(1980, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks, new DateTime(2107, 12, 31, 23, 59, 58, DateTimeKind.Utc).Ticks), TimeSpan.Zero);
                    using var input = source.Content == null ? (Stream)File.OpenRead(source.Source) : new MemoryStream(source.Content, writable: false);
                    using var payload = item.Open();
                    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    var buffer = new byte[1024 * 1024]; long length = 0; int count;
                    while ((count = input.Read(buffer)) > 0) { payload.Write(buffer, 0, count); hash.AppendData(buffer, 0, count); length += count; }
                    manifest.Files.Add(new ProjectPackageFile { Path = source.Target, Role = source.Role, Size = length, ModifiedUtc = source.ModifiedUtc, Sha256 = Convert.ToHexString(hash.GetHashAndReset()) });
                }
                var index = zip.CreateEntry(ManifestName);
                using var writer = index.Open(); JsonSerializer.Serialize(writer, manifest, JsonOptions);
            }
            File.Move(temporary, zipFile, overwrite: true);
            return builder.Sources.Count;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal sealed class SourceFile
    {
        internal string Source, Target, Role;
        internal byte[] Content;
        internal DateTimeOffset ModifiedUtc;
    }
    internal sealed partial class Builder
    {
        private readonly string _root;
        private readonly bool _allowExternalAssets;
        private readonly bool _legacyModelAssets;
        private readonly string _modelTarget;
        private readonly HashSet<string> _knownModels;
        private readonly Dictionary<string, SourceFile> _bySource = new(StringComparer.OrdinalIgnoreCase);
        internal readonly Dictionary<string, SourceFile> Sources = new(StringComparer.OrdinalIgnoreCase);
        internal readonly Dictionary<string, ProjectPackageModel> Models = new(StringComparer.OrdinalIgnoreCase);
        internal Builder(string root, bool allowExternalAssets = true, IEnumerable<string> knownModels = null, bool legacyModelAssets = false, string modelTarget = null)
        { _root = Path.GetFullPath(root); _allowExternalAssets = allowExternalAssets; _knownModels = knownModels?.ToHashSet(StringComparer.OrdinalIgnoreCase); _legacyModelAssets = legacyModelAssets; _modelTarget = modelTarget; }
        internal string Collect(string file, ProjectPackageKind kind) => kind switch
        { ProjectPackageKind.Movie => Movie(file), ProjectPackageKind.Sequence => Sequence(file), ProjectPackageKind.UrdfModel => Urdf(file), _ => throw new InvalidDataException("Unknown package type.") };
        private string Resolve(string stored, string owner, bool audio = false, bool movie = false)
        {
            if (stored.StartsWith("file://", StringComparison.OrdinalIgnoreCase)) stored = new Uri(stored).LocalPath;
            var paths = new List<string> { Path.IsPathRooted(stored) ? stored : Path.Combine(_root, stored) };
            if (audio) { paths.Add(Path.Combine(Path.GetDirectoryName(owner), Path.GetFileName(stored))); paths.Add(Path.Combine(_root, "Projects", Path.GetFileName(stored))); }
            if (movie) { paths.Add(Path.Combine(_root, "Projects", stored)); paths.Add(Path.Combine(Path.GetDirectoryName(owner), stored)); }
            foreach (string candidate in paths)
            {
                string path = Path.GetFullPath(candidate);
                if (!_allowExternalAssets && !ConfigPathService.IsWithin(_root, path)) continue;
                if (File.Exists(path)) return path;
            }
            throw new FileNotFoundException($"A file referenced by {Path.GetFileName(owner)} is missing: {stored}");
        }
        private SourceFile Add(string file, string role, string folder, string preferred = null)
        {
            file = Path.GetFullPath(file);
            if (!File.Exists(file)) throw new FileNotFoundException("Package source is missing.", file);
            if (!_allowExternalAssets && !ConfigPathService.IsWithin(_root, file)) throw new InvalidDataException("A package asset points outside Configuration.");
            if (_bySource.TryGetValue(file, out var prior) && preferred == null) return prior;
            string target = preferred ?? (ConfigPathService.IsWithin(_root, file) ? Path.GetRelativePath(_root, file)
                : Path.Combine(folder, Hash(Encoding.UTF8.GetBytes(file.ToUpperInvariant()))[..12], Path.GetFileName(file)));
            target = SafeRelative(target);
            if (Sources.TryGetValue(target, out var same))
            {
                if (!string.Equals(same.Source, file, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Two package sources would overwrite " + target);
                return same;
            }
            if (Sources.Count >= 100000) throw new InvalidDataException("The package contains too many files.");
            var source = new SourceFile { Source = file, Target = target, Role = role, ModifiedUtc = File.GetLastWriteTimeUtc(file) };
            Sources.Add(target, source); _bySource.TryAdd(file, source); return source;
        }
        private string Asset(string stored, string owner, string folder, bool audio = false) => Add(Resolve(stored, owner, audio), "Media", folder).Target;
        private string Movie(string file)
        {
            file = Path.GetFullPath(file); if (_bySource.TryGetValue(file, out var old)) return old.Target;
            var json = ReadObject(file);
            if (Get(json, "sequences") is not JsonArray sequences) throw new InvalidDataException("The selected file is not a movie.");
            var result = Add(file, "Movie", "Projects");
            for (int i = 0; i < sequences.Count; i++) sequences[i] = Sequence(Resolve(sequences[i].GetValue<string>(), file, movie: true));
            result.Content = Bytes(json); return result.Target;
        }
        private string Sequence(string file)
        {
            file = Path.GetFullPath(file); if (_bySource.TryGetValue(file, out var old)) return old.Target;
            var json = ReadObject(file);
            if (Get(json, "commands") is not JsonArray commands) throw new InvalidDataException("The selected file is not a sequence: " + file);
            var result = Add(file, "Sequence", "Projects");
            string audio = Text(json, "audioFilePath"); if (string.IsNullOrWhiteSpace(audio)) audio = Text(json, "audioFile");
            if (!string.IsNullOrWhiteSpace(audio)) { string path = Asset(audio, file, "Library/Audio", true); Set(json, "audioFilePath", path); Set(json, "audioFile", Path.GetFileName(path)); }
            string scene = Text(json, "urdfBackgroundFile"); if (!string.IsNullOrWhiteSpace(scene)) Set(json, "urdfBackgroundFile", Scene(Resolve(scene, file)));
            Commands(commands, file);
            // The audioFiles header contains names, not playable file references.
            var names = new List<string>(); if (!string.IsNullOrWhiteSpace(audio)) names.Add(Text(json, "audioFile"));
            names.AddRange(commands.OfType<JsonObject>().Where(c => Text(c, "servo").Equals("Play", StringComparison.OrdinalIgnoreCase)).Select(c => Path.GetFileName(Text(c, "value"))));
            Set(json, "audioFiles", string.Join('|', names));
            result.Content = Bytes(json); return result.Target;
        }
        private void Commands(JsonArray commands, string owner)
        {
            foreach (var command in commands.OfType<JsonObject>())
            {
                if (Get(Get(command, "audioTrim") as JsonObject, "HiddenCommands") is JsonArray hidden) Commands(hidden, owner);
                string servo = Text(command, "servo");
                if (Get(command, "value") is not JsonValue v || !v.TryGetValue<string>(out string text) || string.IsNullOrWhiteSpace(text) || text.Equals("Disable", StringComparison.OrdinalIgnoreCase)) continue;
                if (servo.Equals("Play", StringComparison.OrdinalIgnoreCase)) { Set(command, "value", Asset(text, owner, "Library/Audio", true)); continue; }
                if (!new[] { "LoadBackdrop", "VideoClip", "TvShow", "LcdShow", "FireLaser", "UrdfBeam" }.Contains(servo, StringComparer.OrdinalIgnoreCase)) continue;
                var settings = ParseObject(text);
                if (servo.Equals("LoadBackdrop", StringComparison.OrdinalIgnoreCase)) Backdrop(settings, owner);
                else if (servo.Equals("VideoClip", StringComparison.OrdinalIgnoreCase) || servo.Equals("TvShow", StringComparison.OrdinalIgnoreCase))
                { string path = Text(settings, "path"); if (!string.IsNullOrWhiteSpace(path)) Set(settings, "path", Asset(path, owner, servo.Equals("VideoClip", StringComparison.OrdinalIgnoreCase) ? "VideoMedia" : "TvMedia")); }
                else if (servo.Equals("LcdShow", StringComparison.OrdinalIgnoreCase))
                { string path = Text(settings, "image"); if (!string.IsNullOrWhiteSpace(path)) Set(settings, "image", Asset(path, owner, "LcdMedia")); }
                else if (servo.Equals("UrdfBeam", StringComparison.OrdinalIgnoreCase))
                {
                    string sound = Text(settings, "sound");
                    if (string.IsNullOrWhiteSpace(sound) || sound.Equals(BeamCommand.DefaultSound, StringComparison.OrdinalIgnoreCase))
                    {
                        string leaf = Text(settings, "direction").Equals("Out", StringComparison.OrdinalIgnoreCase) ? "Beam_Out.wav" : "Beam_In.wav";
                        string builtIn = Path.Combine(_root, "Library", "Audio", leaf);
                        if (File.Exists(builtIn)) Add(builtIn, "Media", "Library/Audio");
                    }
                    else if (!sound.Equals(BeamCommand.NoSound, StringComparison.OrdinalIgnoreCase)) Set(settings, "sound", Asset(sound, owner, "Library/Audio", true));
                }
                else
                {
                    string sound = Text(settings, "sound");
                    if (string.IsNullOrWhiteSpace(sound)) sound = FireLaserCommand.DefaultSound;
                    if (FireLaserCommand.BuiltInSounds.TryGetValue(sound, out string leaf))
                    { string builtIn = Path.Combine(_root, "Library", "Audio", leaf); if (File.Exists(builtIn)) Add(builtIn, "Media", "Library/Audio"); }
                    else if (!sound.Equals(FireLaserCommand.NoSound, StringComparison.OrdinalIgnoreCase)) Set(settings, "sound", Asset(sound, owner, "Library/Audio", true));
                }
                Set(command, "value", settings.ToJsonString(JsonOptions));
            }
        }
        private string Scene(string file)
        {
            if (_bySource.TryGetValue(file, out var prior)) return prior.Target;
            var json = ReadObject(file);
            if (Get(json, "Settings") is not JsonObject settings) throw new InvalidDataException("Scene/background settings are missing: " + file);
            var result = Add(file, "Scene", string.IsNullOrWhiteSpace(Text(json, "RobotModelFile")) ? "LibraryBackgrounds" : "LibraryScenes");
            string model = Text(json, "RobotModelFile"); if (!string.IsNullOrWhiteSpace(model)) Set(json, "RobotModelFile", ModelReference(model, file));
            Backdrop(settings, file); result.Content = Bytes(json); return result.Target;
        }
        private void Backdrop(JsonObject settings, string owner)
        {
            string scene = Text(settings, "SceneFile");
            if (!string.IsNullOrWhiteSpace(scene)) { Set(settings, "SceneFile", Scene(Resolve(scene, owner))); return; }
            if (Get(settings, "Background") is not JsonObject background) return;
            string mode = Text(background, "BackgroundMode");
            if (mode.Equals("Image", StringComparison.OrdinalIgnoreCase) || mode.Equals("MovingImage", StringComparison.OrdinalIgnoreCase))
            {
                string source = Resolve(Text(background, "BackgroundImage"), owner);
                string preferred = _allowExternalAssets ? UrdfPresentationSettings.ImageReference(_root, source) : null;
                Set(background, "BackgroundImage", Add(source, "Media", UrdfPresentationSettings.ImageFolder, preferred).Target);
            }
            if (mode.Equals("Urdf3D", StringComparison.OrdinalIgnoreCase) && Get(background, "Stage") is JsonObject stage)
            {
                Set(stage, "ModelFile", ModelReference(Text(stage, "ModelFile"), owner));
                if (Get(stage, "Props") is JsonArray props)
                    foreach (var prop in props.OfType<JsonObject>())
                        Set(prop, "ModelFile", ModelReference(Text(prop, "ModelFile"), owner));
            }
        }
        private string ModelReference(string stored, string owner)
        {
            if (!_allowExternalAssets)
            {
                if (_knownModels?.Contains(stored) == true) return stored;
                throw new InvalidDataException("A scene contains an undeclared installed-model reference.");
            }
            // Models may live outside Config; they are referenced, never included in movie/sequence packages.
            string file = stored.StartsWith("file://", StringComparison.OrdinalIgnoreCase) ? new Uri(stored).LocalPath : Path.GetFullPath(Path.Combine(_root, stored));
            if (!File.Exists(file)) throw new FileNotFoundException("Referenced URDF model is missing. Install the model before exporting.", file);
            string identity = ModelIdentity(file);
            string reference = "Models/Installed/" + identity[..12] + "/" + Path.GetFileName(file);
            Models.TryAdd(reference, new ProjectPackageModel { Reference = reference, Name = Path.GetFileName(file), Identity = identity, RelativeHint = Path.GetRelativePath(_root, file) });
            return reference;
        }
        // Retained only for validating version 1 model ZIPs produced before dependency-only export.
        private string LegacyUrdf(string file)
        {
            file = Path.GetFullPath(file);
            var xml = ReadUrdf(file);
            string sourceFolder = Path.GetDirectoryName(file);
            string target = ConfigPathService.IsWithin(_root, file) ? Path.GetRelativePath(_root, file)
                : "Models/" + Path.GetFileName(sourceFolder) + "-" + Hash(Encoding.UTF8.GetBytes(sourceFolder.ToUpperInvariant()))[..12] + "/" + Path.GetFileName(file);
            var result = Add(file, "Urdf", "Models", target);
            string targetFolder = Path.GetDirectoryName(result.Target).Replace('\\', '/');
            string ModelAsset(string source)
            {
                string desired = ConfigPathService.IsWithin(sourceFolder, source) ? targetFolder + "/" + Path.GetRelativePath(sourceFolder, source).Replace('\\', '/') : null;
                return Add(source, "ModelAsset", "Models/Shared", desired).Target;
            }
            foreach (string asset in FilesIn(Path.Combine(sourceFolder, "Meshes"))) ModelAsset(asset);
            bool changed = false;
            foreach (var attribute in xml.Descendants().Where(e => e.Name.LocalName is "mesh" or "texture").Select(e => e.Attribute("filename")).Where(a => a != null))
            {
                string source = attribute.Value.StartsWith("file://", StringComparison.OrdinalIgnoreCase) ? new Uri(attribute.Value).LocalPath : Path.GetFullPath(Path.Combine(sourceFolder, attribute.Value.Replace('/', Path.DirectorySeparatorChar)));
                string asset = ModelAsset(source);
                string relative = Path.GetRelativePath(Path.Combine(_root, targetFolder), Path.Combine(_root, asset)).Replace('\\', '/');
                if (relative != attribute.Value.Replace('\\', '/')) { attribute.Value = relative; changed = true; }
            }
            string treadManifest = Path.Combine(sourceFolder, "Meshes", "Body", "Treads", "tread_animation.json");
            if (File.Exists(treadManifest))
            {
                void Frames(JsonNode node)
                {
                    if (node is JsonObject obj) foreach (var item in obj.ToArray())
                    {
                        if (item.Key == "frames" && item.Value is JsonArray frames)
                            foreach (var frame in frames) ModelAsset(Path.GetFullPath(Path.Combine(sourceFolder, frame.GetValue<string>())));
                        else Frames(item.Value);
                    }
                    else if (node is JsonArray array) foreach (var child in array) Frames(child);
                }
                Frames(ReadObject(treadManifest));
            }
            if (changed)
            {
                using var output = new MemoryStream(); xml.Save(output, SaveOptions.DisableFormatting); result.Content = output.ToArray();
                string manifest = Path.Combine(sourceFolder, "Meshes", "ExteriorOptimized", "manifest.json");
                if (File.Exists(manifest))
                {
                    var json = ReadObject(manifest); Set(json, "UrdfSha256", Hash(result.Content));
                    Add(manifest, "ModelAsset", "Models", targetFolder + "/Meshes/ExteriorOptimized/manifest.json").Content = Bytes(json);
                }
            }
            return result.Target;
        }
    }
}
