using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ServoAnimator;

public partial class UrdfConfigWindow
{
    private readonly ComboBox _renderSelection = new() { Width = 180, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly ComboBox _weatheringSelection = new() { Name = "RenderWeathering", Width = 220, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly ComboBox _finishSelection = new() { Name = "RobotFinish", Width = 220 };
    private readonly ComboBox _accessorySelection = new() { Name = "DefaultAccessory", Width = 220 };
    private readonly TextBlock _weatheringProgress = new() { Name = "WeatheringProgress", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(200,0,0,6), Visibility = Visibility.Collapsed };
    private readonly List<(Slider Slider, TextBlock Value, Func<UrdfRenderProfile, double> Read)> _renderSliders = new();
    private bool _syncingRenderer;
    private Slider _antiAliasingSlider;
    private TextBlock _antiAliasingValue, _antiAliasingHint;
    private readonly ComboBox _antiAliasingSelection = new() { Width = 180, HorizontalAlignment = HorizontalAlignment.Left };
    private static string AntiAliasingLabel(UrdfRenderProfile profile) => profile.AntiAliasingMode == UrdfAntiAliasingMode.Fxaa
        ? new[] { "Off", "Low", "Medium", "High", "Ultra" }[profile.FxaaQuality]
        : profile.AntiAliasing == 0 ? "Off" : $"{1 << profile.AntiAliasing}× MSAA";

    private void InitializeRenderingControls()
    {
        var title = new TextBlock { Text = "Renderer and appearance", FontSize = 16, FontWeight = FontWeights.SemiBold };
        title.SetResourceReference(TextBlock.ForegroundProperty, "HeaderText"); RendererSettingsPanel.Children.Add(title);
        var selection = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 6) };
        selection.Children.Add(new TextBlock { Text = "Renderer", Width = 200, VerticalAlignment = VerticalAlignment.Center });
        _renderSelection.Items.Add("WPF"); _renderSelection.Items.Add("Direct3D 11"); selection.Children.Add(_renderSelection);
        RendererSettingsPanel.Children.Add(selection);
        void Choice(string label, ComboBox combo, string[] options, string hint)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0,3,0,6) };
            row.Children.Add(new TextBlock { Text = label, Width = 200, VerticalAlignment = VerticalAlignment.Center });
            foreach (var option in options) combo.Items.Add(option);
            row.Children.Add(combo);
            row.Children.Add(new TextBlock { Text = hint, Margin = new Thickness(12,0,0,0), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 390 });
            RendererSettingsPanel.Children.Add(row);
        }
        Choice("Robot finish", _finishSelection, new[] { "Original colors", "Gold Johnny" }, "Gold body, eye tubes and supports; optics, LEDs, bellows, piston shafts and black treads retain their colors.");
        Choice("Default accessory", _accessorySelection, new[] { "None", "Toolbox", "Laser" }, "Used until a timeline command selects or hides an accessory.");
        _finishSelection.SelectionChanged += (_,_) => { if (_syncingRenderer) return; _config.Rendering.RobotFinish = (UrdfRobotFinish)Math.Max(0,_finishSelection.SelectedIndex); SyncRenderingControls(); _restored?.Invoke(); };
        _accessorySelection.SelectionChanged += (_,_) => { if (_syncingRenderer) return; _config.Rendering.DefaultAccessory = (UrdfAccessory)Math.Max(0,_accessorySelection.SelectedIndex); _restored?.Invoke(); };
        var weathering = new Grid { Margin = new Thickness(0, 3, 0, 6) };
        weathering.ColumnDefinitions.Add(new() { Width = new GridLength(200) });
        weathering.ColumnDefinitions.Add(new() { Width = new GridLength(220) });
        weathering.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        weathering.Children.Add(new TextBlock { Text = "Weathering / distress", VerticalAlignment = VerticalAlignment.Center });
        foreach (string option in new[] { "0 — Clean", "1 — Light wear", "2 — Workshop used", "3 — Outdoor aged" }) _weatheringSelection.Items.Add(option);
        Grid.SetColumn(_weatheringSelection, 1); weathering.Children.Add(_weatheringSelection);
        var weatheringHint = new TextBlock { Text = "Shared by both renderers. Previews immediately; Save Default keeps the selection.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12,0,0,0), VerticalAlignment = VerticalAlignment.Center };
        weatheringHint.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
        Grid.SetColumn(weatheringHint, 2); weathering.Children.Add(weatheringHint);
        RendererSettingsPanel.Children.Add(weathering);
        _weatheringProgress.SetResourceReference(TextBlock.ForegroundProperty,"SecondaryText");
        RendererSettingsPanel.Children.Add(_weatheringProgress);
        var preparationTimer=new System.Windows.Threading.DispatcherTimer { Interval=TimeSpan.FromMilliseconds(100) };
        preparationTimer.Tick+=(_,_) =>
        {
            var message=UrdfScene.WeatheringActivity;
            _weatheringProgress.Text=message;
            _weatheringProgress.Visibility=string.IsNullOrEmpty(message)?Visibility.Collapsed:Visibility.Visible;
        };
        preparationTimer.Start(); Closed+=(_,_)=>preparationTimer.Stop();
        _weatheringSelection.SelectionChanged += (_, _) =>
        {
            if (_syncingRenderer || _config == null || _weatheringSelection.SelectedIndex < 0) return;
            _config.Rendering.WeatheringLevel = _weatheringSelection.SelectedIndex; _restored?.Invoke();
        };
        var note = new TextBlock { Text = "Lighting and lens settings are separate for each renderer; weathering is shared. Switch to compare live. Save Default saves all these settings. 1.00× retains that renderer's original appearance.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
        note.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText"); RendererSettingsPanel.Children.Add(note);
        _renderSelection.SelectionChanged += (_, _) =>
        {
            if (_syncingRenderer || _config == null) return;
            _config.Rendering.SelectedRenderer = _renderSelection.SelectedIndex == 1 ? UrdfRenderer.Direct3D11 : UrdfRenderer.Wpf;
            SyncRenderingControls(); _restored?.Invoke();
        };
        void Add(string name, string label, double min, double max, string explanation, Func<UrdfRenderProfile, double> read, Action<UrdfRenderProfile, double> write)
        {
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            foreach (double width in new[] { 200d, 260, 78 }) row.ColumnDefinitions.Add(new() { Width = new GridLength(width) });
            row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            var slider = new Slider { Name = name, Minimum = min, Maximum = max, TickFrequency = .05, SmallChange = .05, LargeChange = .25, VerticalAlignment = VerticalAlignment.Center, ToolTip = explanation };
            Grid.SetColumn(slider, 1); row.Children.Add(slider);
            var value = new TextBlock { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(value, 2); row.Children.Add(value);
            var hint = new TextBlock { Text = explanation, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            hint.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText"); Grid.SetColumn(hint, 3); row.Children.Add(hint);
            RendererSettingsPanel.Children.Add(row); _renderSliders.Add((slider, value, read));
            slider.ValueChanged += (_, _) =>
            {
                if (_syncingRenderer || _config == null) return;
                write(_config.Rendering.Profile(_config.Rendering.SelectedRenderer), slider.Value);
                value.Text = $"{slider.Value:0.00}×"; _restored?.Invoke();
            };
        }
        Add("RenderBrightness", "Brightness", 0, 3, "Overall brightness of the 3D surface.", p => p.Brightness, (p, v) => p.Brightness = v);
        Add("RenderAmbient", "Shadow fill / ambient light", 0, 4, "Raise ambient light to brighten shadowed surfaces.", p => p.AmbientLight, (p, v) => p.AmbientLight = v);
        Add("RenderKey", "Key / directional lighting", 0, 3, "Strength of the studio directional and point lights.", p => p.KeyLight, (p, v) => p.KeyLight = v);
        Add("RenderHighlights", "Highlights", 0, 2, "Bright-area strength; lower it to soften intense highlights.", p => p.Highlights, (p, v) => p.Highlights = v);
        Add("RenderGamma", "Gamma / midtones", .25, 3, "Above 1 brightens midtones; below 1 darkens them.", p => p.Gamma, (p, v) => p.Gamma = v);
        Add("RenderContrast", "Contrast", 0, 2, "Below 1 softens contrast; above 1 increases it.", p => p.Contrast, (p, v) => p.Contrast = v);
        Add("RenderLensReflectivity", "Lens reflectivity", 0, 2, "0 = Off • 1 = current appearance • 2 = stronger reflections. Keeps the lens shape and iris opening unchanged.", p => p.LensReflectivity, (p, v) => p.LensReflectivity = v);
        var aaSelection = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 3) };
        aaSelection.Children.Add(new TextBlock { Text = "Anti-aliasing type", Width = 200, VerticalAlignment = VerticalAlignment.Center });
        _antiAliasingSelection.Items.Add("MSAA"); _antiAliasingSelection.Items.Add("FXAA");
        aaSelection.Children.Add(_antiAliasingSelection); RendererSettingsPanel.Children.Add(aaSelection);
        _antiAliasingSelection.SelectionChanged += (_, _) =>
        {
            if (_syncingRenderer || _config == null || _config.Rendering.SelectedRenderer != UrdfRenderer.Direct3D11) return;
            _config.Rendering.Direct3D11.AntiAliasingMode = _antiAliasingSelection.SelectedIndex == 1 ? UrdfAntiAliasingMode.Fxaa : UrdfAntiAliasingMode.Msaa;
            SyncRenderingControls(); _restored?.Invoke();
        };
        var aaRow = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        foreach (double width in new[] { 200d, 260, 78 }) aaRow.ColumnDefinitions.Add(new() { Width = new GridLength(width) });
        aaRow.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        aaRow.Children.Add(new TextBlock { Text = "Anti-aliasing / edge smoothing", VerticalAlignment = VerticalAlignment.Center });
        _antiAliasingSlider = new Slider { Name = "RenderAntiAliasing", Minimum = 0, Maximum = 3, TickFrequency = 1,
            SmallChange = 1, LargeChange = 1, IsSnapToTickEnabled = true, TickPlacement = System.Windows.Controls.Primitives.TickPlacement.BottomRight,
            VerticalAlignment = VerticalAlignment.Center, ToolTip = "Off → 2× → 4× → 8×. Higher quality smooths edges and increases GPU work." };
        Grid.SetColumn(_antiAliasingSlider, 1); aaRow.Children.Add(_antiAliasingSlider);
        _antiAliasingValue = new TextBlock { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(_antiAliasingValue, 2); aaRow.Children.Add(_antiAliasingValue);
        _antiAliasingHint = new TextBlock { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        _antiAliasingHint.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
        Grid.SetColumn(_antiAliasingHint, 3); aaRow.Children.Add(_antiAliasingHint); RendererSettingsPanel.Children.Add(aaRow);
        _antiAliasingSlider.ValueChanged += (_, _) =>
        {
            if (_syncingRenderer || _config == null || _config.Rendering.SelectedRenderer != UrdfRenderer.Direct3D11) return;
            int quality = (int)Math.Round(_antiAliasingSlider.Value);
            var profile = _config.Rendering.Direct3D11;
            if (profile.AntiAliasingMode == UrdfAntiAliasingMode.Fxaa) profile.FxaaQuality = quality;
            else profile.AntiAliasing = quality;
            _antiAliasingValue.Text = AntiAliasingLabel(profile); _restored?.Invoke();
        };
        var reset = new Button { Content = "Reset this renderer", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0), Padding = new Thickness(8, 3, 8, 3) };
        reset.Click += (_, _) =>
        {
            if (_config.Rendering.SelectedRenderer == UrdfRenderer.Direct3D11) _config.Rendering.Direct3D11 = new(); else _config.Rendering.Wpf = new();
            SyncRenderingControls(); _restored?.Invoke();
        };
        RendererSettingsPanel.Children.Add(reset); SyncRenderingControls();
    }
    private void SyncRenderingControls()
    {
        if (_config == null) return;
        _config.Rendering ??= new(); _config.Rendering.Normalize(); _syncingRenderer = true;
        try
        {
            _renderSelection.SelectedIndex = _config.Rendering.SelectedRenderer == UrdfRenderer.Direct3D11 ? 1 : 0;
            _weatheringSelection.SelectedIndex = _config.Rendering.WeatheringLevel;
            _finishSelection.SelectedIndex = (int)_config.Rendering.RobotFinish;
            _accessorySelection.SelectedIndex = (int)_config.Rendering.DefaultAccessory;
            _weatheringSelection.IsEnabled = _config.Rendering.RobotFinish == UrdfRobotFinish.Original;
            _weatheringSelection.ToolTip = _weatheringSelection.IsEnabled ? "Wear applied to original materials." : "Gold Johnny uses a polished gold finish. Your weathering selection is retained for Original colors.";
            var profile = _config.Rendering.Profile(_config.Rendering.SelectedRenderer);
            foreach (var control in _renderSliders) { control.Slider.Value = control.Read(profile); control.Value.Text = $"{control.Slider.Value:0.00}×"; }
            bool dx11 = _config.Rendering.SelectedRenderer == UrdfRenderer.Direct3D11;
            bool fxaa = profile.AntiAliasingMode == UrdfAntiAliasingMode.Fxaa;
            _antiAliasingSelection.IsEnabled = dx11;
            _antiAliasingSelection.SelectedIndex = fxaa ? 1 : 0;
            _antiAliasingSlider.IsEnabled = dx11;
            _antiAliasingSlider.Maximum = fxaa ? 4 : 3;
            _antiAliasingSlider.Value = fxaa ? profile.FxaaQuality : profile.AntiAliasing;
            _antiAliasingSlider.ToolTip = fxaa ? "Off → Low → Medium → High → Ultra. Smooths the finished 3D image; may soften fine detail."
                : "Off → 2× → 4× → 8×. Smooths geometry edges; higher sample counts use more GPU time.";
            _antiAliasingValue.Text = dx11 ? AntiAliasingLabel(profile) : "Auto";
            _antiAliasingHint.Text = dx11 ? fxaa ? "Smooths the finished 3D image; can soften detail. MSAA is disabled. Default FXAA quality: High."
                : "Smooths geometry edges; limited to hardware support. Default: 4×. FXAA is disabled."
                : "WPF manages edge smoothing automatically. Select Direct3D 11 to adjust the sample count.";
        }
        finally { _syncingRenderer = false; }
    }
}
