using System.Text.Json;

namespace ServoAnimator;

public static class AnimationJson
{
    public static readonly JsonDocumentOptions Options = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };
    public static JsonElement Get(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object ? e.EnumerateObject().FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Value : default;
    public static string Text(JsonElement e, string name, string fallback = "") => Get(e, name) is var v && v.ValueKind == JsonValueKind.String ? v.GetString() ?? fallback : fallback;
    public static double Number(JsonElement e, string name, double fallback = 0) => Get(e, name) is var v && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var n) && double.IsFinite(n) ? n : fallback;
    public static bool Flag(JsonElement e, string name) => Get(e, name).ValueKind == JsonValueKind.True;
    public static IEnumerable<JsonElement> Items(JsonElement e) => e.ValueKind == JsonValueKind.Array ? e.EnumerateArray() : [];
    public static JsonDocument ReadFile(string file) => JsonDocument.Parse(File.ReadAllText(file), Options);
}
