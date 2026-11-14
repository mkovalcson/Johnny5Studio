using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ServoAnimator;

internal sealed partial class MeetingWindow
{
    private RadioButton _useCurrent, _useSaved;
    private ComboBox _scenes, _controller, _microphone;
    private CheckBox _face;
    private StackPanel _scenePicker;
    private Button _start;
    private ProgressBar _level;
    private TextBlock _sceneDetails, _previewNotice, _sceneName, _robotName, _backgroundName, _cameraName, _currentHint;
    private TextBlock _sceneState, _cameraState, _controllerState, _microphoneState, _movementMessage, _meterText, _message, _readySummary;
    private readonly List<(Slider Slider, TextBox Number, string Title)> _numbers = new();

    private static TextBlock Text(string text, double size = 14, bool secondary = false)
    {
        var block = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 3) };
        block.SetResourceReference(TextBlock.ForegroundProperty, secondary ? "SecondaryText" : "PrimaryText"); return block;
    }
    private static Border Card(UIElement child) 
    {
        var card = new Border { Child = child, CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1), Padding = new Thickness(13), Margin = new Thickness(0, 0, 0, 10) };
        card.SetResourceReference(Border.BackgroundProperty, "PanelBackground"); card.SetResourceReference(Border.BorderBrushProperty, "DividerBrush"); return card;
    }
    private static StackPanel Section(Panel parent, string title)
    {
        var section = new StackPanel(); var heading = Text(title, 17); heading.FontWeight = FontWeights.SemiBold; heading.Margin = new Thickness(0, 0, 0, 10);
        section.Children.Add(heading); parent.Children.Add(Card(section)); return section;
    }
    private void BuildLayout()
    {
        var root = new DockPanel { Margin = new Thickness(18) }; Content = root;
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 14) }; DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var title = Text("Get ready for your meeting", 23); title.FontWeight = FontWeights.SemiBold; header.Children.Add(title);
        header.Children.Add(Text("Choose a scene, test your inputs, then start.", 14, true));
        var footer = new Grid { Margin = new Thickness(0, 10, 0, 0) }; DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var notes = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) }; footer.Children.Add(notes);
        notes.Children.Add(Text("Starting also saves these settings.", 13, true)); _message = Text("", 13); _message.Name = "MeetingMessage"; notes.Children.Add(_message);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(buttons, 1); footer.Children.Add(buttons);
        Button Action(string caption)
        { var b = new Button { Content = caption, MinWidth = 88, Padding = new Thickness(13, 8, 13, 8), Margin = new Thickness(7, 0, 0, 0) }; buttons.Children.Add(b); return b; }
        Action("Cancel").IsCancel = true;
        Action("Save settings").Click += SaveClicked;
        _start = Action("Start Meeting"); _start.Name = "StartMeeting"; _start.IsDefault = true; _start.SetResourceReference(Control.BackgroundProperty, "SequenceAccentSurface"); _start.Click += StartClicked;
        var columns = new Grid(); root.Children.Add(columns);
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 340 });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star), MinWidth = 380 });
        var body = new StackPanel(); columns.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });

        var scene = Section(body, "1  Scene");
        string sceneGroup = "MeetingScene-" + Guid.NewGuid().ToString("N");
        _useCurrent = new RadioButton { Name = "UseCurrentSetup", Content = "Use current editor setup", GroupName = sceneGroup, Margin = new Thickness(0, 0, 0, 3) }; scene.Children.Add(_useCurrent);
        _currentHint = Text("Includes changes kept with Apply in URDF Scene Setup.", 13, true); scene.Children.Add(_currentHint);
        _useSaved = new RadioButton { Name = "UseSavedScene", Content = "Use saved scene", GroupName = sceneGroup, Margin = new Thickness(0, 6, 0, 3) }; scene.Children.Add(_useSaved);
        _scenePicker = new StackPanel { Margin = new Thickness(23, 0, 0, 0) }; scene.Children.Add(_scenePicker);
        var row = new DockPanel(); _scenePicker.Children.Add(row);
        var browse = new Button { Content = "Browse…", Padding = new Thickness(9, 5, 9, 5), Margin = new Thickness(6, 0, 0, 0), ToolTip = "Choose a complete saved scene" }; DockPanel.SetDock(browse, Dock.Right); row.Children.Add(browse);
        _scenes = new ComboBox { Name = "SceneChoice", MinWidth = 150, VerticalAlignment = VerticalAlignment.Center }; row.Children.Add(_scenes);
        _sceneDetails = Text("", 13, true); _sceneDetails.Visibility = Visibility.Collapsed; _scenePicker.Children.Add(_sceneDetails);
        browse.Click += (_, _) =>
        {
            var file = new Microsoft.Win32.OpenFileDialog { Filter = "Saved scenes|*.json", InitialDirectory = BackgroundLibrary.Folder(_root, true), Title = "Choose a complete meeting scene" };
            if (file.ShowDialog(this) != true) return;
            try
            {
                var chosen = SavedSceneCommand.Read(_root, file.FileName); var existing = _sceneItems.FirstOrDefault(i => i.FileName == chosen.FileName);
                if (existing == null) { _sceneItems.Add(chosen); _scenes.Items.Refresh(); }
                _scenes.SelectedItem = existing ?? chosen; _sceneDetails.Text = chosen.Description;
            }
            catch (Exception ex) { _sceneDetails.Text = ex.Message; ShowMessage(ex.Message, true); }
        };

        var movement = Section(body, "2  Robot movement");
        _face = new CheckBox { Name = "FaceTracking", Content = "Face tracking", Margin = new Thickness(0, 0, 0, 8), ToolTip = "Use your saved webcam face-tracking mappings" }; movement.Children.Add(_face);
        movement.Children.Add(Text("Controller", 13, true));
        _controller = new ComboBox { Name = "ControllerChoice", ItemsSource = new[] { "None", "Xbox", "Steam" } }; movement.Children.Add(_controller);
        _movementMessage = Text("", 13, true); movement.Children.Add(_movementMessage);

        var voice = Section(body, "3  Voice and mouth lights");
        voice.Children.Add(Text("Microphone (required)", 13, true));
        var micRow = new DockPanel(); voice.Children.Add(micRow);
        var refresh = new Button { Content = "Refresh", Padding = new Thickness(9, 5, 9, 5), Margin = new Thickness(6, 0, 0, 0), ToolTip = "Find microphones after connecting a device" }; DockPanel.SetDock(refresh, Dock.Right); micRow.Children.Add(refresh); refresh.Click += (_, _) => RefreshMicrophones();
        _microphone = new ComboBox { Name = "MicrophoneChoice", MinWidth = 150, VerticalAlignment = VerticalAlignment.Center }; micRow.Children.Add(_microphone);
        _level = new ProgressBar { Name = "MicrophoneLevel", Minimum = 0, Maximum = 1, Height = 14, Margin = new Thickness(0, 9, 0, 3), Foreground = Brushes.LimeGreen, Background = Brushes.DarkSlateGray }; voice.Children.Add(_level);
        _meterText = Text("Speak to test your mouth lights.", 12, true); voice.Children.Add(_meterText);
        voice.Children.Add(Text("This microphone animates the mouth lights. Select your meeting microphone separately in Teams.", 13, true));
        var adjustments = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        var advanced = new Expander { Name = "MouthAdjustments", Header = "Adjust mouth response", Content = adjustments, Margin = new Thickness(0, 10, 0, 0), IsExpanded = false };
        advanced.SetResourceReference(Control.ForegroundProperty, "PrimaryText"); voice.Children.Add(advanced);
        AddNumber(adjustments, "Mouth sensitivity", .1, 20, Settings.Gain, "Scales the mouth lights; it does not change microphone volume.", v => Settings.Gain = v);
        AddNumber(adjustments, "Ignore quiet sounds", 0, .5, Settings.Gate, "Higher values suppress more background noise.", v => Settings.Gate = v);
        AddNumber(adjustments, "Light fade time (ms)", 0, 1000, Settings.ReleaseMs, "How quickly the mouth lights fade after a sound.", v => Settings.ReleaseMs = v);
        var reset = new Button { Content = "Restore defaults", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(9, 5, 9, 5) }; adjustments.Children.Add(reset);
        reset.Click += (_, _) => { double[] defaults = { 4, .02, 180 }; for (int i = 0; i < _numbers.Count; i++) { _numbers[i].Slider.Value = defaults[i]; _numbers[i].Number.Text = defaults[i].ToString("0.###"); } ShowMessage("Mouth response defaults restored. Save or start to keep them.", false); };
        var help = new Expander { Header = "Connect to OBS / Teams…", Margin = new Thickness(1, 0, 0, 8) }; help.SetResourceReference(Control.ForegroundProperty, "PrimaryText"); body.Children.Add(help);
        help.Content = Text("1. Start Meeting to open the clean robot window.\n2. In OBS, capture the robot window and start Virtual Camera.\n3. In Teams, select OBS Virtual Camera and your speech microphone.\n\nWhen face tracking is on, it uses the physical webcam.", 13, true);

        var right = new Grid(); Grid.SetColumn(right, 2); columns.Children.Add(right);
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 230 }); right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var preview = new DockPanel();
        var previewHeading = Text("Scene preview", 17); previewHeading.FontWeight = FontWeights.SemiBold; DockPanel.SetDock(previewHeading, Dock.Top); preview.Children.Add(previewHeading);
        var details = new StackPanel { Margin = new Thickness(0, 8, 0, 0) }; DockPanel.SetDock(details, Dock.Bottom); preview.Children.Add(details);
        _sceneName = Text("Current editor setup", 15); _sceneName.FontWeight = FontWeights.SemiBold; details.Children.Add(_sceneName);
        _robotName = Text("", 13, true); details.Children.Add(_robotName); _backgroundName = Text("", 13, true); details.Children.Add(_backgroundName); _cameraName = Text("", 13, true); details.Children.Add(_cameraName);
        _previewNotice = Text("Preview opens when this window is shown.", 12, true); details.Children.Add(_previewNotice);
        var viewportBorder = new Border { Child = PreviewView, ClipToBounds = true, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 10, 0, 0) };
        viewportBorder.SetResourceReference(Border.BackgroundProperty, "ViewportBackground"); preview.Children.Add(viewportBorder);
        var previewCard = Card(preview); right.Children.Add(previewCard);
        previewCard.SizeChanged += (_, _) =>
        {
            var visibility = previewCard.ActualHeight < 295 ? Visibility.Collapsed : Visibility.Visible;
            _robotName.Visibility = _backgroundName.Visibility = _cameraName.Visibility = visibility;
        };
        var readiness = new StackPanel(); var readyCard = Card(readiness); readyCard.Margin = new Thickness(0); Grid.SetRow(readyCard, 1); right.Children.Add(readyCard);
        var readyHeading = Text("Ready to start", 17); readyHeading.FontWeight = FontWeights.SemiBold; readiness.Children.Add(readyHeading);
        TextBlock StateRow(string label, string name)
        {
            var line = new Grid { Margin = new Thickness(0, 5, 0, 0) }; line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) }); line.ColumnDefinitions.Add(new ColumnDefinition()); readiness.Children.Add(line);
            line.Children.Add(Text(label, 13, true)); var status = Text("Checking…", 13); status.Name = name; Grid.SetColumn(status, 1); line.Children.Add(status); return status;
        }
        _sceneState = StateRow("Scene", "SceneReadiness"); _cameraState = StateRow("Camera", "CameraReadiness"); _controllerState = StateRow("Controller", "ControllerReadiness"); _microphoneState = StateRow("Microphone", "MicrophoneReadiness");
        _readySummary = Text("", 12, true); _readySummary.Margin = new Thickness(0, 9, 0, 0); readiness.Children.Add(_readySummary);
    }
    private void AddNumber(Panel parent, string title, double min, double max, double value, string hint, Action<double> set)
    {
        var row = new DockPanel { Margin = new Thickness(0, 6, 0, 0) }; parent.Children.Add(row);
        var number = new TextBox { Width = 72, HorizontalContentAlignment = HorizontalAlignment.Right, ToolTip = $"{title}: {min} to {max}" }; DockPanel.SetDock(number, Dock.Right); row.Children.Add(number); row.Children.Add(Text(title, 13));
        var slider = new Slider { Minimum = min, Maximum = max, Value = double.IsFinite(value) ? Math.Clamp(value, min, max) : min, Margin = new Thickness(0, 6, 0, 0), ToolTip = hint }; parent.Children.Add(slider);
        parent.Children.Add(Text(hint, 12, true));
        void Changed() { if (!number.IsKeyboardFocusWithin) number.Text = slider.Value.ToString("0.###"); set(slider.Value); }
        slider.ValueChanged += (_, _) => Changed(); Changed();
        _numbers.Add((slider, number, title));
        number.LostKeyboardFocus += (_, _) => CommitNumber(slider, number, title);
        number.KeyDown += (_, e) => { if (e.Key == Key.Enter) { CommitNumber(slider, number, title); e.Handled = true; } };
    }
    private bool CommitNumber(Slider slider, TextBox number, string title)
    {
        if (!double.TryParse(number.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double value) || !double.IsFinite(value) || value < slider.Minimum || value > slider.Maximum)
        { ShowMessage($"{title}: enter a number from {slider.Minimum} to {slider.Maximum}.", true); return false; }
        slider.Value = value; number.Text = value.ToString("0.###"); return true;
    }
    private bool CommitNumbers()
    { foreach (var item in _numbers) if (!CommitNumber(item.Slider, item.Number, item.Title)) return false; return true; }
}
