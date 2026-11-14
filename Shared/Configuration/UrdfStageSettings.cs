using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ServoAnimator;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum UrdfStageMode { RobotCentric, RoomCentric }

public sealed class UrdfStageSettings
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<UrdfPropPlacement> Props { get; set; }
    public string ModelFile { get; set; } = ModelLocations.DefaultScenery(FolderSettings.Load()?.ConfigFolderOrDefault ?? AppContext.BaseDirectory);
    public UrdfStageMode MovementMode { get; set; } = UrdfStageMode.RobotCentric;
    public bool PreserveRobotFraming { get; set; } = true;
    public UrdfFraming RobotFraming { get; set; }
    public double RobotX { get; set; }
    public double RobotY { get; set; }
    public double HeadingDegrees { get; set; }
    public double FloorHeight { get; set; } = -1.078381;
    public bool DetectFloor { get; set; } = true;
    public double ViewPanX { get; set; }
    public double ViewPanY { get; set; }
    public double ViewPanZ { get; set; }
    public double ViewZoomPercent { get; set; } = 100;
    public double ViewYawDegrees { get; set; }
    public double EdgeMarginPercent { get; set; } = 20;
    public UrdfStageSettings Clone()
    {
        var copy = (UrdfStageSettings)MemberwiseClone();
        copy.Props = Props == null ? null : new(Props);
        copy.RobotFraming = RobotFraming?.Clone();
        return copy;
    }
    public string ResolveModelFile(string root) => Path.GetFullPath(Path.Combine(root, ModelFile));
    public void Validate(string root = null)
    {
        foreach (double number in new[] { RobotX, RobotY, HeadingDegrees, FloorHeight, ViewPanX, ViewPanY, ViewPanZ, ViewZoomPercent, ViewYawDegrees, EdgeMarginPercent })
            if (!double.IsFinite(number)) throw new InvalidDataException("Stage coordinates must be finite numbers.");
        if (ViewZoomPercent < 10 || ViewZoomPercent > 1000 || EdgeMarginPercent < 5 || EdgeMarginPercent > 45)
            throw new InvalidDataException("Stage zoom must be 10–1000%; edge margin must be 5–45%.");
        if (Props?.Count > 128) throw new InvalidDataException("A stage can contain up to 128 props.");
        foreach (var prop in Props ?? new()) (prop ?? throw new InvalidDataException("Invalid prop placement.")).Resolve(root ?? AppContext.BaseDirectory);
        if (!Enum.IsDefined(MovementMode)) throw new InvalidDataException("Unknown stage movement mode.");
        if (string.IsNullOrWhiteSpace(ModelFile) || !File.Exists(ResolveModelFile(root ?? AppContext.BaseDirectory))) throw new FileNotFoundException("Choose an existing backdrop URDF.", ModelFile);
    }
    public static bool IsMotion(ServoNames servo) => servo is ServoNames.URDFStageX or ServoNames.URDFStageY or ServoNames.URDFStageHeading;
}

public sealed class BackdropCommand
{
    public string SceneFile { get; set; } = "";
    public UrdfPresentationSettings Background { get; set; } = new();
    public UrdfFraming Framing { get; set; }
    public static BackdropCommand Read(string json) => string.IsNullOrWhiteSpace(json) ? new() : JsonSerializer.Deserialize<BackdropCommand>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
    public string ToJson() => string.IsNullOrWhiteSpace(SceneFile) ? JsonSerializer.Serialize(this) : JsonSerializer.Serialize(new { SceneFile });
    public void Validate(string root)
    {
        if (!string.IsNullOrWhiteSpace(SceneFile)) { SavedSceneCommand.Read(root, SceneFile); return; }
        if (Background == null || !Enum.IsDefined(Background.BackgroundMode)) throw new InvalidDataException("Choose a background type.");
        if (Background.BackgroundMode == UrdfBackgroundMode.Urdf3D) (Background.Stage ?? throw new InvalidDataException("Stage settings are missing.")).Validate(root);
        else Background.CreateBrush(root, System.Windows.Media.Brushes.Black);
        ValidateFraming(Framing);
        ValidateFraming(Background.Stage?.RobotFraming);
    }
    private static void ValidateFraming(UrdfFraming Framing)
    {
        if (Framing != null && (!double.IsFinite(Framing.Distance) || Framing.Distance < .55 || Framing.Distance > 11.5 ||
            !double.IsFinite(Framing.HorizontalPercent) || !double.IsFinite(Framing.VerticalPercent) || !double.IsFinite(Framing.SpinDegrees) ||
            !double.IsFinite(Framing.RotateDegrees) || !double.IsFinite(Framing.Yaw) || !double.IsFinite(Framing.Pitch)))
            throw new InvalidDataException("Invalid robot framing. Camera distance must be .55–11.5 metres.");
    }
}
