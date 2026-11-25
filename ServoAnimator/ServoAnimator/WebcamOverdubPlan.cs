using System.Windows;
using System.Windows.Controls;

namespace ServoAnimator;

internal sealed class WebcamOverdubPlan
{
    internal static readonly ServoNames[] Axes = [ServoNames.NeckTurn, ServoNames.NeckNodUp, ServoNames.NeckTiltRight];
    private readonly HashSet<ServoNames> _faceAxes;
    private readonly bool _directPistons;
    private readonly HashSet<ServoNames> _controllerAxes;
    internal IEnumerable<ServoNames> LogicalNeckAxes => _directPistons || _controllerAxes.Any(NeckMix.IsNeckParent)
        ? null : _faceAxes.Where(NeckMix.IsNeckParent);
    internal bool SuppressFacePistonOutput => !_directPistons && !_faceAxes.Any(NeckMix.IsNeckParent);
    internal bool TakesLogicalAxis(ServoNames servo) => !_directPistons && NeckMix.IsNeckParent(servo) && _faceAxes.Contains(servo);

    private WebcamOverdubPlan(HashSet<ServoNames> faceAxes, HashSet<ServoNames> controllerAxes, bool directPistons)
    {
        _faceAxes = faceAxes;
        _controllerAxes = controllerAxes;
        _directPistons = directPistons;
    }

    private static bool IsPistonCommand(ServoCommand command) =>
        NeckMix.IsNeckParent(command.Servo) && command.Control is RobotControls control && NeckMix.IsPiston(control);

