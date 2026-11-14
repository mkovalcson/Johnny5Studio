using System.Text.Json;

namespace ServoAnimator;

/// <summary>A read-only playback view. Fractional/scaled numeric values and unknown commands remain intact.
/// Editors use AnimationDocument for native integer authoring and round-trip serialization.</summary>
public sealed class SequenceSource : IDisposable
{
    private readonly JsonDocument document;
    public JsonElement Root => document.RootElement;
    private SequenceSource(JsonDocument document) => this.document = document;
    public static SequenceSource Load(string path)
    {
        var document = AnimationJson.ReadFile(path);
        if (AnimationJson.Get(document.RootElement, "commands").ValueKind != JsonValueKind.Array)
        { document.Dispose(); throw new InvalidDataException("Not a sequence: " + path); }
        return new(document);
    }
    public IEnumerable<JsonElement> Commands => AnimationJson.Items(AnimationJson.Get(Root, "commands"));
    public IEnumerable<string> SplineServos => AnimationJson.Items(AnimationJson.Get(Root, "splineServos"))
        .Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString());
    public double DurationSeconds => Math.Max(0, AnimationJson.Number(Root, "durationSeconds"));
    public bool ScaleValues => AnimationJson.Flag(Root, "scaleValues");
    public string PrimaryAudio => string.IsNullOrWhiteSpace(AnimationJson.Text(Root, "audioFilePath"))
        ? AnimationJson.Text(Root, "audioFile") : AnimationJson.Text(Root, "audioFilePath");
    public double AudioStartOffsetSeconds => AnimationJson.Number(Root, "audioStartOffsetSeconds");
    public string SceneReference => AnimationJson.Text(Root, "urdfBackgroundFile");
    public void Dispose() => document.Dispose();
}
