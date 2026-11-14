using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace ServoAnimator;

internal static partial class ProjectPackage
{
    internal sealed partial class Builder
    {
        private string Urdf(string file)
        {
            if (_legacyModelAssets) return LegacyUrdf(file);
            file = Path.GetFullPath(file);
            var xml = ReadUrdf(file);
            string sourceFolder = Path.GetDirectoryName(file);
            string target = _modelTarget ?? (_allowExternalAssets ? ModelStorage.PackageTarget(_root, file, xml) : Path.GetRelativePath(_root, file));
            var result = Add(file, "Urdf", "Models", target);
            string targetFolder = Path.GetDirectoryName(result.Target)?.Replace('\\', '/') ?? "";
            string ResolveAsset(string stored) => stored.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
                ? new Uri(stored).LocalPath : Path.GetFullPath(Path.Combine(sourceFolder, stored.Replace('/', Path.DirectorySeparatorChar)));
            SourceFile ModelAsset(string source)
            {
                source = Path.GetFullPath(source);
                string desired = ConfigPathService.IsWithin(sourceFolder, source)
                    ? Path.Combine(targetFolder, Path.GetRelativePath(sourceFolder, source)) : null;
                return Add(source, "ModelAsset", "Models/Shared", desired);
            }
            string RelativeAsset(string source) => Path.GetRelativePath(Path.Combine(_root, targetFolder), Path.Combine(_root, ModelAsset(source).Target)).Replace('\\', '/');
            string LocalAsset(string stored)
            {
                string source = ResolveAsset(stored);
                if (!ConfigPathService.IsWithin(sourceFolder, source)) throw new InvalidDataException("Model animation/optimization data must stay inside its model folder: " + stored);
                return source;
            }
            // Keep every authored visual, including currently hidden laser/toolbox parts, and collision geometry.
            var visuals = xml.Root.Elements("link").SelectMany(link => link.Elements("visual").Select(visual => new
            {
                Key = (string)link.Attribute("name") + "/" + ((string)visual.Attribute("name") ?? ""),
                Source = (string)visual.Element("geometry")?.Element("mesh")?.Attribute("filename")
            })).Where(v => v.Source != null).GroupBy(v => v.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Last().Source, StringComparer.Ordinal);
            var links = xml.Root.Elements("link").Select(l => (string)l.Attribute("name")).ToHashSet(StringComparer.Ordinal);
            var movingLinks = xml.Root.Elements("joint").Select(j => (string)j.Element("child")?.Attribute("link")).ToHashSet(StringComparer.Ordinal);
            var rewrites = new Dictionary<string, string>(StringComparer.Ordinal);
            bool changed = false;
            foreach (var attribute in xml.Descendants().Where(e => e.Name.LocalName is "mesh" or "texture").Select(e => e.Attribute("filename")).Where(a => a != null))
            {
                string original = attribute.Value, relative = RelativeAsset(ResolveAsset(original));
                rewrites[original] = relative;
                if (relative != original) { attribute.Value = relative; changed = true; }
            }
            if (changed)
            {
                using var output = new MemoryStream(); xml.Save(output, SaveOptions.DisableFormatting); result.Content = output.ToArray();
            }

            string fallApart = Path.Combine(sourceFolder, FallApartClip.RelativePath);
            if (File.Exists(fallApart) && FallApartClip.Parse(File.ReadAllText(fallApart)).Links.Any(movingLinks.Contains)) ModelAsset(fallApart);

            string treads = Path.Combine(sourceFolder, "Meshes", "Body", "Treads", "tread_animation.json");
            if (File.Exists(treads))
            {
                var json = ReadObject(treads);
                var sides = new JsonObject();
                foreach (string sideName in new[] { "left", "right" })
                {
                    if (Get(Get(json, "sides") as JsonObject, sideName) is not JsonObject side) continue;
                    var layers = new JsonArray();
                    if (Get(side, "layers") is JsonArray originalLayers)
                        foreach (var layer in originalLayers.OfType<JsonObject>())
                        {
                            string link = Text(layer, "urdfLink"), visual = Text(layer, "urdfVisual");
                            if (!links.Contains(link) || !visuals.ContainsKey(link + "/" + visual)) continue;
                            if (Get(layer, "frames") is not JsonArray frames || frames.Count == 0) throw new InvalidDataException("Tread animation has no frames: " + visual);
                            var copy = (JsonObject)layer.DeepClone(); var portableFrames = new JsonArray();
                            foreach (var frame in frames) portableFrames.Add(RelativeAsset(LocalAsset(frame.GetValue<string>())));
                            Node(copy, "frames", portableFrames); layers.Add(copy);
                        }
                    var rotators = new JsonArray();
                    if (Get(side, "rotators") is JsonArray originalRotators)
                        foreach (var rotor in originalRotators.OfType<JsonObject>())
                        {
                            if (Get(rotor, "urdfLinks") is not JsonArray rotorLinks) continue;
                            var used = new JsonArray();
                            foreach (var link in rotorLinks) if (links.Contains(link.GetValue<string>())) used.Add(link.DeepClone());
                            if (used.Count == 0) continue;
                            var copy = (JsonObject)rotor.DeepClone(); Node(copy, "urdfLinks", used); rotators.Add(copy);
                        }
                    if (layers.Count == 0 && rotators.Count == 0) continue;
                    var selected = (JsonObject)side.DeepClone(); Node(selected, "layers", layers); Node(selected, "rotators", rotators); sides[sideName] = selected;
                }
                if (sides.Count > 0) { Node(json, "sides", sides); ModelAsset(treads).Content = Bytes(json); }
            }

            // These masks are optional, but preserve an installed, validated optimization bundle.
            // Its source hashes are runtime dependencies too: the renderer checks the full snapshot.
            if (UrdfExteriorMeshes.Load(file).Count > 0)
            {
                string manifest = Path.Combine(sourceFolder, "Meshes", "ExteriorOptimized", "manifest.json");
                var json = ReadObject(manifest); var usedVisuals = new JsonObject();
                foreach (var entry in (JsonObject)Get(json, "visuals"))
                {
                    if (entry.Value is not JsonObject settings || !visuals.TryGetValue(entry.Key, out string source) || Text(settings, "source") != source) continue;
                    var copy = (JsonObject)settings.DeepClone();
                    Set(copy, "source", rewrites[source]); Set(copy, "mesh", RelativeAsset(LocalAsset(Text(settings, "mesh")))); usedVisuals[entry.Key] = copy;
                }
                if (usedVisuals.Count > 0)
                {
                    var sources = new JsonObject();
                    foreach (var source in (JsonObject)Get(json, "sources")) sources[RelativeAsset(LocalAsset(source.Key))] = source.Value.DeepClone();
                    Node(json, "sources", sources); Node(json, "visuals", usedVisuals);
                    Set(json, "UrdfSha256", result.Content == null ? HashFile(file) : Hash(result.Content));
                    ModelAsset(manifest).Content = Bytes(json);
                }
            }
            return result.Target;
        }
        private static void Node(JsonObject obj, string key, JsonNode value) =>
            obj[obj.Select(p => p.Key).FirstOrDefault(k => k.Equals(key, StringComparison.OrdinalIgnoreCase)) ?? key] = value;
    }
}
