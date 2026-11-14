using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using ServoAnimator;

internal sealed class ModelMigrationRequest
{
    public string ConfigRoot { get; set; }
    public string BackupFolder { get; set; }
    public bool Commit { get; set; }
    public List<ModelMigrationSource> Models { get; set; } = new();
    public Dictionary<string, string> Aliases { get; set; } = new();
}
internal sealed class ModelMigrationSource
{
    public string Source { get; set; }
    public string Target { get; set; }
}

// Explicit maintenance tool, never run at editor startup or while an editor is open.
internal static class ModelStorageMigration
{
    internal static object Run(ModelMigrationRequest request)
    {
        string root = Path.GetFullPath(request.ConfigRoot), backup = Path.GetFullPath(request.BackupFolder);
        if (!Directory.Exists(root) || ConfigPathService.IsWithin(root, backup) || Directory.Exists(backup))
            throw new InvalidOperationException("Use an existing Config and a new backup folder outside it.");
        if (request.Models.Count == 0) throw new InvalidOperationException("Choose the runtime models explicitly.");
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string Resolve(string path) => Path.GetFullPath(path.StartsWith("file://", StringComparison.OrdinalIgnoreCase) ? new Uri(path).LocalPath : Path.Combine(root, path));
        foreach (var model in request.Models)
        {
            string target = ProjectPackage.SafeRelative(model.Target);
            if (!target.StartsWith(ModelStorage.Robots + "/", StringComparison.OrdinalIgnoreCase) && !target.StartsWith(ModelStorage.Scenery + "/", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("A model target must be under Models/Robots or Models/Scenery.");
            map.Add(Resolve(model.Source), target);
        }
        foreach (var alias in request.Aliases)
        {
            string target = ProjectPackage.SafeRelative(alias.Value);
            if (!map.Values.Contains(target, StringComparer.OrdinalIgnoreCase)) throw new InvalidOperationException("An alias must target an explicitly selected model.");
            // Existing aliases must be equivalent, rather than silently substituting a different model.
            string original = Resolve(alias.Key);
            if (File.Exists(original))
            {
                string source = request.Models.First(m => m.Target.Replace('\\', '/').Equals(target, StringComparison.OrdinalIgnoreCase)).Source;
                if (ProjectPackage.ModelIdentity(original) != ProjectPackage.ModelIdentity(Resolve(source))) throw new InvalidDataException("Alias identifies a different model: " + alias.Key);
            }
            map[original] = target;
        }
        string staging = Path.Combine(Path.GetDirectoryName(root), "model-migration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        var entries = new Dictionary<string, (string Staged, string OriginalHash, DateTime OriginalDate, bool Document)>(StringComparer.OrdinalIgnoreCase);
        var sourceHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int references = 0;
        var framingMerges = new List<string>();
        try
        {
            foreach (var model in request.Models)
            {
                string source = Resolve(model.Source);
                var builder = new ProjectPackage.Builder(root, modelTarget: model.Target);
                builder.Collect(source, ProjectPackageKind.UrdfModel);
                foreach (var item in builder.Sources.Values)
                {
                    sourceHashes[item.Source] = ProjectPackage.HashFile(item.Source);
                    byte[] content = item.Content ?? File.ReadAllBytes(item.Source);
                    string staged = ProjectPackage.Destination(staging, item.Target);
                    if (entries.ContainsKey(item.Target))
                    {
                        if (ProjectPackage.HashFile(staged) != ProjectPackage.Hash(content)) throw new InvalidDataException("Model targets conflict: " + item.Target);
                        continue;
                    }
                    string destination = ProjectPackage.Destination(root, item.Target);
                    if (File.Exists(destination) && ProjectPackage.HashFile(destination) != ProjectPackage.Hash(content))
                        throw new IOException("An existing runtime model would be replaced: " + item.Target);
                    Directory.CreateDirectory(Path.GetDirectoryName(staged)); File.WriteAllBytes(staged, content);
                    File.SetLastWriteTimeUtc(staged, item.ModifiedUtc.UtcDateTime);
                    entries.Add(item.Target, (staged, File.Exists(destination) ? ProjectPackage.HashFile(destination) : null, File.Exists(destination) ? File.GetLastWriteTimeUtc(destination) : default, false));
                }
            }
            // Validate dependency completeness from the staged copies, without access to source folders.
            foreach (var model in request.Models)
            {
                string staged = ProjectPackage.Destination(staging, model.Target);
                var graph = new ProjectPackage.Builder(staging, allowExternalAssets: false);
                graph.Collect(staged, ProjectPackageKind.UrdfModel);
                if (ProjectPackage.ModelIdentity(staged) != ProjectPackage.ModelIdentity(Resolve(model.Source))) throw new InvalidDataException("Model identity changed.");
            }
            string priorSelected = "";
            string defaults = Path.Combine(root, "UrdfModelSettings.json");
            if (File.Exists(defaults)) priorSelected = ProjectPackage.Text(ProjectPackage.ReadObject(defaults), "ModelFile");
            bool Rewrite(JsonNode node, string owner)
            {
                bool changed = false;
                if (node is JsonObject obj)
                {
                    // When multiple historical aliases identify one model, retain its currently selected framing.
                    var pairs = obj.ToArray().OrderBy(p => p.Key.EndsWith(".urdf", StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrWhiteSpace(priorSelected) && Resolve(p.Key).Equals(Resolve(priorSelected), StringComparison.OrdinalIgnoreCase) ? 1 : 0).ToArray();
                    foreach (var pair in pairs)
                    {
                        string key = pair.Key;
                        if (key.EndsWith(".urdf", StringComparison.OrdinalIgnoreCase) && map.TryGetValue(Resolve(key), out string newKey))
                        {
                            var child = pair.Value; obj.Remove(key);
                            if (obj.ContainsKey(newKey)) framingMerges.Add(owner + ": " + key + " -> " + newKey);
                            obj[newKey] = child; key = newKey; changed = true; references++;
                        }
                        if (pair.Value is JsonValue value && value.TryGetValue<string>(out string text))
                        {
                            if (text.EndsWith(".urdf", StringComparison.OrdinalIgnoreCase) && map.TryGetValue(Resolve(text), out string target))
                            { obj[key] = target; references++; changed = true; }
                            else if (text.TrimStart().StartsWith('{') || text.TrimStart().StartsWith('['))
                            {
                                JsonNode embedded = null;
                                try { embedded = JsonNode.Parse(text); } catch (JsonException) { }
                                if (embedded != null && Rewrite(embedded, owner)) { obj[key] = embedded.ToJsonString(ProjectPackage.JsonOptions); changed = true; }
                            }
                        }
                        else changed |= Rewrite(pair.Value, owner);
                    }
                }
                else if (node is JsonArray array) foreach (var child in array) changed |= Rewrite(child, owner);
                return changed;
            }
            foreach (string file in ProjectPackage.FilesIn(root).Where(f => Path.GetExtension(f).Equals(".json", StringComparison.OrdinalIgnoreCase) && !ConfigPathService.IsWithin(Path.Combine(root, "Models"), f)))
            {
                string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                var json = JsonNode.Parse(File.ReadAllText(file), documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                if (!Rewrite(json, relative)) continue;
                string staged = ProjectPackage.Destination(staging, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(staged)); File.WriteAllBytes(staged, ProjectPackage.Bytes(json));
                entries.Add(relative, (staged, ProjectPackage.HashFile(file), File.GetLastWriteTimeUtc(file), true));
            }
            var report = new
            {
                ConfigRoot = root, OriginalFoldersPreserved = true, request.Commit,
                Models = request.Models.Select(m => new { m.Source, m.Target, Identity = ProjectPackage.ModelIdentity(Resolve(m.Source)) }).ToArray(),
                Aliases = map, RuntimeFiles = entries.Count(e => !e.Value.Document),
                RuntimeBytes = entries.Where(e => !e.Value.Document).Sum(e => new FileInfo(e.Value.Staged).Length),
                UpdatedDocuments = entries.Where(e => e.Value.Document).Select(e => e.Key).ToArray(), ReferencesUpdated = references, FramingMerges = framingMerges,
                Files = entries.Select(e => new { Path = e.Key, Sha256 = ProjectPackage.HashFile(e.Value.Staged), Size = new FileInfo(e.Value.Staged).Length, e.Value.OriginalHash }).ToArray(),
                OriginalRuntimeSources = sourceHashes
            };
            if (!request.Commit) return report;
            void CheckUnchanged()
            {
                foreach (var item in sourceHashes) if (ProjectPackage.HashFile(item.Key) != item.Value) throw new IOException("A source model changed: " + item.Key);
                foreach (var item in entries)
                {
                    string path = ProjectPackage.Destination(root, item.Key);
                    if (Directory.Exists(path) || File.Exists(path) != (item.Value.OriginalHash != null) || (File.Exists(path) &&
                        (ProjectPackage.HashFile(path) != item.Value.OriginalHash || File.GetLastWriteTimeUtc(path) != item.Value.OriginalDate)))
                        throw new IOException("Config changed during migration: " + item.Key);
                }
            }
            CheckUnchanged();
            Directory.CreateDirectory(backup);
            foreach (var item in entries.Where(e => e.Value.OriginalHash != null))
            {
                string copy = ProjectPackage.Destination(backup, item.Key); Directory.CreateDirectory(Path.GetDirectoryName(copy));
                File.Copy(ProjectPackage.Destination(root, item.Key), copy);
            }
            File.WriteAllText(Path.Combine(backup, "migration-report.json"), JsonSerializer.Serialize(report, ProjectPackage.JsonOptions));
            CheckUnchanged();
            var installed = new List<string>();
            try
            {
                foreach (var item in entries.OrderBy(e => e.Value.Document ? 1 : 0))
                {
                    if (!item.Value.Document && item.Value.OriginalHash != null) continue;
                    string destination = ProjectPackage.Destination(root, item.Key); Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    if (item.Value.OriginalHash != null) File.Replace(item.Value.Staged, destination, null);
                    else File.Move(item.Value.Staged, destination);
                    installed.Add(item.Key);
                }
                foreach (var item in sourceHashes) if (ProjectPackage.HashFile(item.Key) != item.Value) throw new IOException("Original model changed unexpectedly.");
                File.WriteAllText(Path.Combine(backup, "COMPLETED.txt"), "Runtime copies installed and references updated. Complete source folders were preserved.\n");
            }
            catch
            {
                foreach (string relative in installed.AsEnumerable().Reverse())
                {
                    string destination = ProjectPackage.Destination(root, relative);
                    if (entries[relative].OriginalHash != null)
                    { File.Copy(ProjectPackage.Destination(backup, relative), destination, true); File.SetLastWriteTimeUtc(destination, entries[relative].OriginalDate); }
                    else File.Delete(destination);
                }
                throw;
            }
            return report;
        }
        finally { ProjectPackageImport.RemoveWork(staging, Path.GetDirectoryName(root), "model-migration-"); }
    }
}
