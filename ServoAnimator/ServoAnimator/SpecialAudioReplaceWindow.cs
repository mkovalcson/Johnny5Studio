using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace ServoAnimator;

internal sealed class SpecialAudioReplaceWindow : Window
{
    private readonly TextBox _audio = new(), _update = new(), _strength = new() { Text = "100", Width = 90 };
    private readonly CheckBox _retime = new() { Content = "Re-time the animation", IsChecked = true },
        _repeat = new() { Content = "Repeat the face during stutters", IsChecked = true },
        _motion = new() { Content = "Add motion", IsChecked = true };
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) };
    public string AudioPath => ConfigPathService.Resolve(ConfigPathService.CurrentRoot, _audio.Text.Trim());
    public string UpdatePath => ConfigPathService.Resolve(ConfigPathService.CurrentRoot, _update.Text.Trim());
    public bool Retime => _retime.IsEnabled && _retime.IsChecked == true;
    public bool RepeatFace => Retime && _repeat.IsChecked == true;
    public bool AddMotion => Retime && _motion.IsChecked == true;
    public double Strength { get; private set; } = 1;
    public SpecialAudioReplaceWindow(Window owner, string original)
    {
        Owner = owner; Title = "Special Audio Replace"; Width = 760; SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = new SolidColorBrush(Color.FromRgb(35,38,44)); Foreground = Brushes.WhiteSmoke;
        var panel = new StackPanel { Margin = new Thickness(16) }; Content = panel;
        panel.Children.Add(new TextBlock { Text = "Replacing: " + ConfigPathService.Display(original ?? "unavailable audio"), TextWrapping = TextWrapping.Wrap });
        void FileRow(string caption, TextBox box, bool audio)
        {
            panel.Children.Add(new TextBlock { Text = caption, Margin = new Thickness(0, 12, 0, 4) });
            var row = new DockPanel(); var browse = new Button { Content = "Browse…", Margin = new Thickness(8, 0, 0, 0) };
            DockPanel.SetDock(browse, Dock.Right); row.Children.Add(browse); row.Children.Add(box); panel.Children.Add(row);
            browse.Click += (_, _) =>
            {
                var picker = new OpenFileDialog { Title = caption, Filter = audio ? "Audio|*.wav;*.mp3;*.aiff;*.wma;*.m4a|All files|*.*" : "Command Update JSON|*.json",
                    InitialDirectory = File.Exists(AudioPath) ? Path.GetDirectoryName(AudioPath) : File.Exists(original) ? Path.GetDirectoryName(original) : "" };
                if (picker.ShowDialog(this) != true) return;
                box.Text = ConfigPathService.Display(picker.FileName);
                if (audio)
                {
                    string sidecar = Path.ChangeExtension(picker.FileName, ".timeline.json");
                    _update.Text = File.Exists(sidecar) ? ConfigPathService.Display(sidecar) : "";
                }
                Summary();
            };
            box.LostFocus += (_, _) => Summary();
        }
        FileRow("Replacement audio file", _audio, true);
        FileRow("Command Update JSON (optional; clear to replace audio only)", _update, false);
        foreach (var check in new[] { _retime, _repeat, _motion }) { check.Foreground = Brushes.WhiteSmoke; check.Margin = new Thickness(0, 8, 0, 0); panel.Children.Add(check); }
        var strength = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        strength.Children.Add(new TextBlock { Text = "Motion strength (%)", Width = 170 }); strength.Children.Add(_strength); panel.Children.Add(strength);
        panel.Children.Add(_summary);
        panel.Children.Add(new TextBlock { Text = "Re-timing requires cleared Skip to / End at trims on affected audio. Files outside Config are copied into Config/SpecialAudio. Apply is one Undo step.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray, Margin = new Thickness(0,0,0,12) });
        HelpSystem.EnableContextHelp(this, "special-audio-replace");
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var apply = new Button { Content = "Apply", IsDefault = true, MinWidth = 90 };
        apply.Click += (_, _) =>
        {
            try
            {
                if (!File.Exists(AudioPath)) throw new InvalidDataException("Select an existing replacement audio file.");
                if (!string.IsNullOrWhiteSpace(UpdatePath)) HeadroomTimeline.Load(UpdatePath);
                if (!double.TryParse(_strength.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double value) || !double.IsFinite(value) || value < 0 || value > 200)
                    throw new InvalidDataException("Motion strength must be 0–200%.");
                Strength = value / 100; DialogResult = true;
            }
            catch (Exception error) { _summary.Text = error.Message; }
        };
        buttons.Children.Add(apply); buttons.Children.Add(new Button { Content = "Cancel", IsCancel = true, MinWidth = 90, Margin = new Thickness(8,0,0,0) }); panel.Children.Add(buttons);
        Summary();
    }
    private void Summary()
    {
        bool update = !string.IsNullOrWhiteSpace(UpdatePath);
        _retime.IsEnabled = _repeat.IsEnabled = _motion.IsEnabled = _strength.IsEnabled = update;
        try { _summary.Text = update ? HeadroomTimeline.Load(UpdatePath).Summary : "No Command Update selected: replace audio only; preserve all command timing. A matching .timeline.json beside the selected audio is selected automatically."; }
        catch (Exception error) { _summary.Text = error.Message; }
    }
}
