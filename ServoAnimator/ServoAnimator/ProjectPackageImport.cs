using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;

namespace ServoAnimator;

internal sealed class PackageImportFile
{
    public string FileName => File.Path;
    public string ExistingModified => Exists ? new DateTimeOffset(ModifiedUtc).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz") : "—";
    public string ExistingSize => Exists ? Size.ToString("N0") : "—";
    public string IncomingModified => File.ModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz");
    public string IncomingSize => File.Size.ToString("N0");
    internal ProjectPackageFile File;
    internal string Destination, Staged, ExistingHash;
    internal bool Exists;
    internal long Size;
    internal DateTime ModifiedUtc;
}

/// <summary>Owns a verified staged package. Preparing or canceling never changes destination files.
/// Commit checks the reviewed snapshot again and rolls back replacements on failure.</summary>
internal sealed class ProjectPackageImport : IDisposable
{
    internal string ConfigRoot { get; }
    internal string EntryFile { get; }
    internal ProjectPackageKind Kind { get; }
    internal IReadOnlyList<PackageImportFile> Files { get; private set; }
    internal IEnumerable<PackageImportFile> Overwrites => Files.Where(f => f.Exists);
    private readonly string _staging;
    private bool _committed, _disposed;

    private ProjectPackageImport(string root, string staging, ProjectPackageManifest manifest, ProjectPackageKind kind)
    { ConfigRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)); _staging = staging; Kind = kind; EntryFile = ProjectPackage.Destination(ConfigRoot, manifest.EntryFile); }

    internal static ProjectPackageImport Prepare(string zipFile, string root, ProjectPackageKind expectedKind)
    {
        root = Path.GetFullPath(root);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("The Configuration folder is unavailable.");
        string staging = Path.Combine(Path.GetTempPath(), "J5Package-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            using var input = File.OpenRead(zipFile);
            using var zip = new ZipArchive(input, ZipArchiveMode.Read);
            var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in zip.Entries)
            {
                string name = ProjectPackage.SafeRelative(entry.FullName);
                if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000 || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0 || !entries.TryAdd(name, entry))
                    throw new InvalidDataException("The ZIP contains linked or duplicate entries: " + name);
            }
            if (!entries.TryGetValue(ProjectPackage.ManifestName, out var index) || index.Length > 8 * 1024 * 1024)
                throw new InvalidDataException("Choose a ZIP exported by J5 Animation Editor (package index missing or too large).");
            ProjectPackageManifest manifest;
            using (var stream = index.Open()) manifest = JsonSerializer.Deserialize<ProjectPackageManifest>(stream, ProjectPackage.JsonOptions);
            if (manifest?.Format != "J5 Animation Package" || manifest.Version is not (1 or 2) || !Enum.TryParse<ProjectPackageKind>(manifest.Kind, out var kind) || !Enum.IsDefined(kind))
                throw new InvalidDataException("Unsupported J5 package format or version.");
            if (kind != expectedKind) throw new InvalidDataException($"This ZIP contains a {kind} package. Use its corresponding Import command.");
            if (manifest.Files == null || manifest.Files.Count == 0 || manifest.Files.Count > 100000 || manifest.Models == null || manifest.Models.Count > 10000)
                throw new InvalidDataException("The package file list is invalid.");
            string entryName = ProjectPackage.SafeRelative(manifest.EntryFile);
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long total = 0;
            foreach (var file in manifest.Files)
            {
                file.Path = ProjectPackage.SafeRelative(file.Path);
                if (!paths.Add(file.Path) || file.Size < 0 || file.Sha256?.Length != 64 || !file.Sha256.All(Uri.IsHexDigit) || file.ModifiedUtc.UtcDateTime.Year < 1601 ||
                    !new[] { "Movie", "Sequence", "Scene", "Media", "Urdf", "ModelAsset" }.Contains(file.Role))
                    throw new InvalidDataException("Invalid or duplicate package file: " + file.Path);
                total = checked(total + file.Size);
                string target = ProjectPackage.Destination(root, file.Path);
                if (Directory.Exists(target)) throw new InvalidDataException("A folder blocks an imported file: " + file.Path);
                if (!entries.TryGetValue("Config/" + file.Path, out var payload) || payload.Length != file.Size)
                    throw new InvalidDataException("Missing or incomplete ZIP file: " + file.Path);
                string staged = ProjectPackage.Destination(staging, file.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(staged));
                using (var source = payload.Open())
                using (var destination = new FileStream(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None)) source.CopyTo(destination);
                if (!string.Equals(ProjectPackage.HashFile(staged), file.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("ZIP integrity check failed: " + file.Path);
            }
            if (entries.Count != manifest.Files.Count + 1 || !paths.Contains(entryName)) throw new InvalidDataException("The ZIP contains undeclared files or its main document is missing.");
            string role = kind == ProjectPackageKind.UrdfModel ? "Urdf" : kind.ToString();
            if (manifest.Files.Single(f => f.Path.Equals(entryName, StringComparison.OrdinalIgnoreCase)).Role != role)
                throw new InvalidDataException("The package's main document has the wrong type.");
            var references = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var model in manifest.Models)
            {
                if (model == null || !references.Add(ProjectPackage.SafeRelative(model.Reference)) || model.Identity?.Length != 64 || !model.Identity.All(Uri.IsHexDigit) || string.IsNullOrWhiteSpace(model.Name) || Path.GetFileName(model.Name) != model.Name)
                    throw new InvalidDataException("Invalid installed-model information.");
            }
            // Traverse references inside the staged Config. No unlisted asset or external path may be used.
            var graph = new ProjectPackage.Builder(staging, allowExternalAssets: false, references, legacyModelAssets: kind == ProjectPackageKind.UrdfModel && manifest.Version == 1);
            graph.Collect(ProjectPackage.Destination(staging, entryName), kind);
            if (!paths.SetEquals(graph.Sources.Keys)) throw new InvalidDataException("The ZIP does not contain exactly the files required by its main document.");
            var resolved = ResolveModels(root, manifest.Models);
            if (resolved.Count > 0)
            {
                foreach (var file in manifest.Files.Where(f => f.Role is "Scene" or "Sequence"))
                {
                    string staged = ProjectPackage.Destination(staging, file.Path);
                    var json = ProjectPackage.ReadObject(staged);
                    RewriteModels(json, resolved);
                    byte[] content = ProjectPackage.Bytes(json); File.WriteAllBytes(staged, content);
                    // Review the bytes that will actually be installed, including rewritten scene paths.
                    file.Size = content.LongLength; file.Sha256 = ProjectPackage.Hash(content);
                }
            }
            var plan = new ProjectPackageImport(root, staging, manifest, kind);
            plan.Files = manifest.Files.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase).Select(file =>
            {
                string target = ProjectPackage.Destination(root, file.Path);
                var info = new FileInfo(target);
                var item = new PackageImportFile { File = file, Staged = ProjectPackage.Destination(staging, file.Path), Destination = target,
                    Exists = info.Exists, Size = info.Exists ? info.Length : 0, ModifiedUtc = info.Exists ? info.LastWriteTimeUtc : default };
                if (item.Exists) item.ExistingHash = ProjectPackage.HashFile(target);
                return item;
            }).ToArray();
            plan.CheckUnchanged();
            return plan;
        }
        catch { RemoveWork(staging, Path.GetTempPath(), "J5Package-"); throw; }
    }

    private static Dictionary<string, string> ResolveModels(string root, IEnumerable<ProjectPackageModel> models)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var items = models.ToArray(); if (items.Length == 0) return result;
        // Prefer managed copies over legacy authoring folders or an old ZIP's hints.
        var candidates = new List<string>();
        foreach (string folder in new[] { ModelStorage.Folder(root, false), ModelStorage.Folder(root, true) }.Where(Directory.Exists))
            candidates.AddRange(Directory.EnumerateFiles(folder, "*.urdf", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }));
        foreach (var model in items)
        {
            candidates.Add(Path.Combine(root, model.Reference));
            if (!string.IsNullOrWhiteSpace(model.RelativeHint) && !Path.IsPathRooted(model.RelativeHint) && !model.RelativeHint.Contains(':'))
                candidates.Add(Path.GetFullPath(Path.Combine(root, model.RelativeHint)));
        }
        try { var defaults = UrdfModelSettings.Load(root); candidates.Add(defaults.SelectedFile); candidates.AddRange(defaults.Framing.Keys); } catch { }
        string parent = Path.GetDirectoryName(root);
        var folders = new[] { root, Path.Combine(AppContext.BaseDirectory, "Models"), Path.Combine(AppContext.BaseDirectory, "FullBodyModel"),
            Path.Combine(parent, "Models"), Path.Combine(parent, "FullBodyModel"), Path.Combine(parent, "ServoAnimator", "Models"), Path.Combine(parent, "ServoAnimator", "FullBodyModel") };
        foreach (string folder in folders.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
            candidates.AddRange(Directory.EnumerateFiles(folder, "*.urdf", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }));
        var identities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase).Where(File.Exists))
            try { identities.TryAdd(ProjectPackage.ModelIdentity(candidate), Path.GetFullPath(candidate)); } catch (Exception ex) when (ex is IOException or XmlException or InvalidDataException or UnauthorizedAccessException) { }
        foreach (var model in items)
        {
            if (!identities.TryGetValue(model.Identity, out string found))
                throw new FileNotFoundException($"The installed URDF model {model.Name} could not be matched. Import its URDF Model ZIP first, then import this {nameof(ProjectPackageKind.Movie)}/Sequence ZIP again.");
            result.Add(model.Reference, Path.GetRelativePath(root, found).Replace('\\', '/'));
        }
        return result;
    }
    private static void RewriteModels(JsonNode node, IReadOnlyDictionary<string, string> resolved)
    {
        if (node is JsonObject obj)
        {
            foreach (var pair in obj.ToArray())
            {
                if (pair.Value is JsonValue value && value.TryGetValue<string>(out string text))
                {
                    if ((pair.Key.Equals("RobotModelFile", StringComparison.OrdinalIgnoreCase) || pair.Key.Equals("ModelFile", StringComparison.OrdinalIgnoreCase)) && resolved.TryGetValue(text, out string target)) obj[pair.Key] = target;
                    else if (pair.Key.Equals("value", StringComparison.OrdinalIgnoreCase) && resolved.Keys.Any(text.Contains))
                    { var settings = ProjectPackage.ParseObject(text); RewriteModels(settings, resolved); obj[pair.Key] = settings.ToJsonString(ProjectPackage.JsonOptions); }
                }
                else RewriteModels(pair.Value, resolved);
            }
        }
        else if (node is JsonArray array) foreach (var child in array) RewriteModels(child, resolved);
    }
    private void CheckUnchanged()
    {
        foreach (var item in Files) CheckUnchanged(item);
    }
    private void CheckUnchanged(PackageImportFile item)
    {
        string target = ProjectPackage.Destination(ConfigRoot, item.File.Path);
        var current = new FileInfo(target);
        if (Directory.Exists(target) || current.Exists != item.Exists || (item.Exists && (current.Length != item.Size || current.LastWriteTimeUtc != item.ModifiedUtc || ProjectPackage.HashFile(target) != item.ExistingHash)))
            throw new IOException("A destination changed after the overwrite review was prepared. Start import again: " + item.File.Path);
    }
    internal void Commit()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_committed) throw new InvalidOperationException("This package has already been imported.");
        CheckUnchanged();
        foreach (var file in Files.Where(f => f.Exists))
            if ((File.GetAttributes(file.Destination) & FileAttributes.ReadOnly) != 0) throw new IOException("An import destination is read-only: " + file.File.Path);
        string transaction = ProjectPackage.Destination(ConfigRoot, ".j5-import-" + Guid.NewGuid().ToString("N"));
        var completed = new List<PackageImportFile>();
        var createdDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool retainBackup = false;
        void EnsureParent(string path)
        {
            var missing = new Stack<string>();
            for (string folder = Path.GetDirectoryName(path); !Directory.Exists(folder); folder = Path.GetDirectoryName(folder)) missing.Push(folder);
            while (missing.Count > 0) { string folder = missing.Pop(); Directory.CreateDirectory(folder); createdDirectories.Add(folder); }
        }
        try
        {
            Directory.CreateDirectory(transaction);
            foreach (var file in Files)
            {
                string staged = ProjectPackage.Destination(transaction, "new/" + file.File.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(staged)); File.Copy(file.Staged, staged);
                if (ProjectPackage.HashFile(staged) != file.File.Sha256) throw new InvalidDataException("A staged file changed before import: " + file.File.Path);
                File.SetLastWriteTimeUtc(staged, file.File.ModifiedUtc.UtcDateTime);
            }
            CheckUnchanged();
            // Assets first; editable documents are installed after their dependencies.
            foreach (var file in Files.OrderBy(f => f.File.Role is "Movie" or "Sequence" or "Scene" ? 1 : 0))
            {
                CheckUnchanged(file);
                string target = ProjectPackage.Destination(ConfigRoot, file.File.Path);
                EnsureParent(target);
                string staged = ProjectPackage.Destination(transaction, "new/" + file.File.Path);
                if (file.Exists)
                {
                    string backup = ProjectPackage.Destination(transaction, "backup/" + file.File.Path);
                    Directory.CreateDirectory(Path.GetDirectoryName(backup)); File.Replace(staged, target, backup);
                }
                else File.Move(staged, target); // An unexpected newly-created destination must not be overwritten.
                completed.Add(file);
            }
            _committed = true;
        }
        catch (Exception failure)
        {
            var rollbackFailures = new List<Exception>();
            foreach (var file in completed.AsEnumerable().Reverse())
                try
                {
                    if (file.Exists)
                    {
                        string backup = ProjectPackage.Destination(transaction, "backup/" + file.File.Path);
                        if (File.Exists(file.Destination)) File.Replace(backup, file.Destination, null);
                        else File.Move(backup, file.Destination);
                    }
                    else File.Delete(file.Destination);
                }
                catch (Exception ex) { rollbackFailures.Add(ex); }
            foreach (string folder in createdDirectories.OrderByDescending(p => p.Length))
                try { if (ConfigPathService.IsWithin(ConfigRoot, folder) && Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder); } catch { }
            if (rollbackFailures.Count > 0)
            {
                retainBackup = true;
                throw new IOException("Import failed and some files could not be restored. Original backups remain in " + transaction, new AggregateException(new[] { failure }.Concat(rollbackFailures)));
            }
            throw new IOException("Import failed; replaced files were restored. " + failure.Message, failure);
        }
        finally { if (!retainBackup) RemoveWork(transaction, ConfigRoot, ".j5-import-"); }
    }
    internal static void RemoveWork(string path, string parent, string prefix)
    {
        string full = Path.GetFullPath(path), expectedParent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
        if (!string.Equals(Path.GetDirectoryName(full), expectedParent, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith(prefix, StringComparison.Ordinal) || !Guid.TryParseExact(Path.GetFileName(full)[prefix.Length..], "N", out _))
            throw new InvalidOperationException("Refusing to remove a directory outside this import's working folder.");
        if (Directory.Exists(full)) Directory.Delete(full, recursive: true);
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        RemoveWork(_staging, Path.GetTempPath(), "J5Package-");
    }
}
