using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ServoAnimator;

/// <summary>Small form for a text command's settings (Build… in Edit Commands). Callers add labelled
/// inputs, then read them in <see cref="Apply"/>, which returns the command text or throws a message
/// that is shown without closing the window.</summary>
internal sealed class CommandSettingsWindow : Window
{
    private readonly StackPanel _fields = new() { Margin = new Thickness(12), Background = Brushes.WhiteSmoke };
    private readonly TextBlock _status = new() { Foreground = Brushes.Gold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    private readonly bool _blackTheme;
    internal Func<string> Apply { get; set; }
    internal string Result { get; private set; }

    internal CommandSettingsWindow(string title, string introduction, bool blackTheme = false)
    {
        _blackTheme = blackTheme;
        HelpSystem.EnableContextHelp(this, title == "Disassemble" ? "urdf-effects" : title == "Video Clip" ? "video-clips"
            : title.Contains("TV", StringComparison.OrdinalIgnoreCase) ? "urdf-tv" : "commands");
        Title = title; Width = 560; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(28, 34, 44)); Foreground = Brushes.WhiteSmoke;
        if (blackTheme)
        {
            Background = _fields.Background = Brushes.Black;
            Foreground = Brushes.White;
            foreach (string key in new[] { "AppBackground", "PanelBackground", "ElevatedPanelBackground", "ControlBackground" })
                Resources[key] = Brushes.Black;
            Resources["PrimaryText"] = Brushes.White;
            Resources["SecondaryText"] = Brushes.LightGray;
            Resources["ControlBorder"] = Brushes.Gray;
            Resources["ControlHover"] = new SolidColorBrush(Color.FromRgb(40, 40, 40));
            Resources["SelectionBackground"] = new SolidColorBrush(Color.FromRgb(55, 55, 55));
        }
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var dock = new DockPanel { Margin = new Thickness(14) }; Content = dock;
        if (!string.IsNullOrWhiteSpace(introduction))
        {
            var intro = new TextBlock { Text = introduction, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
            DockPanel.SetDock(intro, Dock.Top); dock.Children.Add(intro);
        }
        var bottom = new StackPanel(); DockPanel.SetDock(bottom, Dock.Bottom); dock.Children.Add(bottom);
        bottom.Children.Add(_status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        var ok = new Button { Content = "Apply", IsDefault = true, Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(16, 6, 16, 6) };
        buttons.Children.Add(ok); buttons.Children.Add(cancel); bottom.Children.Add(buttons);
        dock.Children.Add(_fields);
        ok.Click += (_, _) =>
        {
            try { Result = Apply?.Invoke(); DialogResult = true; }
            catch (Exception ex) { _status.Text = ex.Message; }
        };
    }

    private DockPanel Row(string label, string help)
    {
        var block = new StackPanel { Margin = new Thickness(0, 4, 0, 6) }; _fields.Children.Add(block);
        var row = new DockPanel(); block.Children.Add(row);
        row.Children.Add(new TextBlock { Text = label, Width = 150, Foreground = _blackTheme ? Brushes.White : Brushes.Black, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        if (!string.IsNullOrWhiteSpace(help))
            block.Children.Add(new TextBlock { Text = help, Foreground = _blackTheme ? Brushes.LightGray : Brushes.DimGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(150, 2, 0, 0), FontSize = 11.5 });
        return row;
    }

    /// <summary>Number input; the returned reader parses (current or invariant culture) and range-checks it.</summary>
    internal Func<double> AddNumber(string label, double value, double min, double max, string help)
    {
        var box = new TextBox { Text = value.ToString(CultureInfo.CurrentCulture), Width = 120, HorizontalAlignment = HorizontalAlignment.Left };
        Row(label, help + $" ({min.ToString(CultureInfo.CurrentCulture)} to {max.ToString(CultureInfo.CurrentCulture)})").Children.Add(box);
        return () =>
        {
            if (!double.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double v) &&
                !double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                throw new InvalidOperationException(label + " must be a number.");
            if (!double.IsFinite(v) || v < min || v > max) throw new InvalidOperationException($"{label} must be between {min} and {max}.");
            return v;
        };
    }

    internal Func<int> AddChoice(string label, IReadOnlyList<string> options, int selected, string help)
    {
        var combo = new ComboBox { Width = 260, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var option in options) combo.Items.Add(option);
        combo.SelectedIndex = Math.Clamp(selected, 0, options.Count - 1);
        Row(label, help).Children.Add(combo);
        return () => combo.SelectedIndex;
    }

    internal Func<bool> AddCheck(string label, bool value, string help)
    {
        var check = new CheckBox { IsChecked = value, VerticalAlignment = VerticalAlignment.Center };
        Row(label, help).Children.Add(check);
        return () => check.IsChecked == true;
    }

    internal Func<string> AddText(string label, string value, string help)
    {
        var box = new TextBox { Text = value ?? "" };
        Row(label, help).Children.Add(box);
        return () => box.Text.Trim();
    }

    /// <summary>Path input with a Browse… button.</summary>
    internal Func<string> AddFile(string label, string value, string filter, string help)
    {
        var row = Row(label, help);
        var browse = new Button { Content = "Browse…", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(6, 0, 0, 0) };
        DockPanel.SetDock(browse, Dock.Right); row.Children.Add(browse);
        var box = new TextBox { Text = ConfigPathService.Display(value), ToolTip = "Path relative to Config" }; row.Children.Add(box);
        browse.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Title = label, Filter = filter };
            if (dialog.ShowDialog(this) == true) box.Text = ConfigPathService.Display(dialog.FileName);
        };
        return () => box.Text.Trim();
    }
}
