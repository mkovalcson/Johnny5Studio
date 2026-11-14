using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace ServoAnimator;

internal sealed class UrdfToneEffect : ShaderEffect
{
    private static readonly PixelShader ToneShader = new() { UriSource = new Uri("pack://application:,,,/AnimationEditorPlayer;component/Shaders/UrdfTone.ps") };
    private static readonly PixelShader FxaaShader = new() { UriSource = new Uri("pack://application:,,,/AnimationEditorPlayer;component/Shaders/UrdfFxaa.ps") };
    public static readonly DependencyProperty InputProperty = RegisterPixelShaderSamplerProperty(nameof(Input), typeof(UrdfToneEffect), 0);
    public static readonly DependencyProperty BrightnessProperty = DependencyProperty.Register(nameof(Brightness), typeof(double), typeof(UrdfToneEffect), new UIPropertyMetadata(1d, PixelShaderConstantCallback(0)));
    public static readonly DependencyProperty GammaProperty = DependencyProperty.Register(nameof(Gamma), typeof(double), typeof(UrdfToneEffect), new UIPropertyMetadata(1d, PixelShaderConstantCallback(1)));
    public static readonly DependencyProperty ContrastProperty = DependencyProperty.Register(nameof(Contrast), typeof(double), typeof(UrdfToneEffect), new UIPropertyMetadata(1d, PixelShaderConstantCallback(2)));
    public static readonly DependencyProperty HighlightsProperty = DependencyProperty.Register(nameof(Highlights), typeof(double), typeof(UrdfToneEffect), new UIPropertyMetadata(1d, PixelShaderConstantCallback(3)));
    public static readonly DependencyProperty PixelStepProperty = DependencyProperty.Register(nameof(PixelStep), typeof(Point), typeof(UrdfToneEffect), new UIPropertyMetadata(new Point(1d / 800, 1d / 600), PixelShaderConstantCallback(4)));
    public static readonly DependencyProperty FxaaQualityProperty = DependencyProperty.Register(nameof(FxaaQuality), typeof(double), typeof(UrdfToneEffect), new UIPropertyMetadata(0d, PixelShaderConstantCallback(5)));
    public Brush Input { get => (Brush)GetValue(InputProperty); set => SetValue(InputProperty, value); }
    public double Brightness { get => (double)GetValue(BrightnessProperty); set => SetValue(BrightnessProperty, value); }
    public double Gamma { get => (double)GetValue(GammaProperty); set => SetValue(GammaProperty, value); }
    public double Contrast { get => (double)GetValue(ContrastProperty); set => SetValue(ContrastProperty, value); }
    public double Highlights { get => (double)GetValue(HighlightsProperty); set => SetValue(HighlightsProperty, value); }
    public Point PixelStep { get => (Point)GetValue(PixelStepProperty); set => SetValue(PixelStepProperty, value); }
    public double FxaaQuality { get => (double)GetValue(FxaaQualityProperty); set => SetValue(FxaaQualityProperty, value); }
    public UrdfToneEffect()
    {
        PixelShader = ToneShader;
        foreach (var property in new[] { InputProperty, BrightnessProperty, GammaProperty, ContrastProperty, HighlightsProperty, PixelStepProperty, FxaaQualityProperty }) UpdateShaderValue(property);
    }
    internal void Apply(UrdfRenderProfile profile, bool fxaa = false)
    {
        PixelShader = fxaa ? FxaaShader : ToneShader;
        Brightness = profile.Brightness; Gamma = profile.Gamma; Contrast = profile.Contrast; Highlights = profile.Highlights;
        FxaaQuality = fxaa ? profile.FxaaQuality : 0;
    }
}
