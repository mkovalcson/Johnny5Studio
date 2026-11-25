using System.Windows;

namespace ServoAnimator;

public partial class MainWindow
{
    private string _editingCommandGroup;
    private HashSet<ServoCommand> _previewDragCommands = new();

    private void SetGroupEdit(string id)
    {
        if (!ApplyOpenCommandEditor()) return;
        if (id != null && _doc.HiddenCommandGroups.Remove(id)) RefreshGroupVisibility();
        _editingCommandGroup = id;
        UpdateGroupEditIndicator();
        Waveform.SetMarkerSelection(Array.Empty<double>());
        RefreshGroupManager();
    }

    private void UpdateGroupEditIndicator()
    {
        FinishGroupEditButton.Visibility = _editingCommandGroup == null ? Visibility.Collapsed : Visibility.Visible;
        if (_editingCommandGroup != null)
            FinishGroupEditButton.Content = $"Done editing {GroupDisplayName(_editingCommandGroup)}";
    }

    private void FinishGroupEdit_Click(object sender, RoutedEventArgs e) => SetGroupEdit(null);

    private string GroupEditScopeAt(double time, ServoCommand focus)
    {
        if (_editingCommandGroup == null || (focus != null && _doc.Commands.Contains(focus) && focus.GroupId != _editingCommandGroup))
            return null;
        return _doc.Commands.Any(c => c.GroupId == _editingCommandGroup && ServoCommand.TimeKey(c.OffsetSeconds) == ServoCommand.TimeKey(time))
            ? _editingCommandGroup : null;
    }

    private void SelectGroupCommandList(string id, double time, bool toggle)
    {
        var commands = VisibleCommands.Where(c => c.GroupId == id && ServoCommand.TimeKey(c.OffsetSeconds) == time).ToArray();
        if (toggle && commands.All(_selectedTimelineCommands.Contains)) _selectedTimelineCommands.ExceptWith(commands);
        else
        {
            if (!toggle) _selectedTimelineCommands.Clear();
            _selectedTimelineCommands.UnionWith(commands);
        }
        RefreshSelectedCommandMarkers();
    }

    private HashSet<ServoCommand> TimelineDragTargets(double? anchor)
    {
        var targets = SelectedGroupCommands().ToHashSet();
        if (RippleTimeline.IsChecked != true || !anchor.HasValue) return targets;

        double time = anchor.Value;
        // An interior list can ripple within its group. The final list
        // shifts the rest of the sequence, including hidden commands.
        bool withinGroup = _editingCommandGroup != null &&
            targets.Any(c => c.GroupId == _editingCommandGroup && ServoCommand.TimeKey(c.OffsetSeconds) == time) &&
            _doc.Commands.Any(c => c.GroupId == _editingCommandGroup && ServoCommand.TimeKey(c.OffsetSeconds) > time);
        targets.UnionWith(_doc.Commands.Where(c => ServoCommand.TimeKey(c.OffsetSeconds) > time &&
            (!withinGroup || c.GroupId == _editingCommandGroup)));
        return targets;
    }

    private IEnumerable<(double Time, int Lane)> BuildDragPreview(double anchor)
    {
        _previewDragCommands = TimelineDragTargets(anchor);
        return _previewDragCommands.Select(c => (ServoCommand.TimeKey(c.OffsetSeconds), TimelineLanes.For(c.Servo)));
    }
}