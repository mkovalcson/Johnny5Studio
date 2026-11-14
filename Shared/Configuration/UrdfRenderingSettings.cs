using System.Text.Json.Serialization;

namespace ServoAnimator;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum UrdfRenderer { Wpf, Direct3D11 }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum UrdfAntiAliasingMode { Msaa, Fxaa }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum UrdfRobotFinish { Original, GoldJohnny }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum UrdfAccessory { None, Toolbox, Laser }

public sealed class UrdfRenderingSettings
{
    public UrdfRenderer SelectedRenderer { get; set; } = UrdfRenderer.Wpf;
    // Shared across renderers; the clean model is always the default.
    public int WeatheringLevel { get; set; }
    public UrdfRobotFinish RobotFinish { get; set; }
    public UrdfAccessory DefaultAccessory { get; set; } = UrdfAccessory.None;
    public UrdfRenderProfile Wpf { get; set; } = new();
    public UrdfRenderProfile Direct3D11 { get; set; } = new();
    public UrdfRenderProfile Profile(UrdfRenderer renderer) => renderer == UrdfRenderer.Direct3D11 ? Direct3D11 : Wpf;
    public UrdfRenderingSettings Clone() => new() { SelectedRenderer = SelectedRenderer, WeatheringLevel = WeatheringLevel, RobotFinish = RobotFinish, DefaultAccessory = DefaultAccessory, Wpf = Wpf?.Clone() ?? new(), Direct3D11 = Direct3D11?.Clone() ?? new() };
    public void Normalize()
    {
        if (!Enum.IsDefined(SelectedRenderer)) SelectedRenderer = UrdfRenderer.Wpf;
        WeatheringLevel = Math.Clamp(WeatheringLevel, 0, 3);
        if (!Enum.IsDefined(RobotFinish)) RobotFinish = UrdfRobotFinish.Original;
        if (!Enum.IsDefined(DefaultAccessory)) DefaultAccessory = UrdfAccessory.None;
        Wpf ??= new(); Direct3D11 ??= new(); Wpf.Normalize(); Direct3D11.Normalize();
    }
}

public sealed class UrdfRenderProfile
{
    // Neutral multipliers preserve each renderer's own existing appearance.
    public double Brightness { get; set; } = 1;
    public double AmbientLight { get; set; } = 1;
    public double KeyLight { get; set; } = 1;
    public double Highlights { get; set; } = 1;
    public double Gamma { get; set; } = 1;
    public double Contrast { get; set; } = 1;
    public double LensReflectivity { get; set; } = 1;
    // Discrete quality steps: Off, 2x, 4x (the original DX11 default), 8x.
    public int AntiAliasing { get; set; } = 2;
    public UrdfAntiAliasingMode AntiAliasingMode { get; set; } = UrdfAntiAliasingMode.Msaa;
    // Off, Low, Medium, High, Ultra. Remember independently of MSAA samples.
    public int FxaaQuality { get; set; } = 3;
    public UrdfRenderProfile Clone() => (UrdfRenderProfile)MemberwiseClone();
    public void Normalize()
    {
        static double Limit(double value, double min, double max) => double.IsFinite(value) ? Math.Clamp(value, min, max) : 1;
        Brightness = Limit(Brightness, 0, 3); AmbientLight = Limit(AmbientLight, 0, 4);
        KeyLight = Limit(KeyLight, 0, 3); Highlights = Limit(Highlights, 0, 2);
        Gamma = Limit(Gamma, .25, 3); Contrast = Limit(Contrast, 0, 2);
        LensReflectivity = Limit(LensReflectivity, 0, 2);
        AntiAliasing = Math.Clamp(AntiAliasing, 0, 3);
        if (!Enum.IsDefined(AntiAliasingMode)) AntiAliasingMode = UrdfAntiAliasingMode.Msaa;
        FxaaQuality = Math.Clamp(FxaaQuality, 0, 4);
    }
    [JsonIgnore]
    public bool NeutralTone => Brightness == 1 && Highlights == 1 && Gamma == 1 && Contrast == 1;
}