    internal static WebcamOverdubPlan Prepare(Window owner, IEnumerable<ServoCommand> commands, double start,
        double end, ControllerRecordingWindow setup, FaceTrackingSettings settings)
    {
        // A prior key may still be driving the neck when this take starts.
        var keys = commands.Where(c => Axes.Contains(c.Servo)).ToArray();
        var relevant = keys.Where(c => c.OffsetSeconds >= start && c.OffsetSeconds <= end)
            .Concat(keys.Where(c => c.OffsetSeconds < start).GroupBy(c => (c.Servo, c.Control))
                .Select(g => g.MaxBy(c => c.OffsetSeconds))).ToArray();
        double lastParentTime = keys.Where(c => NeckMix.IsNeckParent(c.Servo) && !c.Control.HasValue && c.OffsetSeconds <= start)
            .Select(c => c.OffsetSeconds).DefaultIfEmpty(double.NegativeInfinity).Max();
        bool existingPistons = relevant.Any(c => IsPistonCommand(c) &&
            (c.OffsetSeconds >= start || c.OffsetSeconds > lastParentTime));
        var pistonMappings = new HashSet<RobotControls>();
        var available = new HashSet<ServoNames>();
        foreach (var mapping in settings.Mappings.Where(m => m.Enabled))
        {
            if (!ControllerTargets.TryServo(mapping.Target, out var servo, out var child) || !Axes.Contains(servo)) continue;
            bool armed = setup.ArmedControls.Contains(servo) ||
                NeckMix.IsNeckParent(servo) && (setup.ArmedControls.Any(NeckMix.IsNeckParent) ||
                    child.HasValue && setup.ArmedChildren.Any(c => c.Control == child.Value));
            if (!armed) continue;
            if (child is RobotControls control && NeckMix.IsPiston(control)) pistonMappings.Add(control);
            else available.Add(servo);
        }

        bool mappedPistons = pistonMappings.Count > 0;
        var face = new HashSet<ServoNames>();
        var rows = new List<(string Label, ServoNames[] Axes, bool CanUseFace, bool Conflict)>();
        rows.Add(("Neck Turn", [ServoNames.NeckTurn], available.Contains(ServoNames.NeckTurn),
            relevant.Any(c => c.Servo == ServoNames.NeckTurn)));
        if (mappedPistons || existingPistons)
        {
            rows.Add(("Neck Nod / Tilt (piston pair)", [ServoNames.NeckNodUp, ServoNames.NeckTiltRight],
                pistonMappings.Count == 2 || available.Contains(ServoNames.NeckNodUp) && available.Contains(ServoNames.NeckTiltRight),
                relevant.Any(c => NeckMix.IsNeckParent(c.Servo))));
        }
        else
        {
            rows.Add(("Neck Nod", [ServoNames.NeckNodUp], available.Contains(ServoNames.NeckNodUp),
                relevant.Any(c => c.Servo == ServoNames.NeckNodUp)));
            rows.Add(("Neck Tilt", [ServoNames.NeckTiltRight], available.Contains(ServoNames.NeckTiltRight),
                relevant.Any(c => c.Servo == ServoNames.NeckTiltRight)));
        }

        foreach (var row in rows.Where(r => !r.Conflict && r.CanUseFace)) face.UnionWith(row.Axes);
        if (rows.Any(r => r.Conflict))
        {
            var dialog = new Window
            {
                Owner = owner, Title = "Webcam Overdub · Neck sources",
                Width = 560, SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            dialog.SetResourceReference(Window.BackgroundProperty, "AppBackground");
            dialog.SetResourceReference(Window.ForegroundProperty, "PrimaryText");
            var content = new StackPanel { Margin = new Thickness(18) };
            dialog.Content = content;
            content.Children.Add(new TextBlock
            {
                Text = $"Existing neck motion is used during the planned take ({start:F3}–{end:F3} s). Choose a source for each axis.",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10)
            });
            var selections = new List<(ServoNames[] Axes, ComboBox Source)>();
            foreach (var row in rows.Where(r => r.Conflict))
            {
                var line = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
                var pick = new ComboBox { Width = 138, ItemsSource = new[] { "Existing", "Face tracking" }, SelectedIndex = 0, IsEnabled = row.CanUseFace };
                System.Windows.Automation.AutomationProperties.SetName(pick, row.Label + " source");
                DockPanel.SetDock(pick, Dock.Right);
                line.Children.Add(pick);
                line.Children.Add(new TextBlock
                {
                    Text = row.Label + (row.CanUseFace ? "" : " · no armed face mapping"),
                    VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap
                });
                content.Children.Add(line);
                selections.Add((row.Axes, pick));
            }
            if (mappedPistons || existingPistons)
                content.Children.Add(new TextBlock
                {
                    Text = "Direct piston keys or mappings combine nod and tilt. Choose one source for the pair. Face tracking needs both mappings armed.",
                    TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0)
                });
            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 15, 0, 0) };
            var next = new Button { Content = "Open overdub preview", IsDefault = true, Padding = new Thickness(10, 5, 10, 5) };
            next.Click += (_, _) => dialog.DialogResult = true;
            actions.Children.Add(next);
            actions.Children.Add(new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(8, 0, 0, 0) });
            content.Children.Add(actions);
            if (dialog.ShowDialog() != true) return null;
            foreach (var (axes, source) in selections)
                if (source.SelectedIndex == 1) face.UnionWith(axes);
        }
        var controller = new HashSet<ServoNames>();
        if (setup.UsesController)
        {
            // An unopposed, unmapped neck control can still use the selected controller.
            if (!rows[0].Conflict && !face.Contains(ServoNames.NeckTurn) && setup.ArmedControls.Contains(ServoNames.NeckTurn))
                controller.Add(ServoNames.NeckTurn);
            if (!relevant.Any(c => NeckMix.IsNeckParent(c.Servo)) && !face.Any(NeckMix.IsNeckParent) &&
                !mappedPistons && !available.Any(NeckMix.IsNeckParent) &&
                (setup.ArmedControls.Any(NeckMix.IsNeckParent) || setup.ArmedChildren.Any(c => NeckMix.IsPiston(c.Control))))
                controller.UnionWith(Axes.Where(NeckMix.IsNeckParent));
        }
        return new WebcamOverdubPlan(face, controller, mappedPistons && face.Any(NeckMix.IsNeckParent));
    }

    internal bool UsesMapping(FaceMapping mapping)
    {
        if (!ControllerTargets.TryServo(mapping.Target, out var servo, out var child) || !Axes.Contains(servo)) return true;
        if (child is RobotControls control && NeckMix.IsPiston(control)) return _directPistons;
        return _faceAxes.Contains(servo);
    }

    internal void RestrictRecordingControls(HashSet<ServoNames> parents,
        HashSet<(ServoNames Servo, RobotControls Control)> children, FaceTrackingSettings settings, bool webcamOnly)
    {
        bool KeepsAxis(ServoNames s) => !Axes.Contains(s) || _faceAxes.Contains(s) || _controllerAxes.Contains(s);
        parents.RemoveWhere(s => !KeepsAxis(s));
        children.RemoveWhere(c => !KeepsAxis(c.Servo));
        if (!webcamOnly) return;
        var mapped = settings.OwnedControls;
        foreach (var servo in parents.Where(s => !Axes.Contains(s)).ToArray())
        {
            var members = ServoConfiguration.ControlsFor(servo);
            if (members.Length > 0 && members.All(mapped.Contains)) continue;
            parents.Remove(servo);
            foreach (var member in members.Where(mapped.Contains)) children.Add((servo, member));
        }
        children.RemoveWhere(c => !mapped.Contains(c.Control));
    }

    internal string SourceSummary => string.Join("   ·   ", Axes.Select(servo =>
        $"{Label(servo)}: {(_faceAxes.Contains(servo) ? "Face tracking" : _controllerAxes.Contains(servo) ? "Controller" : "Existing")}"));

    private static string Label(ServoNames servo) => servo switch
    {
        ServoNames.NeckTurn => "Neck Turn",
        ServoNames.NeckNodUp => "Neck Nod",
        _ => "Neck Tilt"
    };
}
