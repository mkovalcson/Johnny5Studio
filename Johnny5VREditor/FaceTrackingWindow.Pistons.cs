using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ServoAnimator;

internal sealed partial class FaceTrackingWindow
{
    private readonly TextBlock _warnings = new() { Foreground = Brushes.Gold, TextWrapping = TextWrapping.Wrap };
    public FaceTrackingSettings PreviewSettings { get; private set; }

    private void RefreshPistonPreview()
    {
        // Only piston settings change live. Mapping edits still require Apply.
        PreviewSettings.Pistons = JsonSerializer.Deserialize<PistonSignalSettings>(JsonSerializer.Serialize(_draft.Pistons));
    }

    private UIElement CreatePistonSettings()
    {
        var panel = new WrapPanel { Margin = new Thickness(6) };
        void Label(string text) => panel.Children.Add(new TextBlock { Text = text, Margin = new Thickness(5), VerticalAlignment = VerticalAlignment.Center });
        void Range(string label, Func<double> get, Action<double> set)
        {
            Label(label);
            var box = new TextBox { Text = get().ToString(CultureInfo.CurrentCulture), Width = 48, Margin = new Thickness(3) };
            panel.Children.Add(box);
            box.LostKeyboardFocus += (_, _) =>
            {
                if (double.TryParse(box.Text, out double value) && double.IsFinite(value) && value > 0)
                { set(value); RefreshPistonPreview(); }
                else { box.Text = get().ToString(CultureInfo.CurrentCulture); _status.Text = label + " must be greater than zero; previous value retained."; }
            };
        }
        void Gain(string label, Func<double> get, Action<double> set)
        {
            Label(label);
            var slider = new Slider { Minimum = 0, Maximum = 1, Value = get(), Width = 90, Margin = new Thickness(4), VerticalAlignment = VerticalAlignment.Center };
            var value = new TextBlock { Text = get().ToString("0.00"), Width = 32, VerticalAlignment = VerticalAlignment.Center };
            panel.Children.Add(slider); panel.Children.Add(value);
            slider.ValueChanged += (_, _) => { set(slider.Value); value.Text = slider.Value.ToString("0.00"); RefreshPistonPreview(); };
        }
        void Reverse(string label, Func<bool> get, Action<bool> set)
        {
            var box = new CheckBox { Content = label, IsChecked = get(), Margin = new Thickness(6), VerticalAlignment = VerticalAlignment.Center };
            panel.Children.Add(box);
            box.Click += (_, _) => { set(box.IsChecked == true); RefreshPistonPreview(); };
        }
        Range("Pitch range (deg)", () => _draft.Pistons.PitchRange, v => _draft.Pistons.PitchRange = v);
        Range("Roll range (deg)", () => _draft.Pistons.RollRange, v => _draft.Pistons.RollRange = v);
        Gain("Pitch gain", () => _draft.Pistons.PitchGain, v => _draft.Pistons.PitchGain = v);
        Gain("Roll authority", () => _draft.Pistons.RollAuthority, v => _draft.Pistons.RollAuthority = v);
        Reverse("Reverse pitch", () => _draft.Pistons.ReversePitch, v => _draft.Pistons.ReversePitch = v);
        Reverse("Reverse roll", () => _draft.Pistons.ReverseRoll, v => _draft.Pistons.ReverseRoll = v);
        Label("Clip mode");
        var clip = new ComboBox { ItemsSource = Enum.GetValues<PistonClipMode>(), SelectedItem = _draft.Pistons.ClipMode, Width = 135, Margin = new Thickness(3) };
        panel.Children.Add(clip);
        clip.SelectionChanged += (_, _) => { if (clip.SelectedItem is PistonClipMode mode) { _draft.Pistons.ClipMode = mode; RefreshPistonPreview(); } };
        return new GroupBox { Header = "Piston signals", Content = panel };
    }

    private void AddPistonMappings()
    {
        foreach (string signal in new[] { "LPiston", "RPiston" })
            if (!_draft.Mappings.Any(m => m.Signal == signal))
                _draft.Mappings.Add(new FaceMapping { Signal = signal,
                    Target = signal == "LPiston" ? FaceTrackingSettings.NeckLeftTarget : FaceTrackingSettings.NeckRightTarget,
                    InputMin = -1, InputMax = 1, OutputMin = -100, OutputMax = 100, SmoothingMs = 200 });
        _grid.Items.Refresh();
        _status.Text = _message = "Piston rows added disabled. Disable the parent neck mappings, then enable these rows and Apply.";
    }
}
