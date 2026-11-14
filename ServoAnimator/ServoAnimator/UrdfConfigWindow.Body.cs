using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ServoAnimator;

public partial class UrdfConfigWindow
{
    private void BuildBodyControls()
    {
        BodyControlsPanel.Children.Clear();
        BodyControlsPanel.Children.Add(new TextBlock
        {
            Text = "URDF body only. Command and controller values run from −100 to +100. Minimum and maximum degrees override the model's declared limits except for ShoulderHug, which stops at ±0.5 radian. Command 0 uses the built pose (or the nearest endpoint if your range excludes zero). Save Default persists these ranges; use timeline commands to save movement.",
            Foreground = Brushes.WhiteSmoke, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8)
        });
        var cards = new Grid();
        cards.ColumnDefinitions.Add(new ColumnDefinition());
        cards.ColumnDefinitions.Add(new ColumnDefinition());
        BodyControlsPanel.Children.Add(cards);
        int row = -1;
        foreach (var control in BodyControlCatalog.PairedPicklistControls)
        {
            bool right = control.Joint.EndsWith("Right", StringComparison.Ordinal);
            bool left = control.Joint.EndsWith("Left", StringComparison.Ordinal);
            if (!right) { row++; cards.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); }
            var panel = new StackPanel { Margin = new Thickness(8) };
            var card = new Border { Child = panel, BorderBrush = Brushes.SlateGray, BorderThickness = new Thickness(1), Margin = new Thickness(4) };
            Grid.SetRow(card, row); Grid.SetColumn(card, right ? 1 : 0);
            if (!left && !right) Grid.SetColumnSpan(card, 2);
            cards.Children.Add(card);
            var heading = new DockPanel { LastChildFill = true };
            var icon = new Image
            {
                Source = ServoIconProvider.For(control.Servo), Width = 24, Height = 24,
                Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false
            };
            DockPanel.SetDock(icon, Dock.Left);
            heading.Children.Add(icon);
            heading.Children.Add(new TextBlock
            {
                Text = control.Servo + " · " + control.Description,
                Foreground = Brushes.WhiteSmoke, FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center
            });
            panel.Children.Add(heading);
            if(BodyControlCatalog.IsPairedGang(control.Servo))
            {
                panel.Children.Add(new TextBlock { Text="Ganged control. Uses the left and right joint ranges below, including their Reverse settings. Matching physical angles keep motion symmetric; travel is limited to the range available on both sides.", Foreground=Brushes.WhiteSmoke,TextWrapping=TextWrapping.Wrap });
                var position=new TextBlock { Text="Position: 0",Foreground=Brushes.LightSkyBlue };
                var preview=new Slider { Minimum=-100,Maximum=100,SmallChange=1,LargeChange=10 };
                preview.ValueChanged+=(_,_)=>{position.Text=$"Position: {preview.Value:0}";_previewGang?.Invoke(control.Servo,preview.Value);};
                panel.Children.Add(position);panel.Children.Add(preview);continue;
            }
            if (BodyControlCatalog.IsHandGang(control.Servo))
            {
                panel.Children.Add(new TextBlock { Text = "Uses the configured ranges of this hand's finger and thumb joints. 0% closes; 100% opens. Closing safeguards still apply.", Foreground = Brushes.WhiteSmoke, TextWrapping = TextWrapping.Wrap });
                var percent = new TextBlock { Text = "0%", Foreground = Brushes.LightSkyBlue };
                var opening = new Slider { Minimum = 0, Maximum = 100, SmallChange = 1, LargeChange = 10 };
                opening.ValueChanged += (_, _) => { percent.Text = opening.Value.ToString("0") + "%"; _previewGang?.Invoke(control.Servo, opening.Value); };
                panel.Children.Add(percent); panel.Children.Add(opening);
                continue;
            }
            var setting = _config.BodySetting(control);
            var defaults = BodyMotionSetting.Default(control);
            bool pivotStop = control.Servo is ServoNames.B_ShoulderHugLeft or ServoNames.B_ShoulderHugRight;
            double physicalMinimum = control.MinRadians * 180 / Math.PI;
            double physicalMaximum = control.MaxRadians * 180 / Math.PI;
            var range = new WrapPanel { Margin = new Thickness(0, 5, 0, 0) }; panel.Children.Add(range);
            TextBox Field(string label, double value)
            {
                range.Children.Add(new TextBlock { Text = label, Foreground = Brushes.WhiteSmoke, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
                var box = new TextBox { Text = value.ToString("0.######", CultureInfo.CurrentCulture), Width = 100, Margin = new Thickness(0, 0, 14, 0) }; range.Children.Add(box); return box;
            }
            var minimum = Field("Minimum (degrees)", setting.MinDegrees);
            var maximum = Field("Maximum (degrees)", setting.MaxDegrees);
            var reverse = new CheckBox { Content = "Reversed", Foreground = Brushes.WhiteSmoke, IsChecked = setting.Reverse, VerticalAlignment = VerticalAlignment.Center }; range.Children.Add(reverse);
            // All independent body controls are revolute joints, including the lift linkage.
            var dial = new AngularRangeDial
            {
                Width = 174, Height = 174, HorizontalAlignment = HorizontalAlignment.Left,
                MinimumExtent = setting.MinDegrees, MaximumExtent = setting.MaxDegrees,
                ZeroExtent = Math.Clamp(0, setting.MinDegrees, setting.MaxDegrees), Reverse = setting.Reverse,
                ToolTip = "Drag the Minimum and Maximum handles to change the angular range. Use the position slider below to preview movement."
            };
            panel.Children.Add(dial);
            bool syncingDial = false;
            void SyncDial()
            {
                syncingDial = true;
                try
                {
                    dial.EditorMinimum = pivotStop ? physicalMinimum : Math.Min(-180, setting.MinDegrees);
                    dial.EditorMaximum = pivotStop ? physicalMaximum : Math.Max(180, setting.MaxDegrees);
                    dial.ZeroExtent = Math.Clamp(0, setting.MinDegrees, setting.MaxDegrees);
                    dial.MinimumExtent = setting.MinDegrees; dial.MaximumExtent = setting.MaxDegrees;
                    dial.Reverse = setting.Reverse;
                }
                finally { syncingDial = false; }
            }
            SyncDial();
            var caption = new TextBlock { Text = "Position: 0 · angle: 0°", Foreground = Brushes.LightSkyBlue, Margin = new Thickness(0, 5, 0, 0) }; panel.Children.Add(caption);
            var slider = new Slider { Minimum = -100, Maximum = 100, Value = 0, SmallChange = 1, LargeChange = 10 }; panel.Children.Add(slider);
            void Preview()
            {
                SyncDial();
                caption.Text = $"Position: {slider.Value:0.0} · angle: {setting.Map(slider.Value, control) * 180 / Math.PI:0.##}°";
                _previewGang?.Invoke(control.Servo, slider.Value);
            }
            bool CommitRange()
            {
                if (!double.TryParse(minimum.Text, out double min) || !double.TryParse(maximum.Text, out double max) ||
                    !double.IsFinite(min) || !double.IsFinite(max) || min >= max ||
                    (pivotStop && (min < physicalMinimum - 1e-6 || max > physicalMaximum + 1e-6)))
                {
                    ConfigPathText.Text = pivotStop
                        ? $"{control.Servo}: keep the range within ±0.5 radian (±28.64789°)."
                        : $"{control.Servo}: enter finite degree values with minimum less than maximum.";
                    minimum.Text = setting.MinDegrees.ToString("0.######"); maximum.Text = setting.MaxDegrees.ToString("0.######"); return false;
                }
                if (pivotStop) { min = Math.Max(min, physicalMinimum); max = Math.Min(max, physicalMaximum); }
                setting.MinDegrees = min; setting.MaxDegrees = max;
                Preview(); return true;
            }
            dial.ExtentsChanged += (_, _) =>
            {
                if (syncingDial) return;
                if (dial.MinimumExtent >= dial.MaximumExtent) { SyncDial(); return; }
                setting.MinDegrees = pivotStop ? Math.Max(dial.MinimumExtent, physicalMinimum) : dial.MinimumExtent;
                setting.MaxDegrees = pivotStop ? Math.Min(dial.MaximumExtent, physicalMaximum) : dial.MaximumExtent;
                minimum.Text = setting.MinDegrees.ToString("0.######", CultureInfo.CurrentCulture);
                maximum.Text = setting.MaxDegrees.ToString("0.######", CultureInfo.CurrentCulture);
                Preview();
            };
            minimum.LostFocus += (_, _) => CommitRange(); maximum.LostFocus += (_, _) => CommitRange();
            reverse.Click += (_, _) => { setting.Reverse = reverse.IsChecked == true; Preview(); };
            slider.ValueChanged += (_, _) => Preview();
            var zero = new Button { Content = "Zero position", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
            zero.Click += (_, _) => { slider.Value = 0; Preview(); }; panel.Children.Add(zero);
            var restore = new Button { Content = "Restore model range", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
            restore.Click += (_, _) =>
            {
                setting.MinDegrees = defaults.MinDegrees; setting.MaxDegrees = defaults.MaxDegrees;
                minimum.Text = setting.MinDegrees.ToString("0.######"); maximum.Text = setting.MaxDegrees.ToString("0.######"); Preview();
            };
            panel.Children.Add(restore);
        }
        BodyControlsPanel.Children.Add(new TextBlock
        {
            Text = "Automatic linkage controls: B_NeckLiftLevel follows B_NeckLift × −1; B_NeckLiftFrontRod follows B_NeckLift using the URDF multiplier (0.616383 in v32). These followers are not independently mapped or keyed. Tracks and caster have no driven joints in this model.",
            Foreground = Brushes.LightSkyBlue, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8)
        });
    }
}
