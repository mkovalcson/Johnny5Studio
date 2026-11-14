using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ServoAnimator;

public partial class MainWindow
{
    private Window _sceneSetupWindow;
    private void UrdfModel_Click(object sender, RoutedEventArgs e)
    {
        if (_sceneSetupWindow != null) { _sceneSetupWindow.Activate(); return; }
        if (!CanEditBackground()) return;
        var view = ApiUrdfView;
        if (view == null) return;
        try
        {
        var sceneDocument = _doc;
        var baseline = CaptureSceneViews();
        var priorBackdrops = _backdropTimeline.ToArray();
        var background = UrdfConfiguration.CreateDefault();
        background.Presentation = CurrentBackground().Background;
        var draft = view.CaptureCurrentFraming();
        var placementRefresh = new List<Action>();
        bool synchronizingCamera = false;
        bool savedToTimeline = false, forcedClose = false;
        string appliedSignature = null, savedSignature = null;
        var window = new Window { Title = "URDF Scene Setup", Owner = this, Width = 860, Height = 850, FontSize = 14,
            MinWidth = 680, MinHeight = 560, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        window.SetResourceReference(BackgroundProperty, "AppBackground"); window.SetResourceReference(ForegroundProperty, "PrimaryText");
        HelpSystem.EnableContextHelp(window, "urdf-framing");
        var dock = new DockPanel { Margin = new Thickness(16) }; window.Content = dock;
        dock.SetResourceReference(Panel.BackgroundProperty, "AppBackground");
        var footer = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(footer, Dock.Bottom); dock.Children.Add(footer);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(3, 0, 0, 8), Foreground = Brushes.WhiteSmoke };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        DockPanel.SetDock(header, Dock.Top); dock.Children.Add(header);
        header.Children.Add(new TextBlock { Text = "Saved scene", Margin = new Thickness(3, 0, 0, 5) });
        var sceneRow = new DockPanel(); header.Children.Add(sceneRow);
        var scenes = new ComboBox { MinWidth = 240, Margin = new Thickness(3), VerticalContentAlignment = VerticalAlignment.Center };
        var sceneInfo = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(3, 6, 0, 0) }; header.Children.Add(sceneInfo);
        footer.Children.Add(status);
        var finalButtons = new DockPanel(); footer.Children.Add(finalButtons);
        var sceneButtons = new WrapPanel();
        footer.Children.Add(new TextBlock { Text = "Apply keeps this setup after closing, without saving a scene.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(3, 8, 0, 0) });
        var tabs = new TabControl { Background = new SolidColorBrush(Color.FromRgb(24, 32, 44)),
            Foreground = Brushes.WhiteSmoke, BorderBrush = new SolidColorBrush(Color.FromRgb(69, 87, 109)) };
        tabs.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/AnimationEditorPlayer;component/SceneSetupResources.xaml", UriKind.Absolute)
        });
        var checkStyle = new Style(typeof(CheckBox));
        checkStyle.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.WhiteSmoke));
        tabs.Resources[typeof(CheckBox)] = checkStyle;
        dock.Children.Add(tabs);
        BackgroundLibraryItem activeScene = null;
        try
        {
            string savedScene = UrdfModelSettings.Load(ConfigRoot).ActiveSceneFile;
            if (!string.IsNullOrWhiteSpace(savedScene)) { activeScene = BackgroundLibrary.Read(ConfigRoot, savedScene); savedSignature = Signature(activeScene); }
        }
        catch (Exception ex) { status.Text = "Previous scene: " + ex.Message; }
        void Run(Action action) { try { action(); } catch (Exception ex) { status.Text = ex.Message; } }
        Button Button(Panel parent, string label, Action action)
        {
            var button = new Button { Content = label, Margin = new Thickness(3), Padding = new Thickness(10, 5, 10, 5) };
            SceneSetupButtonHelp.Attach(button, window);
            parent.Children.Add(button); button.Click += (_, _) => Run(action); return button;
        }
        StackPanel Tab(string title)
        {
            var panel = new StackPanel { Margin = new Thickness(14) };
            tabs.Items.Add(new TabItem { Header = title,
                Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
            return panel;
        }
        void Text(Panel panel, string text) => panel.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 8) });
        void Heading(Panel panel, string text) => panel.Children.Add(new TextBlock { Text = text, FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 8) });
        BackdropCommand CurrentDraft()
        {
            var result = new BackdropCommand { Background = background.Presentation.Clone(), Framing = view.CaptureCurrentFraming() };
            result.Background.Stage ??= new(); result.Background.Stage.RobotFraming = result.Framing.Clone();
            return result;
        }
        BackgroundLibraryItem CurrentScene() => new() { RobotModelFile = view.LoadedModelFile ?? UrdfModelSettings.DefaultModelFile, Settings = CurrentDraft() };
        string Signature(BackgroundLibraryItem item)
        {
            var settings = BackdropCommand.Read(item.Settings.ToJson());
            settings.Background.Stage ??= new(); settings.Background.Stage.RobotFraming = settings.Framing?.Clone();
            return item.RobotModelFile + "|" + settings.ToJson();
        }
        void RefreshIdentity()
        {
            sceneInfo.Text = "Editing: " + (activeScene?.Name ?? "Unsaved scene") +
                (savedSignature == Signature(CurrentScene()) ? " — saved scene matches current setup" : " — changes not saved to scene");
        }
        void ApplySetup()
        {
            var item = CurrentScene(); item.FileName = activeScene?.FileName;
            ApplyLibraryScene(item); background.Presentation = view.CaptureBackground(); Checkpoint(); BuildTabs();
            status.Text = "Applied to current setup — scene file unchanged. Meeting can use this setup.";
        }
        void Checkpoint()
        {
            baseline = CaptureSceneViews(); _backdropTimeline.Clear(); _urdfTimelineBaselines.Clear();
            priorBackdrops = _backdropTimeline.ToArray(); _sequenceModelBaselines.Clear();
            appliedSignature = Signature(CurrentScene()); RefreshIdentity();
        }
        void RefreshScenes(string select = null)
        {
            var items = BackgroundLibrary.List(ConfigRoot, out var errors, scenes: true);
            scenes.ItemsSource = items;
            RefreshIdentity();
            scenes.SelectedItem = items.FirstOrDefault(i => i.FileName == (select ?? activeScene?.FileName));
            if (errors.Length > 0) status.Text = errors;
        }
        void PreviewBackground()
        {
            var frame = view.CaptureCurrentFraming();
            ForEachHeadView(v => v.PrepareBackdrop(background.Presentation));
            ForEachHeadView(v => { v.SetTimelineBackground(background.Presentation); v.ApplyFraming(frame); });
        }
        bool SaveScene(bool asNew)
        {
            var item = CurrentScene();
            if (!asNew && activeScene != null)
            {
                item.Name = activeScene.Name; item.Category = activeScene.Category; item.Description = activeScene.Description; item.FileName = activeScene.FileName;
                BackgroundLibrary.Save(ConfigRoot, item, scenes: true);
            }
            else
            {
                var save = new BackgroundLibraryWindow(ConfigRoot, item.Settings, scenes: true, robotModel: item.RobotModelFile, saveOnly: true) { Owner = window };
                if (save.ShowDialog() != true) return false;
                item = save.Selected;
            }
            ApplyLibraryScene(item); activeScene = item; background.Presentation = view.CaptureBackground();
            savedSignature = Signature(item); Checkpoint(); RefreshScenes(item.FileName); BuildTabs();
            status.Text = $"Scene saved: {item.Name}. Robot, background, placement and camera are included.";
            return true;
        }
        void BuildTabs()
        {
            int selectedTab = Math.Max(0, tabs.SelectedIndex); tabs.Items.Clear();
            draft = view.CaptureCurrentFraming();
            var robot = Tab("Robot");
            Heading(robot, "Robot model");
            Text(robot, "Choose the robot you are animating. Scenery is selected under Background.");
            var robotPath = new TextBox { Text = ConfigPathService.Reference(ConfigRoot, view.LoadedModelFile ?? UrdfModelSettings.DefaultModelFile), IsReadOnly = true,
                ToolTip = "Path relative to Config" }; robot.Children.Add(robotPath);
            var robotButtons = new WrapPanel(); robot.Children.Add(robotButtons);
            Button(robotButtons, "Change Robot Model…", () =>
            {
                var pick = new Microsoft.Win32.OpenFileDialog { Filter = "Robot URDF model|*.urdf", Title = "Choose the robot model", FileName = ConfigPathService.Resolve(ConfigRoot, robotPath.Text),
                    InitialDirectory = Directory.Exists(ModelStorage.Folder(ConfigRoot, false)) ? ModelStorage.Folder(ConfigRoot, false) : ConfigRoot };
                if (pick.ShowDialog(window) != true) return;
                var item = CurrentScene(); item.RobotModelFile = pick.FileName; item.FileName = activeScene?.FileName;
                var models = UrdfModelSettings.Load(ConfigRoot);
                item.Settings.Framing = models.Framing.TryGetValue(pick.FileName, out var frame) ? frame : view.CaptureCurrentFraming();
                ApplyLibraryScene(item); background.Presentation = view.CaptureBackground(); Checkpoint(); BuildTabs();
                status.Text = "Robot loaded. Save Scene to store the complete combination.";
            });
            Text(robot, "Keep the model's mesh folders alongside its URDF file.");

            var backdrop = Tab("Background");
            Heading(backdrop, "Background");
            Text(backdrop, "Choose what appears behind the robot.");
            var controls = new StackPanel(); backdrop.Children.Add(controls);
            bool was3D = background.Presentation.BackgroundMode == UrdfBackgroundMode.Urdf3D;
            UrdfBackgroundControls.Build(controls, window, background, ConfigRoot, () => Run(() =>
            {
                PreviewBackground();
                bool now3D = background.Presentation.BackgroundMode == UrdfBackgroundMode.Urdf3D;
                if (now3D != was3D) { was3D = now3D; BuildPlacement(); }
            }), () => view.CaptureCurrentFraming(), sceneSetup: true);
            Button(backdrop, "Props / Prop Library…", () =>
            {
                var presentation = background.Presentation;
                var stage = presentation.BackgroundMode == UrdfBackgroundMode.Urdf3D ? presentation.Stage : null;
                var props = new UrdfPropLibraryWindow(ConfigRoot, stage) { Owner = window };
                if (props.ShowDialog() != true || stage == null) return;
                var previous = stage.Props;
                stage.Props = props.Placements;
                try { PreviewBackground(); }
                catch { stage.Props = previous; PreviewBackground(); throw; }
                RefreshIdentity();
                status.Text = $"Previewing {stage.Props.Count} prop(s). Apply or save the scene to keep this setup.";
            });
            var presets = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            backdrop.Children.Add(new Expander { Header = "Reusable backgrounds", Content = presets, Margin = new Thickness(0, 16, 0, 0), Foreground = Brushes.WhiteSmoke });
            Text(presets, "Background presets contain scenery only. Loading one keeps the robot model and its framing.");
            var backgroundRow = new DockPanel(); presets.Children.Add(backgroundRow);
            var backgrounds = new ComboBox { MinWidth = 230 };
            void RefreshBackgrounds(string file = null)
            {
                backgrounds.ItemsSource = BackgroundLibrary.List(ConfigRoot, out var errors);
                backgrounds.SelectedItem = backgrounds.Items.Cast<BackgroundLibraryItem>().FirstOrDefault(i => i.FileName == file) ?? backgrounds.Items.Cast<BackgroundLibraryItem>().FirstOrDefault();
                if (errors.Length > 0) status.Text = errors;
            }
            Button(backgroundRow, "Load Background", () =>
            {
                if (backgrounds.SelectedItem is not BackgroundLibraryItem chosen) throw new InvalidOperationException("Select a saved background first.");
                ApplyLibraryBackground(BackgroundLibrary.Read(ConfigRoot, chosen.FileName));
                background.Presentation = view.CaptureBackground(); Checkpoint(); BuildTabs(); status.Text = "Background loaded; robot model and framing preserved.";
            });
            backgroundRow.Children.Add(backgrounds); RefreshBackgrounds();
            var backgroundButtons = new WrapPanel(); presets.Children.Add(backgroundButtons);
            Button(backgroundButtons, "Save Background Only…", () =>
            {
                var save = new BackgroundLibraryWindow(ConfigRoot, CurrentDraft(), saveOnly: true) { Owner = window };
                if (save.ShowDialog() == true) { RefreshBackgrounds(save.Selected.FileName); status.Text = "Background saved. Use Save Scene to save the complete setup."; }
            });
            Button(backgroundButtons, "Manage Backgrounds…", () =>
            {
                var manager = new BackgroundLibraryWindow(ConfigRoot, CurrentDraft()) { Owner = window };
                if (manager.ShowDialog() == true && manager.Selected != null)
                { ApplyLibraryBackground(manager.Selected); background.Presentation = view.CaptureBackground(); Checkpoint(); BuildTabs(); }
                else RefreshBackgrounds();
            });
            BuildPlacement(); tabs.SelectedIndex = Math.Min(selectedTab, tabs.Items.Count - 1);
        }
        void BuildPlacement()
        {
            placementRefresh.Clear();
            draft = view.CaptureCurrentFraming();
            int selected = Math.Max(0, tabs.SelectedIndex);
            while (tabs.Items.Count > 2) tabs.Items.RemoveAt(2);
            var placement = Tab("Robot Position");
            var camera = Tab("Camera");
            var stage = background.Presentation.Stage ??= new();
            bool is3D = background.Presentation.BackgroundMode == UrdfBackgroundMode.Urdf3D;
            double LiveValue(string name, double fallback) => name switch
            {
                "Spin (cumulative °)" or "Spin fine adjustment" => draft.SpinDegrees,
                "Roll / tilt sideways (°)" => draft.RotateDegrees,
                "Horizontal position (%)" => draft.HorizontalPercent,
                "Vertical position (%)" => draft.VerticalPercent,
                "Robot framing zoom (%)" => 115 / draft.Distance,
                "Orbit left / right (°)" => draft.Yaw * 180 / Math.PI,
                "Orbit up / down (°)" => draft.Pitch * 180 / Math.PI,
                _ => fallback
            };
            Slider Number(Panel parent, string label, double value, Action<double> change, double? min = null, double? max = null)
            {
                var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) }; parent.Children.Add(row);
                row.Children.Add(new TextBlock { Text = label, Width = 225, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
                var box = new TextBox { Text = value.ToString("0.###"), Width = 95 };
                DockPanel.SetDock(box, Dock.Right); row.Children.Add(box);
                Slider numericSlider = null;
                bool updatingNumber = false;
                if (min.HasValue && max.HasValue)
                {
                    numericSlider = new Slider { Minimum = Math.Min(min.Value, value), Maximum = Math.Max(max.Value, value),
                        Value = value, MinWidth = 100, Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
                    row.Children.Add(numericSlider);
                    numericSlider.ValueChanged += (_, _) =>
                    {
                        if (updatingNumber || synchronizingCamera) return;
                        Run(() => { change(numericSlider.Value); value = numericSlider.Value; box.Text = numericSlider.Value.ToString("0.###"); });
                    };
                }
                placementRefresh.Add(() =>
                {
                    double current = LiveValue(label, value);
                    if (!box.IsKeyboardFocusWithin) box.Text = current.ToString("0.###");
                    if (numericSlider != null)
                    {
                        updatingNumber = true;
                        try { numericSlider.Minimum = Math.Min(numericSlider.Minimum, current); numericSlider.Maximum = Math.Max(numericSlider.Maximum, current); numericSlider.Value = current; }
                        finally { updatingNumber = false; }
                    }
                });
                double last = value;
                void Commit() => Run(() =>
                {
                    if (!double.TryParse(box.Text, out double next) || !double.IsFinite(next)) { box.Text = last.ToString("0.###"); throw new InvalidDataException("Enter a finite number for " + label); }
                    if (next == LiveValue(label, value)) return;
                    try { change(next); } catch { box.Text = LiveValue(label, value).ToString("0.###"); throw; }
                    last = next; value = next;
                    if (numericSlider != null)
                    {
                        updatingNumber = true;
                        numericSlider.Minimum = Math.Min(numericSlider.Minimum, next); numericSlider.Maximum = Math.Max(numericSlider.Maximum, next);
                        numericSlider.Value = next; updatingNumber = false;
                        if (label == "Spin (cumulative °)")
                        {
                            updatingNumber = true;
                            try { numericSlider.Minimum = next - 360; numericSlider.Maximum = next + 360; }
                            finally { updatingNumber = false; }
                        }
                    }
                });
                box.LostKeyboardFocus += (_, _) => Commit(); box.KeyDown += (_, args) => { if (args.Key == System.Windows.Input.Key.Enter) { Commit(); args.Handled = true; } };
                return numericSlider;
            }
            Slider Slide(string name, double initial, double min, double max, Action<double> change, string suffix, Panel parent = null)
            {
                return Number(parent ?? placement, name, initial, n =>
                {
                    if (name != "Spin (cumulative °)" && (n < min || n > max))
                        throw new InvalidDataException($"{name} must be between {min:0.###} and {max:0.###}.");
                    change(n); ForEachHeadView(v => v.ApplyFraming(draft));
                }, min, max);
            }
            Heading(placement, "Robot position");
            Text(placement, "Place and orient the robot. Camera framing has its own tab.");
            Slide("Spin (cumulative °)", draft.SpinDegrees, draft.SpinDegrees - 360, draft.SpinDegrees + 360, n => draft.SpinDegrees = n, "°");
            Text(placement, "Spin supports multiple turns. Enter 720° for two full turns.");
            Slide("Roll / tilt sideways (°)", draft.RotateDegrees, -360, 360, n => draft.RotateDegrees = n, "°");
            if (is3D)
            {
                Number(placement, "Room X (m)", stage.RobotX, n => { stage.RobotX = n; PreviewBackground(); }, -10, 10);
                Number(placement, "Room Y (m)", stage.RobotY, n => { stage.RobotY = n; PreviewBackground(); }, -10, 10);
                Number(placement, "Heading in room (°)", stage.HeadingDegrees, n => { stage.HeadingDegrees = n; PreviewBackground(); }, -360, 360);
                var movement = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
                placement.Children.Add(new Expander { Header = "Movement and floor alignment", Content = movement, Foreground = Brushes.WhiteSmoke, Margin = new Thickness(0, 16, 0, 0) });
                Text(movement, "Movement behavior");
                var mode = new ComboBox { ItemsSource = new[] { "Keep robot framed", "Move robot through room" }, SelectedIndex = (int)stage.MovementMode }; movement.Children.Add(mode);
                Text(movement, "Keep robot framed moves scenery around the robot. Move robot through room uses perspective and follows near the edges.");
                mode.SelectionChanged += (_, _) => Run(() => { stage.MovementMode = (UrdfStageMode)mode.SelectedIndex; PreviewBackground(); });
                var preserve = new CheckBox { Content = "Preserve robot framing within the stage", IsChecked = stage.PreserveRobotFraming, Margin = new Thickness(0, 8, 0, 4) }; movement.Children.Add(preserve);
                preserve.Click += (_, _) => Run(() => { stage.PreserveRobotFraming = preserve.IsChecked == true; PreviewBackground(); });
                var floor = new CheckBox { Content = "Detect floor automatically", IsChecked = stage.DetectFloor }; movement.Children.Add(floor);
                floor.Click += (_, _) => Run(() => { stage.DetectFloor = floor.IsChecked == true; PreviewBackground(); });
                var manualFloor = new StackPanel { Visibility = stage.DetectFloor ? Visibility.Collapsed : Visibility.Visible }; movement.Children.Add(manualFloor);
                Number(manualFloor, "Floor height (m)", stage.FloorHeight, n => { stage.FloorHeight = n; PreviewBackground(); });
                floor.Click += (_, _) => manualFloor.Visibility = floor.IsChecked == true ? Visibility.Collapsed : Visibility.Visible;
            }
            else Text(placement, "Select a 3D URDF background to set room position, movement mode and floor alignment.");
            Heading(camera, "Camera");
            Text(camera, "Frame the shot here, or drag and zoom in the editor’s 3D view.");
            Slide("Horizontal position (%)", draft.HorizontalPercent, -200, 200, n => draft.HorizontalPercent = n, "%", camera);
            Slide("Vertical position (%)", draft.VerticalPercent, -200, 200, n => draft.VerticalPercent = n, "%", camera);
            Slide("Robot framing zoom (%)", 115 / draft.Distance, 10, 115 / .55, n => draft.Distance = 115 / n, "%", camera);
            Slide("Orbit left / right (°)", draft.Yaw * 180 / Math.PI, -180, 180, n => draft.Yaw = n * Math.PI / 180, "°", camera);
            Slide("Orbit up / down (°)", draft.Pitch * 180 / Math.PI, -75, 75, n => draft.Pitch = n * Math.PI / 180, "°", camera);
            if (is3D)
            {
                var backgroundCamera = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
                camera.Children.Add(new Expander { Header = "Background view adjustments", Content = backgroundCamera, Foreground = Brushes.WhiteSmoke, Margin = new Thickness(0, 16, 0, 0) });
                Text(backgroundCamera, "Keep robot framed (Robot Centric) keeps the robot's on-screen size while the backdrop moves and zooms. Room Centric uses perspective and follows the robot near the edges.");
                Number(backgroundCamera, "Background pan X (metres)", stage.ViewPanX, n => { stage.ViewPanX = n; PreviewBackground(); }, -20, 20);
                Number(backgroundCamera, "Background pan Y (metres)", stage.ViewPanY, n => { stage.ViewPanY = n; PreviewBackground(); }, -20, 20);
                Number(backgroundCamera, "Background pan Z (metres)", stage.ViewPanZ, n => { stage.ViewPanZ = n; PreviewBackground(); }, -20, 20);
                Number(backgroundCamera, "Background zoom (%) — 10 to 1000", stage.ViewZoomPercent, n => { if (n < 10 || n > 1000) throw new InvalidDataException("Background zoom must be 10–1000%."); stage.ViewZoomPercent = n; PreviewBackground(); }, 10, 1000);
                Number(backgroundCamera, "Background camera yaw (degrees)", stage.ViewYawDegrees, n => { stage.ViewYawDegrees = n; PreviewBackground(); });
                Number(backgroundCamera, "Follow edge margin (%) — 5 to 45", stage.EdgeMarginPercent, n => { if (n < 5 || n > 45) throw new InvalidDataException("Follow edge margin must be 5–45%."); stage.EdgeMarginPercent = n; PreviewBackground(); });
            }
            var viewOptions = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            camera.Children.Add(new Expander { Header = "View options", Content = viewOptions, Foreground = Brushes.WhiteSmoke, Margin = new Thickness(0, 16, 0, 0) });
            var clean = new CheckBox { Content = "Clean view when undocked", IsChecked = background.Presentation.CleanUndockedView }; viewOptions.Children.Add(clean);
            Text(viewOptions, "Hides controls, FPS and legends, and suppresses collision warnings until docked. Camera gestures remain available.");
            clean.Click += (_, _) => Run(() => { background.Presentation.CleanUndockedView = clean.IsChecked == true; PreviewBackground(); });
            var animation = Tab("Animation");
            Heading(animation, "Use in animation");
            Text(animation, "Save a starting scene for the current sequence, or record movement at the timeline cursor.");
            Heading(animation, "Starting scene for this sequence");
            Text(animation, "Current sequence: " + (string.IsNullOrWhiteSpace(_jsonPath) ? CurrentMovieDraft?.Name ?? "Unsaved sequence" : Path.GetFileNameWithoutExtension(_jsonPath)));
            Button(animation, "Save Scene and Assign to Sequence", () =>
            {
                // Save the current edited setup so the assignment matches what is visible.
                if (!SaveScene(false)) return; AssignSequenceScene(activeScene);
                status.Text = $"Assigned {activeScene.Name}. Save the sequence to keep the assignment.";
            });
            Heading(animation, "Record placement and camera");
            var splinePosition = new CheckBox { Content = "Smooth movement between recorded positions", IsChecked = true,
                Margin = new Thickness(3, 6, 3, 6), ToolTip = "Save positions at two cursor times to interpolate between them. Discrete choices such as background type and movement mode use Load Scene." };
            animation.Children.Add(splinePosition);
            var recordPosition = Button(animation, "Record Position", () =>
            {
                if (SaveUrdfPositionAtCursor(view.CaptureCurrentFraming(), view.CaptureStage(), splinePosition.IsChecked == true))
                {
                    savedToTimeline = true;
                    baseline = baseline.Select(s => s with { Frame = s.View.CaptureCurrentFraming() }).ToList();
                    status.Text = $"Placement and camera recorded at {_cursorTime:F3} s. Save the sequence to retain the commands.";
                }
            });
            placementRefresh.Add(() => recordPosition.Content = $"Record Position at {_cursorTime:F3} s");
            Text(animation, "Writes numeric placement and camera commands at the cursor. Recording again here updates them. Save the sequence afterward.");
            foreach (var refresh in placementRefresh) refresh();
            tabs.SelectedIndex = Math.Min(selected, tabs.Items.Count - 1);
        }
        var loadScene = Button(sceneRow, "Load Scene…", () =>
        {
            if (scenes.SelectedItem is not BackgroundLibraryItem selected) throw new InvalidOperationException("Select a saved scene first.");
            var item = SavedSceneCommand.Read(ConfigRoot, selected.FileName); ApplyLibraryScene(item); activeScene = item;
            background.Presentation = view.CaptureBackground(); savedSignature = Signature(item); Checkpoint(); RefreshScenes(item.FileName); BuildTabs(); status.Text = $"Loaded scene: {item.Name}.";
        });
        DockPanel.SetDock(loadScene, Dock.Right); sceneRow.Children.Add(scenes);
        var close = Button(finalButtons, "Close", () => window.Close()); DockPanel.SetDock(close, Dock.Right);
        finalButtons.Children.Add(sceneButtons);
        var apply = Button(sceneButtons, "Apply", ApplySetup); apply.FontWeight = FontWeights.SemiBold;
        apply.SetResourceReference(Control.BackgroundProperty, "SequenceAccentSurface");
        apply.SetResourceReference(Control.BorderBrushProperty, "SequenceAccent");
        Button(sceneButtons, "Save Scene", () => SaveScene(false)); Button(sceneButtons, "Save Scene As…", () => SaveScene(true));
        appliedSignature = Signature(CurrentScene()); RefreshScenes(); BuildTabs();
        if (string.IsNullOrWhiteSpace(status.Text)) status.Text = "Changes preview immediately. Apply keeps the setup; Save Scene stores a reusable scene.";
        var observedViews = new List<RobotHeadView>();
        void CameraChanged(RobotHeadView changed)
        {
            if (synchronizingCamera) return;
            synchronizingCamera = true;
            try
            {
                draft = changed.CaptureCurrentFraming();
                ForEachHeadView(v => { if (!ReferenceEquals(v, changed)) v.ApplyFraming(draft); });
                foreach (var refresh in placementRefresh) refresh();
            }
            finally { synchronizingCamera = false; }
        }
        void ObserveViews()
        {
            ForEachHeadView(v =>
            {
                if (observedViews.Contains(v)) return;
                observedViews.Add(v); v.CameraInteracted += CameraChanged;
            });
        }
        ObserveViews();
        // Docking can create another view while this modeless setup remains open.
        var observer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        observer.Tick += (_, _) =>
        {
            if (!ReferenceEquals(sceneDocument, _doc) || IsRunning || RecordingController || _meetingActive)
            { forcedClose = true; window.Close(); return; }
            ObserveViews(); RefreshIdentity();
            synchronizingCamera = true;
            try { foreach (var refresh in placementRefresh) refresh(); }
            finally { synchronizingCamera = false; }
        };
        observer.Start();
        window.Closing += (_, args) =>
        {
            System.Windows.Input.Keyboard.ClearFocus();
            if (forcedClose || appliedSignature == Signature(CurrentScene())) return;
            var choice = new ScenePreviewCloseWindow { Owner = window };
            if (choice.ShowDialog() != true) { args.Cancel = true; return; }
            if (choice.ApplyChanges)
                try { ApplySetup(); } catch (Exception ex) { status.Text = ex.Message; args.Cancel = true; }
        };
        window.Closed += (_, _) =>
        {
            observer.Stop();
            _editingUrdfFraming = false; _sceneSetupWindow = null;
            foreach (var observed in observedViews) observed.CameraInteracted -= CameraChanged;
            try
            {
                if (!ReferenceEquals(sceneDocument, _doc)) return;
                RestoreSceneViews(baseline); _backdropTimeline.Clear();
                foreach (var entry in priorBackdrops) _backdropTimeline[entry.Key] = entry.Value;
                if (savedToTimeline && !IsRunning) UpdateServoState(_cursorTime);
            }
            catch (Exception ex) { ShowStatus("Restore scene preview: " + ex.Message); }
        };
        _editingUrdfFraming = true;
        _sceneSetupWindow = window;
        window.Show();
        }
        catch (Exception ex) { ShowStatus("URDF Scene Setup: " + ex.Message); }
    }
}
