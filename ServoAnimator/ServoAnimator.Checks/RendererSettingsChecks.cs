using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using ServoAnimator;

internal static partial class Program
{
    private static void RendererSettingsChecks()
    {
        var config = UrdfConfiguration.CreateDefault();
        Check(config.Rendering.DefaultAccessory == UrdfAccessory.None && config.Rendering.RobotFinish == UrdfRobotFinish.Original,"New URDF configurations default to no accessory and original colors");
        Check(config.Rendering.SelectedRenderer == UrdfRenderer.Wpf && config.Rendering.Wpf.NeutralTone && config.Rendering.Direct3D11.NeutralTone,
            "Renderer profiles preserve existing WPF and DX11 defaults");
        Check(!ReferenceEquals(config.Rendering.Wpf, config.Rendering.Direct3D11), "Renderer settings are independent profiles");
        string folder = Path.Combine(Path.GetTempPath(), "UrdfRenderer-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            int previews = 0;
            var dialog = new UrdfConfigWindow(config, ServoConfiguration.CreateDefault(), (_, _) => { }, (_, _, _) => { }, null,
                folder, restored: () => previews++);
            var selector = (ComboBox)typeof(UrdfConfigWindow).GetField("_renderSelection", flags).GetValue(dialog);
            var weathering = (ComboBox)typeof(UrdfConfigWindow).GetField("_weatheringSelection", flags).GetValue(dialog);
            var finish = (ComboBox)typeof(UrdfConfigWindow).GetField("_finishSelection", flags).GetValue(dialog);
            var accessory = (ComboBox)typeof(UrdfConfigWindow).GetField("_accessorySelection", flags).GetValue(dialog);
            finish.SelectedIndex = 1; accessory.SelectedIndex = 2;
            Check(config.Rendering.RobotFinish == UrdfRobotFinish.GoldJohnny && !weathering.IsEnabled && config.Rendering.DefaultAccessory == UrdfAccessory.Laser,"Gold Johnny and accessory choices preview immediately");
            finish.SelectedIndex = 0;
            Check(weathering.SelectedIndex == 0 && weathering.Items.Count == 4 && config.Rendering.WeatheringLevel == 0,
                "Weathering offers four numbered levels and defaults to the original clean model");
            weathering.SelectedIndex = 2;
            Check(config.Rendering.WeatheringLevel == 2, "Weathering selection previews the shared level");
            var controls = (StackPanel)dialog.FindName("RendererSettingsPanel");
            Slider Slider(string name) => controls.Children.OfType<Grid>().SelectMany(r => r.Children.OfType<Slider>()).Single(s => s.Name == name);
            Check(!Slider("RenderAntiAliasing").IsEnabled && config.Rendering.Direct3D11.AntiAliasing == 2,
                "WPF reports automatic smoothing and older DX11 settings retain 4x AA");
            Slider("RenderGamma").Value = 1.4;
            var lensSlider = Slider("RenderLensReflectivity");
            Check(lensSlider.Minimum == 0 && lensSlider.Maximum == 2 && lensSlider.Value == 1,
                "Lens slider starts centered at the existing appearance with Off at the left");
            lensSlider.Value = .4;
            Check(config.Rendering.Wpf.Gamma == 1.4 && config.Rendering.Direct3D11.Gamma == 1, "WPF edits leave DX11 settings unchanged");
            selector.SelectedIndex = 1; Slider("RenderBrightness").Value = .65; Slider("RenderContrast").Value = .8;
            Check(lensSlider.Value == 1, "DX11 lens reflectivity starts independently at the existing appearance");
            lensSlider.Value = 1.8;
            Slider("RenderAntiAliasing").Value = 3;
            Check(Slider("RenderAntiAliasing").IsEnabled && config.Rendering.Direct3D11.AntiAliasing == 3 && config.Rendering.Wpf.AntiAliasing == 2,
                "AA slider previews 8x for DX11 independently of WPF");
            selector.SelectedIndex = 0;
            Check(weathering.SelectedIndex == 2, "Switching renderers retains the shared weathering level");
            Check(lensSlider.Value == .4, "Switching back restores WPF lens reflectivity");
            Check(Slider("RenderGamma").Value == 1.4 && Slider("RenderBrightness").Value == 1, "Switching renderers restores the WPF profile for live A/B comparison");
            selector.SelectedIndex = 1;
            Check(Slider("RenderBrightness").Value == .65 && Slider("RenderContrast").Value == .8 && previews >= 5, "DX11 sliders retain their own values and preview changes");
            var aaSelector = (ComboBox)typeof(UrdfConfigWindow).GetField("_antiAliasingSelection", flags).GetValue(dialog);
            aaSelector.SelectedIndex = 1; Slider("RenderAntiAliasing").Value = 4;
            aaSelector.SelectedIndex = 0;
            Check(Slider("RenderAntiAliasing").Value == 3 && Slider("RenderAntiAliasing").Maximum == 3, "Switching AA types restores the MSAA sample count");
            aaSelector.SelectedIndex = 1;
            Check(Slider("RenderAntiAliasing").Value == 4 && Slider("RenderAntiAliasing").Maximum == 4, "Switching AA types restores FXAA Ultra independently");
            typeof(UrdfConfigWindow).GetMethod("SaveDefault_Click", flags).Invoke(dialog, new object[] { dialog, new RoutedEventArgs() });
            var loaded = UrdfConfiguration.Load(Path.Combine(folder, "URDFconfig.json"));
            Check(loaded.Rendering.WeatheringLevel == 2, "Save Default persists the weathering level");
            Check(loaded.Rendering.DefaultAccessory == UrdfAccessory.Laser,"Save Default persists the accessory selection");
            loaded.Rendering.RobotFinish=UrdfRobotFinish.GoldJohnny;
            Check(loaded.Rendering.Wpf.LensReflectivity == .4 && loaded.Rendering.Direct3D11.LensReflectivity == 1.8,
                "Save and reload preserve both lens reflection settings");
            Check(loaded.Rendering.SelectedRenderer == UrdfRenderer.Direct3D11 && loaded.Rendering.Wpf.Gamma == 1.4 && loaded.Rendering.Direct3D11.Brightness == .65,
                "Save URDF Configuration persists the selected renderer and both profiles");
            Check(loaded.Rendering.Direct3D11.AntiAliasing == 3, "Save URDF Configuration persists anti-aliasing quality");
            Check(loaded.Rendering.Direct3D11.AntiAliasingMode == UrdfAntiAliasingMode.Fxaa && loaded.Rendering.Direct3D11.FxaaQuality == 4,
                "Save persists the AA type and independent FXAA quality");
            var copy = UrdfConfiguration.CreateDefault(); copy.CopyFrom(loaded); loaded.Rendering.Wpf.Gamma = 2;
            Check(copy.Rendering.RobotFinish==UrdfRobotFinish.GoldJohnny && copy.Rendering.DefaultAccessory==UrdfAccessory.Laser,"Scene snapshots include finish and accessory defaults");
            loaded.Rendering.WeatheringLevel = 3;
            Check(copy.Rendering.WeatheringLevel == 2, "Configuration snapshots retain an independent weathering value");
            Check(copy.Rendering.Wpf.Gamma == 1.4 && !ReferenceEquals(copy.Rendering.Direct3D11, loaded.Rendering.Direct3D11), "Config snapshots deep-copy rendering profiles");
            Directory.CreateDirectory(PreviewDirectory("renderer-previews"));
            RenderControl((FrameworkElement)dialog.Content, Path.Combine(PreviewDirectory("renderer-previews"), "UrdfConfiguration-rendering.png"), 1380, 880);
            typeof(UrdfConfigWindow).GetMethod("Back_Click", flags).Invoke(dialog, new object[] { dialog, new RoutedEventArgs() });
            Check(config.Rendering.WeatheringLevel == 0 && weathering.SelectedIndex == 0, "Back restores the opening weathering level");
            Check(config.Rendering.RobotFinish==UrdfRobotFinish.Original && config.Rendering.DefaultAccessory==UrdfAccessory.None && accessory.SelectedIndex==0,"Back restores opening finish and no-accessory default");
            Check(config.Rendering.Wpf.LensReflectivity == 1 && config.Rendering.Direct3D11.LensReflectivity == 1 && lensSlider.Value == 1,
                "Back restores both opening lens reflection settings and the slider");
            Check(config.Rendering.SelectedRenderer == UrdfRenderer.Wpf && config.Rendering.Wpf.Gamma == 1 && config.Rendering.Direct3D11.Brightness == 1 && selector.SelectedIndex == 0,
                "Back restores the opening renderer, both profiles and slider values");
            Check(config.Rendering.Direct3D11.AntiAliasing == 2, "Back restores the opening anti-aliasing setting");
            Check(config.Rendering.Direct3D11.AntiAliasingMode == UrdfAntiAliasingMode.Msaa && config.Rendering.Direct3D11.FxaaQuality == 3,
                "Back restores AA type and both quality settings");
            dialog.Close();
            File.WriteAllText(Path.Combine(folder, "URDFconfig.json"), "{}");
            loaded = UrdfConfiguration.Load(Path.Combine(folder, "URDFconfig.json"));
            Check(loaded.Rendering.WeatheringLevel == 0, "Legacy configuration files retain the clean finish");
            Check(loaded.Rendering.DefaultAccessory==UrdfAccessory.None,"Legacy configurations without an accessory preference now default to None");
            loaded.Rendering.WeatheringLevel = 99; loaded.Rendering.Normalize();
            Check(loaded.Rendering.WeatheringLevel == 3, "Invalid weathering levels are limited to the supported range");
            Check(loaded.Rendering.Wpf.LensReflectivity == 1 && loaded.Rendering.Direct3D11.LensReflectivity == 1,
                "Older configurations retain the current lens appearance");
            Check(loaded.Rendering.SelectedRenderer == UrdfRenderer.Wpf && loaded.Rendering.Wpf.NeutralTone && loaded.Rendering.Direct3D11.NeutralTone,
                "Legacy configurations preserve both current appearances and default to WPF");
            loaded.Rendering.Wpf.Gamma = double.NaN; loaded.Rendering.Direct3D11.Brightness = 100; loaded.Rendering.Normalize();
            Check(loaded.Rendering.Wpf.Gamma == 1 && loaded.Rendering.Direct3D11.Brightness == 3, "Invalid rendering values are normalized safely");
        }
        finally { File.Delete(Path.Combine(folder, "URDFconfig.json")); Directory.Delete(folder); }
    }
}
