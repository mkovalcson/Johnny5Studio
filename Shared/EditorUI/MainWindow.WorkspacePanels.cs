using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace ServoAnimator;

public partial class MainWindow
{
    private Window _controlFloat, _streamDeckFloat, _groupManager;
    private readonly Dictionary<ControllerKind, (Window Window, ControllerMappingDisplay Display)> _floatingControllers = new();
    private readonly List<NamedGroupTrack> _namedGroupTracks = new();
    private DispatcherTimer _groupTrackTimer;

    private sealed record GroupMetadata(Dictionary<string, int> Numbers, Dictionary<string, string> Names, HashSet<string> Timelines);
    private GroupMetadata CaptureGroupMetadata() => new(new(_doc.CommandGroupNumbers ?? new()), new(_doc.CommandGroupNames ?? new()), new(_doc.CommandGroupTimelines ?? new()));
    private void RestoreGroupMetadata(GroupMetadata groups)
    {
        if (groups == null) return;
        _doc.CommandGroupNumbers = new(groups.Numbers); _doc.CommandGroupNames = new(groups.Names); _doc.CommandGroupTimelines = new(groups.Timelines);
    }
    private bool _closingWorkspace, _workspaceWasVacant;
    private GridLength _dockedControlHeight = new(250);
    private bool WorkspaceTopRowVacated => _controlFloat != null && _urdfUndocked && UndockedControllerMappingHost.Visibility != Visibility.Visible;

