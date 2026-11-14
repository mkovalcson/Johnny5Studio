using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ServoAnimator;

public sealed partial class RobotHeadView
{
    private readonly Expander _bodyPosePanel = new()
    {
        Header = "Full Body Pose", Width = 290, IsExpanded = false,
        Foreground = Brushes.WhiteSmoke, Background = new SolidColorBrush(Color.FromArgb(235, 28, 36, 46)),
        Padding = new Thickness(8), Visibility = Visibility.Collapsed
    };
    private readonly StackPanel _bodyPoseGroups = new();
    private readonly ScrollViewer _bodyPoseScroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly Dictionary<ServoNames, (Slider Slider, TextBlock Value)> _bodyPoseSliders = new();
    private UrdfScene _bodyPoseScene;
    private bool _updatingBodyPoseUi;

    private void InitializeBodyPoseControls()
    {
        _bodyPoseScroll.Content = _bodyPoseGroups;
        _bodyPosePanel.Content = _bodyPoseScroll;
        _poseOverlay.Children.Add(_bodyPosePanel);
        Panel.SetZIndex(_bodyPosePanel, 20);
        HelpSystem.SetTopic(_bodyPosePanel, "urdf-body");
    }

    private static string BodyPoseGroup(BodyControlDefinition control)
    {
        if(BodyControlCatalog.IsPairedGang(control.Servo))return "Paired arms · Ganged";
        if (control.Servo is ServoNames.B_PanelsOpen or ServoNames.B_LaserLower) return "Waist panels and laser";
        bool hand = control.Joint.Contains("Finger") || control.Joint.Contains("Thumb") || BodyControlCatalog.IsHandGang(control.Servo);
        if (control.Joint.EndsWith("Left")) return hand ? "Left hand" : "Left arm";
        if (control.Joint.EndsWith("Right")) return hand ? "Right hand" : "Right arm";
        return "Torso and neck lift";
    }

    private void RebuildBodyPoseControls()
    {
        _bodyPoseScene = _scene;
        _bodyPoseGroups.Children.Clear(); _bodyPoseSliders.Clear();
        if (_scene == null) return;
        foreach (var group in BodyControlCatalog.Controls.Where(HasBodyControl).GroupBy(BodyPoseGroup))
        {
            var items = new StackPanel();
            var section = new Expander { Header = group.Key, Foreground = Brushes.WhiteSmoke, IsExpanded = true, Content = items, Margin = new Thickness(0, 3, 0, 4) };
            _bodyPoseGroups.Children.Add(section);
            foreach (var control in group)
            {
                var label = new TextBlock { Text = control.Servo.ToString(), Foreground = Brushes.WhiteSmoke, TextWrapping = TextWrapping.Wrap, ToolTip = control.Description, Margin = new Thickness(0, 5, 0, 0) };
                items.Children.Add(label);
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                var bounds = ServoCommand.RangeFor(control.Servo);
                var slider = new Slider(); ConfigurePoseSlider(slider, control.Description, bounds.Min, bounds.Max, Orientation.Horizontal, 160, 28);
                slider.Value = BodyPosition(control.Servo);
                var value = new TextBlock { Width = 42, Foreground = Brushes.LightSkyBlue, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
                var reset = new Button { Content = "↺", Width = 27, Height = 25, Margin = new Thickness(5, 0, 0, 0), ToolTip = "Reset " + control.Servo + " to command 0" };
                _bodyPoseSliders[control.Servo] = (slider, value);
                slider.ValueChanged += (_, e) =>
                {
                    if (_updatingBodyPoseUi || !_poseEditEnabled) return;
                    ApplyBodyControl(control.Servo, e.NewValue);
                    UpdateBodyPoseControls();
                    PoseEdited?.Invoke(CapturePose());
                };
                reset.Click += (_, _) => { slider.Value = 0; };
                row.Children.Add(slider); row.Children.Add(value); row.Children.Add(reset); items.Children.Add(row);
            }
        }
        if (_bodyPoseSliders.Count == 0) return;
        var resetAll = new Button { Content = "Reset body pose", Margin = new Thickness(0, 8, 0, 5) };
        resetAll.Click += (_, _) =>
        {
            bool prior = _suppressCollisionRefresh; _suppressCollisionRefresh = true;
            try { foreach (var servo in _bodyPoseSliders.Keys.Where(s => !BodyControlCatalog.IsHandGang(s))) ApplyBodyControl(servo, 0); }
            finally { _suppressCollisionRefresh = prior; }
            UpdateBodyPoseControls(); if (!prior) RefreshCollisionState();
            PoseEdited?.Invoke(CapturePose());
        };
        _bodyPoseGroups.Children.Add(resetAll);
    }

    private void UpdateBodyPoseControls()
    {
        if (!ReferenceEquals(_bodyPoseScene, _scene)) RebuildBodyPoseControls();
        _bodyPosePanel.Visibility = _poseEditEnabled && !_cleanViewActive && _bodyPoseSliders.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_bodyPosePanel.Visibility != Visibility.Visible) return;
        _bodyPosePanel.Width = Math.Min(290, Math.Max(200, ActualWidth - 16));
        _bodyPoseScroll.MaxHeight = Math.Max(60, ActualHeight - 160);
        Canvas.SetRight(_bodyPosePanel, 8); Canvas.SetTop(_bodyPosePanel, 76);
        _updatingBodyPoseUi = true;
        try
        {
            foreach (var item in _bodyPoseSliders)
            {
                double position = BodyPosition(item.Key);
                item.Value.Slider.Value = position;
                if (BodyControlCatalog.IsHandGang(item.Key))
                {
                    item.Value.Value.Text = position.ToString("0") + "%";
                    item.Value.Value.ToolTip = "0% closed · 100% fully open. Shows average opening after individual joint edits.";
                    continue;
                }
                item.Value.Value.Text = position.ToString("0.0");
                if(BodyControlCatalog.IsPairedGang(item.Key)){item.Value.Value.ToolTip="Symmetric pair · uses both calibrated joint ranges";continue;}
                var definition = BodyControlCatalog.Find(item.Key);
                var setting = _urdfConfiguration?.BodySetting(definition) ?? BodyMotionSetting.Default(definition);
                item.Value.Value.ToolTip = $"{setting.Map(position, definition) * 180 / Math.PI:0.##}°";
            }
        }
        finally { _updatingBodyPoseUi = false; }
    }
}
