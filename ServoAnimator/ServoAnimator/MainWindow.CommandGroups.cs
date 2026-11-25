using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ServoAnimator
{
    public partial class MainWindow
    {
        private AnimationDocument _markerSelectionDocument;
        private IEnumerable<ServoCommand> VisibleCommands => _doc.Commands.Where(c => !_doc.HiddenCommandGroups.Contains(c.GroupId));
        private void EnsureCommandGroupNumbers()
        {
            _doc.CommandGroupNumbers ??= new();
            _doc.CommandGroupNames ??= new();
            _doc.CommandGroupTimelines ??= new();
            int next = _doc.CommandGroupNumbers.Values.DefaultIfEmpty(0).Max() + 1;
            foreach (string id in _doc.Commands.Select(c => c.GroupId).Where(id => !string.IsNullOrEmpty(id)).Distinct())
                if (!_doc.CommandGroupNumbers.ContainsKey(id)) _doc.CommandGroupNumbers[id] = next++;
        }
        internal static SkiaSharp.SKColor CommandGroupColor(int number)
            => SkiaSharp.SKColor.FromHsv((float)((157 + (number - 1) * 137.507764) % 360), 78, 84);
        private void RefreshHiddenGroupButtons()
        {
            HiddenGroupButtons.Children.Clear();
            foreach (var id in _doc.HiddenCommandGroups.Where(id => _doc.Commands.Any(c => c.GroupId == id))
                .OrderBy(id => _doc.CommandGroupNumbers[id]))
            {
                int number = _doc.CommandGroupNumbers[id];
                var color = CommandGroupColor(number);
                var button = new Button { Content = $"Show {GroupDisplayName(id)}", Margin = new Thickness(5, 0, 0, 0),
                    Padding = new Thickness(9, 5, 9, 5), Background = new SolidColorBrush(Color.FromRgb(color.Red, color.Green, color.Blue)),
                    Foreground = Brushes.Black };
                button.Click += (_, _) => { _doc.HiddenCommandGroups.Remove(id); RefreshGroupVisibility(); };
                HiddenGroupButtons.Children.Add(button);
            }
        }
        private void RefreshGroupVisibility()
        {
            Waveform.SetMarkerSelection(Array.Empty<double>());
            RebuildSplineData(); RefreshMarkers(); UpdateCommandsAtPointList();
            Waveform.AudioClips = _playbackAudioClips.Where(c => !_doc.HiddenCommandGroups.Contains(c.Command.GroupId)).ToList();
            Waveform.VideoClips = _videoClipVisuals.Where(c => !_doc.HiddenCommandGroups.Contains(c.Command.GroupId)).ToList();
            Waveform.InvalidateVisual();
        }
        private bool _showModifiedControls;
        private bool _updatingModifiedControls;

        private void InitializeCommandGroups()
        {
            InitializeWorkspacePanels();
            Waveform.SelectedGroupNameAtMarker = SelectedGroupNameAtMarker;
            Waveform.MarkerSelectionRequested += SelectCommandMarkers;
            Waveform.IsGroupedMarker = (time, lane) => VisibleCommands.Any(c => !string.IsNullOrEmpty(c.GroupId) &&
                ServoCommand.TimeKey(c.OffsetSeconds) == time && (lane < 0 || TimelineLanes.For(c.Servo) == lane));
            Waveform.SelectedMarkersMoved += MoveSelectedCommandMarkers;
            Waveform.DragMarkersFrom = BuildDragPreview;
            Waveform.MarkerSelectionChanged += () =>
            {
                if (!_showModifiedControls) return;
                if (Waveform.SelectedMarkers.Count == 0)
                {
                    _showModifiedControls = false;
                    Waveform.HighlightedControlMarkers.Clear();
                    Waveform.InvalidateVisual();
                }
                UpdateCommandsAtPointList();
            };
            PreviewMouseDown += (_, e) =>
            {
                if (!IsWaveformMouseSource(e.OriginalSource as DependencyObject))
                    Waveform.SetMarkerSelection(Array.Empty<double>());
            };
        }

        private bool IsWaveformMouseSource(DependencyObject source)
        {
            while (source != null)
            {
                if (source is NamedGroupTrack || ReferenceEquals(source, NamedGroupTimelineRows) || ReferenceEquals(source, MainMenuPanel)) return true;
                if (ReferenceEquals(source, Waveform)) return true;
                // Inspecting this temporary list must not end the selection it describes.
                if (_showModifiedControls && ReferenceEquals(source, CommandsAtPointList)) return true;
                if (Spline.CombinedMode && ReferenceEquals(source, AudioPlotHost)) return true;
                if (source is ContextMenu menu && ReferenceEquals(menu.PlacementTarget, Waveform)) return true;
                source = source is Visual || source is System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(source) ?? LogicalTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
            }
            return false;
        }

        private void SyncCommandGroupSelection()
        {
            EnsureCommandGroupNumbers();
            RefreshNamedGroupTracks();
            Waveform.GroupColors = VisibleCommands.Where(c => !string.IsNullOrEmpty(c.GroupId))
                .GroupBy(c => ServoCommand.TimeKey(c.OffsetSeconds)).ToDictionary(g => g.Key,
                    g => CommandGroupColor(_doc.CommandGroupNumbers[g.First().GroupId]));
            Waveform.LaneGroupColors = VisibleCommands.Where(c => !string.IsNullOrEmpty(c.GroupId))
                .GroupBy(c => (ServoCommand.TimeKey(c.OffsetSeconds), TimelineLanes.For(c.Servo)))
                .ToDictionary(g => g.Key, g => CommandGroupColor(_doc.CommandGroupNumbers[g.First().GroupId]));
            RefreshHiddenGroupButtons();
            if (!ReferenceEquals(_markerSelectionDocument, _doc))
            {
                _selectedTimelineCommands.Clear();
                _editingCommandGroup = null;
            }
            if (!string.IsNullOrEmpty(_editingCommandGroup) && !VisibleCommands.Any(c => c.GroupId == _editingCommandGroup))
                _editingCommandGroup = null;
            UpdateGroupEditIndicator();
            _markerSelectionDocument = _doc;
            _selectedTimelineCommands.IntersectWith(VisibleCommands);
            RefreshSelectedCommandMarkers();
        }

        private readonly HashSet<ServoCommand> _selectedTimelineCommands = new();

        private void RefreshSelectedCommandMarkers()
        {
            Waveform.ShowMarkerSelection(_selectedTimelineCommands.Select(c =>
                (ServoCommand.TimeKey(c.OffsetSeconds), TimelineLanes.For(c.Servo))));
            foreach (var strip in _namedGroupTracks) strip.InvalidateVisual();
        }

        private void SelectCommandMarkers(IEnumerable<double> times, int lane, bool toggle, bool replace)
        {
            var keys = times.Select(ServoCommand.TimeKey).ToHashSet();
            if (replace) _selectedTimelineCommands.Clear();
            if (keys.Count == 0)
            {
                RefreshSelectedCommandMarkers();
                return;
            }
            var hits = VisibleCommands.Where(c => keys.Contains(ServoCommand.TimeKey(c.OffsetSeconds)) &&
                (lane < 0 || TimelineLanes.For(c.Servo) == lane)).ToHashSet();
            if (_editingCommandGroup != null)
            {
                var groupTimes = hits.Where(c => c.GroupId == _editingCommandGroup)
                    .Select(c => ServoCommand.TimeKey(c.OffsetSeconds)).ToHashSet();
                hits.RemoveWhere(c => c.GroupId != _editingCommandGroup && groupTimes.Contains(ServoCommand.TimeKey(c.OffsetSeconds)));
                hits.UnionWith(VisibleCommands.Where(c => c.GroupId == _editingCommandGroup &&
                    groupTimes.Contains(ServoCommand.TimeKey(c.OffsetSeconds))));
            }
            var ids = hits.Select(c => c.GroupId)
                .Where(id => !string.IsNullOrEmpty(id) && id != _editingCommandGroup).ToHashSet();
            var selection = VisibleCommands.Where(c => hits.Contains(c) || ids.Contains(c.GroupId)).ToArray();
            if (toggle && selection.All(_selectedTimelineCommands.Contains)) _selectedTimelineCommands.ExceptWith(selection);
            else _selectedTimelineCommands.UnionWith(selection);
            RefreshSelectedCommandMarkers();
        }

        private List<ServoCommand> SelectedGroupCommands() => VisibleCommands.Where(_selectedTimelineCommands.Contains).ToList();

        private void ShowSelectedCommandMenu()
        {
            var menu = new ContextMenu { PlacementTarget = Waveform };
            void Item(string label, Action action)
            {
                var item = new MenuItem { Header = label };
                item.Click += (_, _) => action();
                menu.Items.Add(item);
            }
            var groupNames = GroupDisplayNames(SelectedGroupCommands());
            bool grouped = groupNames.Length > 0;
            if (grouped) AddGroupMenuHeader(menu, groupNames);
            if (_editingCommandGroup != null) Item("Finish group edit", () => SetGroupEdit(null));
            else
            {
                var groupIds = SelectedGroupCommands().Select(c => c.GroupId).Where(id => !string.IsNullOrEmpty(id)).Distinct().ToArray();
                if (groupIds.Length == 1) Item("Edit commands within group", () => SetGroupEdit(groupIds[0]));
            }
            Item("Group manager…", ShowGroupManager);
            Item("Uniform offset…", UniformSelectedCommands);
            Item("Proportional Offset…", ProportionalSelectedCommands);
            Item(grouped ? "Ungroup Commands" : "Group Commands", () => SetPersistentCommandGroup(!grouped));
            if (grouped) Item("Hide Group", () =>
            {
                if (!ApplyOpenCommandEditor() || SelectedGroupCommands().Count == 0) return;
                foreach (var id in SelectedGroupCommands().Select(c => c.GroupId).Where(id => !string.IsNullOrEmpty(id)))
                    _doc.HiddenCommandGroups.Add(id);
                RefreshGroupVisibility();
            });
            Item("Repeat…", RepeatSelectedCommands);
            Item("Show all modified controls", () =>
            {
                _showModifiedControls = true;
                UpdateCommandsAtPointList();
            });
            Item("Copy", () =>
            {
                _clipboard.Clear();
                _clipboard.AddRange(SelectedGroupCommands().Select(c => c.Clone()));
                ShowStatus($"Copied {_clipboard.Count} commands. Click a timeline position, then right-click and Paste.");
            });
            Item("Delete", () =>
            {
                if (!PrepareGroupEdit()) return;
                var selected = SelectedGroupCommands();
                ApplySelectedGroupEdit($"Delete {selected.Count} selected commands", () =>
                    _doc.Commands.RemoveAll(selected.Contains));
            });
            if (_clipboard.Count > 0)
            {
                menu.Items.Add(new Separator());
                Item($"Paste {_clipboard.Count} copied commands at {_cursorTime:F3} s", PasteClipboardAtCursor);
            }
            menu.IsOpen = true;
        }

        private void ModifiedDelete_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (sender is not Button { DataContext: ModifiedCommandControl control } || !PrepareGroupEdit()) return;
            var selected = SelectedGroupCommands().Where(c => c.Servo == control.Servo && c.Control == control.Control).ToHashSet();
            if (selected.Count == 0) return;
            ApplySelectedGroupEdit($"Delete {selected.Count} {control.NameText} commands from selection",
                () => _doc.Commands.RemoveAll(selected.Contains), keepSelection: true);
        }

        private void UpdateModifiedControlsList()
        {
            var selected = CommandsAtPointList.SelectedItem as ModifiedCommandControl;
            _updatingModifiedControls = true;
            try
            {
                CommandsAtPointList.Items.Clear();
                foreach (var control in ModifiedCommandControls.From(SelectedGroupCommands()))
                {
                    var commands = SelectedGroupCommands().Where(c => c.Servo == control.Servo && c.Control == control.Control &&
                        SplineBreakOperations.SupportsSpline(c) && !c.Disable && SplineServosEnabled().Contains(c.Servo)).ToArray();
                    control.CanSetBreaks = commands.Length > 0;
                    control.CanClearBreaks = commands.Any(c => c.BreakSpline);
                    CommandsAtPointList.Items.Add(control);
                }
                CommandsAtPointHeader.Text = $"Modified controls · {Waveform.SelectedMarkers.Count} selected times";
                CommandsAtPointHeader.ToolTip = $"{CommandsAtPointList.Items.Count} distinct controls in the selected command triangles";
                if (selected != null)
                    CommandsAtPointList.SelectedItem = CommandsAtPointList.Items.OfType<ModifiedCommandControl>()
                        .FirstOrDefault(c => c.Servo == selected.Servo && c.Control == selected.Control);
            }
            finally { _updatingModifiedControls = false; }
            CommandsAtPointList.ToolTip = "Select a control to highlight every command triangle that includes it in purple.";
            HighlightModifiedControl();
        }

        private void CommandsAtPointList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_showModifiedControls && !_updatingModifiedControls) HighlightModifiedControl();
        }

        private void HighlightModifiedControl()
        {
            Waveform.HighlightedControlMarkers.Clear();
            if (CommandsAtPointList.SelectedItem is ModifiedCommandControl control)
                Waveform.HighlightedControlMarkers.UnionWith(_doc.Commands.Where(control.Includes)
                    .Select(c => ServoCommand.TimeKey(c.OffsetSeconds)));
            Waveform.InvalidateVisual();
        }

        private bool PrepareGroupEdit()
        {
            if (!ApplyOpenCommandEditor()) return false;
            if (IsRunning) PausePlayback();
            return Waveform.SelectedMarkers.Count > 0 && SelectedGroupCommands().Count > 0;
        }

        private void SetPersistentCommandGroup(bool group)
        {
            if (!PrepareGroupEdit()) return;
            var selected = SelectedGroupCommands();
            var ids = selected.Where(c => !string.IsNullOrEmpty(c.GroupId)).Select(c => c.GroupId).ToHashSet();
            string id = group ? Guid.NewGuid().ToString("N") : "";
            ApplySelectedGroupEdit(group ? "Group Commands" : "Ungroup Commands", () =>
            {
                foreach (var c in _doc.Commands.Where(c => selected.Contains(c) || ids.Contains(c.GroupId))) c.GroupId = id;
                if (group) { _doc.CommandGroupNames[id] = "New group"; _doc.CommandGroupTimelines.Add(id); }
            });
        }

        private void DeleteSelectedCommandGroup()
        {
            if (!PrepareGroupEdit()) return;
            var selected = SelectedGroupCommands();
            ApplySelectedGroupEdit($"Delete {selected.Count} selected commands", () => _doc.Commands.RemoveAll(selected.Contains));
        }

        private void ApplySelectedGroupEdit(string description, Action edit, bool keepSelection = false)
        {
            var previous = _selectedTimelineCommands.Select(SelectionKey).ToHashSet();
            PushUndo(description);
            edit();
            bool accepted = RefreshAfterEdit();
            if (!accepted)
                _selectedTimelineCommands.UnionWith(VisibleCommands.Where(c => previous.Contains(SelectionKey(c))));
            else if (!keepSelection)
                _selectedTimelineCommands.Clear();
            RefreshSelectedCommandMarkers();
            if (accepted) ShowStatus(description);
        }

        private static (ServoNames, RobotControls?, double, string) SelectionKey(ServoCommand c) =>
            (c.Servo, c.Control, ServoCommand.TimeKey(c.OffsetSeconds), c.GroupId);

        private void MoveSelectedCommandMarkers(double offset, double? anchor)
        {
            if (!PrepareGroupEdit()) return;
            var selected = TimelineDragTargets(anchor);
            if (selected.Count == 0) return;
            offset = ServoCommand.TimeKey(Math.Max(-selected.Min(c => c.OffsetSeconds), offset));
            if (offset == 0) return;

            ApplySelectedGroupEdit($"Move {selected.Count} selected commands by {offset:0.###} s", () =>
            {
                _doc.Commands.RemoveAll(selected.Contains);
                foreach (var command in selected) command.OffsetSeconds = ServoCommand.TimeKey(command.OffsetSeconds + offset);
                _doc.Commands.AddRange(selected); // Conflicts list existing values before the newly moved values.
            }, keepSelection: true);
        }

        private void UniformSelectedCommands()
        {
            if (!PrepareGroupEdit()) return;
            var times = Waveform.SelectedMarkers.ToArray();
            var dialog = new CommandGroupDialog(this, repeat: false, CommandGroupOperations.MeanSpacing(times));
            if (dialog.ShowDialog() != true) return;
            var map = CommandGroupOperations.UniformTimes(times, dialog.Offset);
            if (map.Values.Any(t => !double.IsFinite(t)))
            {
                MessageBox.Show(this, "The resulting command time is too large.", "Uniform offset");
                return;
            }
            var selected = SelectedGroupCommands();
            ApplySelectedGroupEdit($"Uniform offset: {dialog.Offset:0.######} s", () =>
            {
                _doc.Commands.RemoveAll(selected.Contains);
                foreach (var command in selected) command.OffsetSeconds = map[ServoCommand.TimeKey(command.OffsetSeconds)];
                _doc.Commands.AddRange(selected);
            }, keepSelection: true);
        }

        private void ProportionalSelectedCommands()
        {
            if (!PrepareGroupEdit()) return;
            var selected = SelectedGroupCommands();
            var times = selected.Select(c => ServoCommand.TimeKey(c.OffsetSeconds)).Distinct().ToArray();
            if (times.Length < 2)
            {
                MessageBox.Show(this, "Select commands at two or more different times to change their spacing.", "Proportional Offset");
                return;
            }
            var dialog = new CommandGroupDialog(this, repeat: false, mean: 0, proportional: true);
            if (dialog.ShowDialog() != true) return;
            Dictionary<double, double> map;
            try { map = CommandGroupOperations.ProportionalTimes(times, dialog.Multiplier); }
            catch (ArgumentException error)
            {
                MessageBox.Show(this, error.Message, "Proportional Offset", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (map.All(pair => pair.Key == pair.Value)) return;
            ApplySelectedGroupEdit($"Proportional offset: {dialog.Multiplier:0.######}×", () =>
            {
                _doc.Commands.RemoveAll(selected.Contains);
                foreach (var command in selected) command.OffsetSeconds = map[ServoCommand.TimeKey(command.OffsetSeconds)];
                _doc.Commands.AddRange(selected);
            }, keepSelection: true);
        }

        private void RepeatSelectedCommands()
        {
            if (!PrepareGroupEdit()) return;
            var dialog = new CommandGroupDialog(this, repeat: true, 0);
            if (dialog.ShowDialog() != true) return;
            try
            {
                var copies = CommandGroupOperations.Repeat(SelectedGroupCommands(), dialog.Repetitions, dialog.Offset);
                ApplySelectedGroupEdit($"Repeat selected commands {dialog.Repetitions} times", () => _doc.Commands.AddRange(copies), keepSelection: true);
            }
            catch (ArgumentException error) { MessageBox.Show(this, error.Message, "Repeat commands", MessageBoxButton.OK, MessageBoxImage.Information); }
        }
    }
}