    private void InitializeWorkspacePanels()
    {

        AudioTimelineArea.SizeChanged += (_, _) => NamedGroupTimelineScroller.MaxHeight = Math.Max(0, Math.Min(360, AudioTimelineArea.ActualHeight * .6));
        Waveform.ViewChanged += RedrawNamedGroupTracks;
        Waveform.SelectionMovePreviewChanged += RedrawNamedGroupTracks;
        // Invalidate only when the cursor changes; stopped editors do no repeated drawing.
        double previous = double.NaN;
        _groupTrackTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(40) };
        _groupTrackTimer.Tick += (_, _) =>
        {
            if (_namedGroupTracks.Count == 0 || previous == _cursorTime) return;
            previous = _cursorTime;
            foreach (var view in _namedGroupTracks) view.InvalidateVisual();
        };
        _groupTrackTimer.Start();
    }

    private void RedrawNamedGroupTracks()
    {
        foreach (var track in _namedGroupTracks) track.InvalidateVisual();
    }

    private Window WorkspaceWindow(string title, double width, double height, UIElement content)
    {
        var window = new Window { Title = title, Owner = this, Width = width, Height = height,
            MinWidth = 280, MinHeight = 160, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        window.SetResourceReference(Window.BackgroundProperty, "PanelBackground");
        window.SetResourceReference(Window.ForegroundProperty, "PrimaryText");
        var dock = new DockPanel();
        var header = new DockPanel { Margin = new Thickness(6) };
        var close = new Button { Content = "Dock back", Padding = new Thickness(10, 4, 10, 4) };
        close.Click += (_, _) => window.Close();
        DockPanel.SetDock(close, Dock.Right); header.Children.Add(close);
        header.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold });
        DockPanel.SetDock(header, Dock.Top); dock.Children.Add(header); dock.Children.Add(content);
        window.Content = dock;
        return window;
    }

    private void FloatControlView_Click(object sender, RoutedEventArgs e)
    {
        if (_controlFloat != null) { _controlFloat.Close(); return; }
        var host = (Grid)CommandsAtPointContent.Parent;
        host.Children.Remove(CommandsAtPointContent);
        _controlFloat = WorkspaceWindow("Control View", Math.Max(560, CommandsAtPointPanel.ActualWidth), 400, CommandsAtPointContent);
        _controlFloat.Closed += (_, _) =>
        {
            ((DockPanel)CommandsAtPointContent.Parent).Children.Remove(CommandsAtPointContent);
            host.Children.Insert(0, CommandsAtPointContent); _controlFloat = null;
            if (!_closingWorkspace) { RefreshActiveControllerMapping(); UpdateVacatedWorkspace(); }
        };
        _controlFloat.Show(); RefreshActiveControllerMapping(); UpdateVacatedWorkspace();
    }

    private void FloatXboxView_Click(object sender, RoutedEventArgs e) => ToggleFloatingController(ControllerKind.Xbox);
    private void FloatSteamView_Click(object sender, RoutedEventArgs e) => ToggleFloatingController(ControllerKind.Steam);
    private void ToggleFloatingController(ControllerKind kind)
    {
        if (_floatingControllers.TryGetValue(kind, out var existing)) { existing.Window.Close(); return; }
        var display = new ControllerMappingDisplay();
        var window = WorkspaceWindow(kind == ControllerKind.Xbox ? "Xbox Controller View" : "Steam Controller View", 680, 480, display);
        _floatingControllers.Add(kind, (window, display));
        window.Closed += (_, _) => { _floatingControllers.Remove(kind); if (!_closingWorkspace) RefreshActiveControllerMapping(); };
        window.Show(); RefreshActiveControllerMapping();
    }

    private void RefreshFloatingControllerViews()
    {
        foreach (var (kind, entry) in _floatingControllers)
            if (_controllerProfiles.TryGetValue(kind, out var profile))
                entry.Display.ShowMapping(profile, kind == ControllerKind.Xbox ? _lastXboxSample : _lastSteamSample, _faceTrackingOverride);
    }

    private void FloatStreamDeckView_Click(object sender, RoutedEventArgs e)
    {
        if (_streamDeckFloat != null) { _streamDeckFloat.Close(); return; }
        var parent = (Panel)StreamDeckControlPanel.Parent;
        int index = parent.Children.IndexOf(StreamDeckControlPanel);
        parent.Children.Remove(StreamDeckControlPanel);
        var body = new StackPanel { Margin = new Thickness(14) };
        body.Children.Add(StreamDeckControlPanel);
        body.Children.Add(new TextBlock { Text = "Connection and output controls. Key layouts remain in the Stream Deck application.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) });
        _streamDeckFloat = WorkspaceWindow("Stream Deck controls", 420, 230, body);
        _streamDeckFloat.Closed += (_, _) =>
        {
            body.Children.Remove(StreamDeckControlPanel); parent.Children.Insert(Math.Min(index, parent.Children.Count), StreamDeckControlPanel);
            _streamDeckFloat = null;
        };
        _streamDeckFloat.Show();
    }

    private void UpdateVacatedWorkspace()
    {
        if (TopEditorRow == null) return;
        bool vacant = WorkspaceTopRowVacated;
        if (vacant && TopEditorRow.Height.Value > 0) _dockedControlHeight = TopEditorRow.Height;
        CommandsAtPointPanel.Visibility = _controlFloat == null || ActiveControllerMapping.Visibility == Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;
        if (vacant) TopEditorRow.MinHeight = 0;
        else if (_workspaceWasVacant) TopEditorRow.MinHeight = 80;
        if (vacant) TopEditorRow.Height = new GridLength(0);
        else if (TopEditorRow.Height.Value <= 0) TopEditorRow.Height = _dockedControlHeight;
        CommandsTimelineSplitter.Visibility = vacant ? Visibility.Collapsed : Visibility.Visible;
        // Keep hardware usable when the editors above the timelines have floated away.
        Grid.SetRowSpan(HardwareViewport, vacant ? 7 : 3);
        Grid.SetColumnSpan(AudioTimelineArea, vacant ? 3 : 4);
        Grid.SetColumnSpan(SplineArea, vacant ? 3 : 4);
        _workspaceWasVacant = vacant;
    }
    private void DockAllViews_Click(object sender, RoutedEventArgs e) => DockAllWorkspaceViews();
    private void DockAllWorkspaceViews()
    {
        _controlFloat?.Close(); _streamDeckFloat?.Close();
        foreach (var entry in _floatingControllers.Values.ToArray()) entry.Window.Close();
        if (_urdfUndocked) SetUrdfUndocked(false);
    }
    private void CloseWorkspaceViews()
    {
        _closingWorkspace = true; _groupTrackTimer?.Stop();
        _groupManager?.Close(); _controlFloat?.Close(); _streamDeckFloat?.Close();
        foreach (var entry in _floatingControllers.Values.ToArray()) entry.Window.Close();
    }

    private string GroupDisplayName(string id) => _doc.CommandGroupNames.TryGetValue(id, out var name) && !string.IsNullOrWhiteSpace(name)
        ? name : $"Group {_doc.CommandGroupNumbers.GetValueOrDefault(id)}";
    private string[] GroupDisplayNames(IEnumerable<ServoCommand> commands) => commands
        .Select(c => c.GroupId).Where(id => !string.IsNullOrEmpty(id)).Distinct()
        .OrderBy(id => _doc.CommandGroupNumbers.GetValueOrDefault(id)).Select(GroupDisplayName).ToArray();

    private string SelectedGroupNameAtMarker(double time, int lane)
    {
        var names = GroupDisplayNames(_selectedTimelineCommands.Where(c => ServoCommand.TimeKey(c.OffsetSeconds) == time &&
            (lane < 0 || TimelineLanes.For(c.Servo) == lane)));
        return string.Join(", ", names);
    }

    private static void AddGroupMenuHeader(ContextMenu menu, params string[] names)
    {
        string label = names.Length == 1 ? "Group Name: " : "Group Names: ";
        var text = new TextBlock { Text = label + string.Join(", ", names), FontWeight = FontWeights.SemiBold,
            MaxWidth = 420, TextWrapping = TextWrapping.Wrap };
        text.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryText");
        var highlight = new Border { Child = text, CornerRadius = new CornerRadius(3), Padding = new Thickness(8, 4, 8, 4) };
        highlight.SetResourceReference(Border.BackgroundProperty, "SelectionBackground");
        menu.Items.Add(new MenuItem { Header = highlight, IsEnabled = false });
        menu.Items.Add(new Separator());
    }

    private void GroupManager_Click(object sender, RoutedEventArgs e) => ShowGroupManager();
    private void ShowGroupManager()
    {
        if (_groupManager != null) { _groupManager.Activate(); return; }
        _groupManager = new Window { Title = "Named groups and timelines", Owner = this, Width = 850, Height = 580, MinWidth = 620, MinHeight = 300,
            WindowStartupLocation = WindowStartupLocation.CenterOwner };
        _groupManager.SetResourceReference(Window.BackgroundProperty, "PanelBackground");
        _groupManager.SetResourceReference(Window.ForegroundProperty, "PrimaryText");
        _groupManager.Closed += (_, _) => _groupManager = null;
        RefreshGroupManager(); _groupManager.Show();
    }

    private void RefreshGroupManager()
    {
        if (_groupManager == null) return;
        EnsureCommandGroupNumbers();
        var root = new DockPanel { Margin = new Thickness(12) };
        var instructions = new TextBlock { Text = "Name and organize command groups. Timeline rows and command visibility are separate; neither mutes playback.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(instructions, Dock.Top); root.Children.Add(instructions);
        var footer = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        void Footer(string text, Action action) { var b = new Button { Content = text, Margin = new Thickness(4), Padding = new Thickness(9, 5, 9, 5) }; b.Click += (_, _) => action(); footer.Children.Add(b); }
        Footer("Group selected commands", () => { SetPersistentCommandGroup(true); RefreshGroupManager(); });
        Footer("Show all group timelines", () => ChangeGroupMetadata("Show all group timelines", () => _doc.CommandGroupTimelines.UnionWith(_doc.Commands.Select(c => c.GroupId).Where(id => !string.IsNullOrEmpty(id)))));
        Footer("Close", () => _groupManager.Close());
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var rows = new StackPanel();
        var document = _doc;
        foreach (var grouping in _doc.Commands.Where(c => !string.IsNullOrEmpty(c.GroupId)).GroupBy(c => c.GroupId).OrderBy(g => _doc.CommandGroupNumbers[g.Key]))
        {
            string id = grouping.Key; var commands = grouping.ToArray();
            var card = new StackPanel { Margin = new Thickness(0, 4, 0, 8) };
            var nameRow = new DockPanel();
            var name = new TextBox { Text = GroupDisplayName(id), MinWidth = 200, Margin = new Thickness(3), MaxLength = 100 };
            var rename = new Button { Content = "Rename", Margin = new Thickness(3), Padding = new Thickness(8, 3, 8, 3) };
            rename.Click += (_, _) => { if (_doc != document) { RefreshGroupManager(); return; } string value = name.Text.Trim(); if (value.Length > 0) ChangeGroupMetadata("Rename group", () => _doc.CommandGroupNames[id] = value); };
            DockPanel.SetDock(rename, Dock.Right); nameRow.Children.Add(rename); nameRow.Children.Add(name); card.Children.Add(nameRow);
            double start = commands.Min(c => c.OffsetSeconds), end = commands.Max(c => c.OffsetSeconds);
            card.Children.Add(new TextBlock { Text = $"{start:0.###}–{end:0.###} s · duration {end-start:0.###} s · {commands.Length} commands · " +
                string.Join(", ", commands.Select(c => TimelineLanes.For(c.Servo) switch { 0 => "Head / Neck / Audio", 1 => "Body", _ => "URDF / Scene" }).Distinct()), Margin = new Thickness(4) });
            card.Children.Add(new TextBlock { Text = string.Join(", ", commands.Select(c => c.Control?.ToString() ?? c.Servo.ToString()).Distinct()), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4), FontSize = 11 });
            var actions = new WrapPanel();
            void ActionButton(string text, Action action) { var b = new Button { Content = text, Margin = new Thickness(3), Padding = new Thickness(8, 3, 8, 3) }; b.Click += (_, _) => { if (_doc == document) action(); else RefreshGroupManager(); }; actions.Children.Add(b); }
            var timeline = new CheckBox { Content = "Own timeline", IsChecked = _doc.CommandGroupTimelines.Contains(id), Margin = new Thickness(6), VerticalAlignment = VerticalAlignment.Center };
            timeline.Click += (_, _) => { if (_doc == document) SetGroupTimeline(id, timeline.IsChecked == true); else RefreshGroupManager(); }; actions.Children.Add(timeline);
            ActionButton("Select", () => { SelectNamedGroup(id, AddGroupToSelection); SetCursor(start); });
            ActionButton(_editingCommandGroup == id ? "Finish group edit" : "Edit commands",
                () => SetGroupEdit(_editingCommandGroup == id ? null : id));
            ActionButton(_doc.HiddenCommandGroups.Contains(id) ? "Show commands" : "Hide commands", () => { if (!_doc.HiddenCommandGroups.Remove(id)) _doc.HiddenCommandGroups.Add(id); RefreshGroupVisibility(); RefreshGroupManager(); });
            ActionButton("Duplicate at cursor", () => DuplicateNamedGroup(id));
            ActionButton("Offset…", () => OffsetNamedGroup(id));
            ActionButton("Ungroup", () => EditNamedGroup(id, "Ungroup commands", list => { foreach (var c in list) c.GroupId = ""; }));
            card.Children.Add(actions); rows.Children.Add(card);
        }
        if (rows.Children.Count == 0) rows.Children.Add(new TextBlock { Text = "No groups yet. Select command markers, then choose Group selected commands.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) });
        root.Children.Add(new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        _groupManager.Content = root;
    }

    private void ChangeGroupMetadata(string description, Action action)
    {
        if (!ApplyOpenCommandEditor()) return;
        PushUndo(description); action(); RefreshMarkers(); UpdateDocumentStatusIndicators(); RefreshGroupManager();
    }
    private void SetGroupTimeline(string id, bool show) => ChangeGroupMetadata(show ? "Show group timeline" : "Hide group timeline", () => { if (show) _doc.CommandGroupTimelines.Add(id); else _doc.CommandGroupTimelines.Remove(id); });
    private void SelectNamedGroup(string id) => SelectNamedGroup(id, toggle: false);

    private void SelectNamedGroup(string id, bool toggle)
    {
        if (_doc.HiddenCommandGroups.Remove(id)) RefreshGroupVisibility();
        var commands = VisibleCommands.Where(c => c.GroupId == id).ToArray();
        if (toggle && commands.All(_selectedTimelineCommands.Contains)) _selectedTimelineCommands.ExceptWith(commands);
        else
        {
            if (!toggle) _selectedTimelineCommands.Clear();
            _selectedTimelineCommands.UnionWith(commands);
        }
        RefreshSelectedCommandMarkers();
    }

    private static bool AddGroupToSelection => Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ||
        Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

    private void EditNamedGroup(string id, string description, Action<List<ServoCommand>> action)
    {
        if (!ApplyOpenCommandEditor()) return;
        if (IsRunning) PausePlayback();
        var commands = _doc.Commands.Where(c => c.GroupId == id).ToList();
        if (commands.Count == 0) return;
        ApplySelectedGroupEdit(description, () => action(commands)); RefreshGroupManager();
    }
    private void MoveNamedGroup(string id, double delta)
    {
        EditNamedGroup(id, "Move group", commands =>
        {
            delta = Math.Max(-commands.Min(c => c.OffsetSeconds), delta);
            _doc.Commands.RemoveAll(commands.Contains);
            foreach (var c in commands) c.OffsetSeconds = ServoCommand.TimeKey(c.OffsetSeconds + delta);
            _doc.Commands.AddRange(commands);
        });
    }
    private void OffsetNamedGroup(string id)
    {
        var dialog = new CommandGroupDialog(this, false, 0, shiftGroup: true);
        if (dialog.ShowDialog() == true) MoveNamedGroup(id, dialog.Offset);
    }
    private void DuplicateNamedGroup(string id)
    {
        EditNamedGroup(id, "Duplicate group at cursor", commands =>
        {
            double start = commands.Min(c => c.OffsetSeconds); string copyId = Guid.NewGuid().ToString("N");
            var copies = commands.Select(c => c.Clone()).ToList();
            void Assign(ServoCommand copy) { copy.GroupId = copyId; if (copy.AudioTrim != null) foreach (var hidden in copy.AudioTrim.HiddenCommands) Assign(hidden); }
            foreach (var copy in copies) { Assign(copy); copy.OffsetSeconds = ServoCommand.TimeKey(Math.Max(0, _cursorTime + copy.OffsetSeconds - start)); }
            AudioTrimSettings.RemapCopiedTakes(copies); _doc.Commands.AddRange(copies);
            _doc.CommandGroupNames[copyId] = GroupDisplayName(id) + " copy"; _doc.CommandGroupTimelines.Add(copyId);
        });
    }

    private void RefreshNamedGroupTracks()
    {
        _namedGroupTracks.Clear(); NamedGroupTimelineRows.Children.Clear();

        RefreshGroupManager();
        foreach (string id in _doc.CommandGroupTimelines.Where(id => _doc.Commands.Any(c => c.GroupId == id)).OrderBy(id => _doc.CommandGroupNumbers[id]))
        {
            var row = new DockPanel { Height = 82, LastChildFill = true };
            var label = new DockPanel { Height = 28, Margin = new Thickness(4, 1, 4, 1) };
            var hide = new Button { Content = "▾", Width = 22, MinWidth = 0, Padding = new Thickness(0), ToolTip = "Hide this group timeline" };
            hide.Click += (_, _) => SetGroupTimeline(id, false); DockPanel.SetDock(hide, Dock.Right); label.Children.Add(hide);
            var caption = new Button { Content = GroupDisplayName(id), HorizontalContentAlignment = HorizontalAlignment.Left, FontWeight = FontWeights.SemiBold, Padding = new Thickness(2), ToolTip = "Select this group; right-click its timeline for group actions" };
            caption.Click += (_, _) => SelectNamedGroup(id, AddGroupToSelection); label.Children.Add(caption); DockPanel.SetDock(label, Dock.Top); row.Children.Add(label);
            var commands = _doc.Commands.Where(c => c.GroupId == id).ToArray();
            var track = new NamedGroupTrack(Waveform, commands, () => _cursorTime, _doc.HiddenCommandGroups.Contains(id))
            {
                SelectedGroupName = () => commands.Any(_selectedTimelineCommands.Contains) ? GroupDisplayName(id) : null,
                IsEditingGroup = () => _editingCommandGroup == id,
                IsCommandSelected = _selectedTimelineCommands.Contains,
                PreviewOffset = command => (Waveform.MarkerMoveAnchor.HasValue ? _previewDragCommands : _selectedTimelineCommands).Contains(command)
                    ? Waveform.SelectedMarkerMoveOffset : 0,
                SelectCommandList = (time, toggle) => SelectGroupCommandList(id, time, toggle),
                EditCommandList = time => { SetCursor(time); EditCommandsAtCursor(groupScope: id); },
            };
            var color = CommandGroupColor(_doc.CommandGroupNumbers[id]); track.Accent = new SolidColorBrush(Color.FromRgb(color.Red, color.Green, color.Blue));
            track.SelectGroup = toggle => SelectNamedGroup(id, toggle); track.Seek = SetCursor; track.MoveGroup = MoveSelectedCommandMarkers;
            track.GroupMenu = () =>
            {
                var menu = new ContextMenu();
                AddGroupMenuHeader(menu, GroupDisplayName(id));
                void Item(string text, Action action) { var item = new MenuItem { Header = text }; item.Click += (_, _) => action(); menu.Items.Add(item); }
                Item(_editingCommandGroup == id ? "Finish group edit" : "Edit commands within group",
                    () => SetGroupEdit(_editingCommandGroup == id ? null : id));
                Item("Group manager / rename…", ShowGroupManager); Item("Select group", () => SelectNamedGroup(id));
                Item("Duplicate at cursor", () => DuplicateNamedGroup(id)); Item("Offset group…", () => OffsetNamedGroup(id)); Item("Hide timeline", () => SetGroupTimeline(id, false)); menu.IsOpen = true;
            };
            row.Children.Add(track); NamedGroupTimelineRows.Children.Add(row); _namedGroupTracks.Add(track);
        }
        NamedGroupTimelineScroller.Visibility = _namedGroupTracks.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
