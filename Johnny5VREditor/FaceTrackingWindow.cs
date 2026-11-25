using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ServoAnimator;

internal sealed partial class FaceTrackingWindow : Window
{
    private readonly FaceTrackingClient _client;
    private readonly FaceTrackingSettings _draft;
    private readonly Image _image = new() { Width = 400, Height = 300, Stretch = Stretch.Uniform };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _trackingStatus = new() { FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) };
    private readonly TextBlock _configurationState = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
    private readonly TextBox _camera = new() { Width = 48, Margin = new Thickness(6), ToolTip = "Camera index: 0 is normally the first camera." };
    private readonly Button _cameraToggle = new() { Content = "Start camera", Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(6) };
    private readonly WrapPanel _cameraButtons = new();
    private readonly WrapPanel _configurationActions = new() { Margin = new Thickness(0, 10, 0, 0) };
    private readonly Expander _performanceDetails = new() { Header = "Performance details", Margin = new Thickness(0, 8, 0, 0) };
    private readonly CheckBox _advancedMappings = new() { Content = "Advanced mapping settings", Margin = new Thickness(12, 0, 0, 0) };
    private readonly List<DataGridColumn> _advancedColumns = new();
    private string _savedSignature, _appliedSignature;
    private readonly ComboBox _frameRate = new() { Width = 85, Margin = new Thickness(6), VerticalAlignment = VerticalAlignment.Center,
        ToolTip = "Requested camera and face-processing rate. Changing this restarts a running camera. Save configuration to retain it." };
    private readonly TextBlock _processingRate = new() { FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 8), TextWrapping = TextWrapping.Wrap,
        ToolTip = "Actual completed tracking frames per elapsed second, averaged over about one second. Includes frames where no face is detected; this is not the camera preview refresh rate." };
    private readonly TextBlock _captureDiagnostics = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6),
        ToolTip = "Resolution comes from received frames. Driver FPS is reported by the camera, not measured throughput. Camera wait includes acquiring/decoding a frame. Face processing includes color conversion, MediaPipe and signal calculations, excluding camera wait, preview preparation and rate pacing. Timings average completed frames over about one second." };
    private readonly TextBox _values = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Height = 110, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly CheckBox _preview = new() { Content = "Drive URDF preview using applied settings (no physical output)" };
    private readonly CheckBox _mirrorVideo = new() { Content = "Mirror webcam", Margin = new Thickness(6), VerticalAlignment = VerticalAlignment.Center,
        ToolTip = "Flip the webcam image horizontally before face tracking and preview. Changing this restarts a running camera. Save configuration to retain it. Mirroring in both the webcam software and editor cancels out." };
    private readonly CheckBox _servoSpeeds = new() { Content = "Enforce Servo Speed limits in preview and recording", Margin = new Thickness(0, 8, 0, 8),
        ToolTip = "Use each actuator's configured Speed and Acceleration for the active profile, plus Servo Speed Calibration timing. Record the resulting motion instead of the requested targets. Capture uses 1× playback and retains these samples without take smoothing or reduction." };
    private readonly DataGrid _grid = new() { AutoGenerateColumns = false, CanUserAddRows = false };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private string _lastPreview;
    private string _message;
    private sealed record Choice<T>(T Value, string Label)
    {
        public override string ToString() => Label;
    }
    public bool PreviewEnabled => _preview.IsChecked == true;
    public Action<FaceTrackingSettings> ApplyRequested;
    public Action PreviewChanged;
    internal Func<FaceMapping,double?> AppliedOutput;
    public Action<FaceTrackingSettings> SaveRequested;
    public FaceTrackingWindow(FaceTrackingClient client, FaceTrackingSettings current, FaceTrackingSettings savedSettings = null)
    {
        _client = client;
        _draft = System.Text.Json.JsonSerializer.Deserialize<FaceTrackingSettings>(System.Text.Json.JsonSerializer.Serialize(current));
        PreviewSettings = System.Text.Json.JsonSerializer.Deserialize<FaceTrackingSettings>(System.Text.Json.JsonSerializer.Serialize(current));
        _appliedSignature = Signature(current); _savedSignature = Signature(savedSettings ?? current);
        HelpSystem.EnableContextHelp(this, "webcam-face-tracking");
        Title = "Webcam Face Tracking"; Width = 1292; Height = 944; MinWidth = 1000; MinHeight = 700;
        SetResourceReference(BackgroundProperty, "AppBackground"); SetResourceReference(ForegroundProperty, "PrimaryText");
        var root = new DockPanel { Margin = new Thickness(16) }; root.SetResourceReference(Panel.BackgroundProperty, "AppBackground"); Content = root;
        DockPanel.SetDock(_configurationActions, Dock.Bottom); root.Children.Add(_configurationActions);
        var buttons = _cameraButtons;
        var cameraGroup = new GroupBox { Header = "Camera", Content = buttons, Margin = new Thickness(0, 0, 0, 10), Padding = new Thickness(6) };
        DockPanel.SetDock(cameraGroup, Dock.Top); root.Children.Add(cameraGroup);
        buttons.Children.Add(new TextBlock { Text = "Camera index:", VerticalAlignment = VerticalAlignment.Center });
        var camera = _camera; camera.Text = current.Camera.ToString(); buttons.Children.Add(camera);
        camera.TextChanged += (_, _) => { if (int.TryParse(camera.Text, out int index)) _draft.Camera = index; };
        buttons.Children.Add(new TextBlock { Text = "Tracking rate:", VerticalAlignment = VerticalAlignment.Center });
        _frameRate.Items.Add(new Choice<int>(30, "30 fps")); _frameRate.Items.Add(new Choice<int>(60, "60 fps"));
        _frameRate.SelectedIndex = current.FrameRate == 60 ? 1 : 0; buttons.Children.Add(_frameRate);
        _frameRate.SelectionChanged += (_, _) =>
        {
            _draft.FrameRate = ((Choice<int>)_frameRate.SelectedItem).Value;
            PreviewSettings.FrameRate = _draft.FrameRate;
            try { _message = null; if (_client.Running) StartCamera(); }
            catch (Exception ex) { _message = ex.Message; }
            Refresh();
        };
        _mirrorVideo.IsChecked = _draft.MirrorVideo; buttons.Children.Add(_mirrorVideo);
        void ChangeMirror()
        {
            _draft.MirrorVideo = _mirrorVideo.IsChecked == true;
            PreviewSettings.MirrorVideo = _draft.MirrorVideo;
            try { _message = null; if (_client.Running) StartCamera(); }
            catch (Exception ex) { _message = ex.Message; }
            Refresh();
        }
        _mirrorVideo.Checked += (_, _) => ChangeMirror();
        _mirrorVideo.Unchecked += (_, _) => ChangeMirror();
        void Button(string text, Action action) { var b = new Button { Content = text, Margin = new Thickness(5), Padding = new Thickness(8, 4, 8, 4) }; b.Click += (_, _) => { try { _message = null; action(); } catch (Exception ex) { _status.Text = _message = ex.Message; } }; buttons.Children.Add(b); }
        buttons.Children.Add(_cameraToggle);
        _cameraToggle.Click += (_, _) =>
        {
            try
            {
                _message = null;
                if (_client.Running) { _preview.IsChecked = false; _client.Dispose(); _image.Source = null; _lastPreview = null; }
                else StartCamera();
            }
            catch (Exception ex) { _message = ex.Message; }
            Refresh();
        };
        Button("Set neutral", () =>
        {
            if (!_client.Fresh) throw new InvalidOperationException("Start the camera and face it first.");
            var neutralValues = _client.Latest.Values;
            if (neutralValues.TryGetValue("headPitch", out double pitch)) _draft.Pistons.PitchNeutral = pitch;
            if (neutralValues.TryGetValue("headRoll", out double roll)) _draft.Pistons.RollNeutral = roll;
            foreach (var m in _draft.Mappings)
                if (m.Signal is "LPiston" or "RPiston") m.Neutral = 0;
                else if (neutralValues.TryGetValue(m.Signal, out double value)) m.Neutral = value;
            _grid.Items.Refresh(); _status.Text = _message = "Neutral captured. Apply to try it, or Save configuration to keep it.";
        });
        DockPanel.SetDock(_warnings, Dock.Top); root.Children.Add(_warnings);
        var upper = new StackPanel();
        var previewRow = new Grid(); previewRow.ColumnDefinitions.Add(new() { Width = new GridLength(416) }); previewRow.ColumnDefinitions.Add(new()); upper.Children.Add(previewRow);
        var cameraPreview = new Border { Child = _image, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 12, 8) };
        cameraPreview.SetResourceReference(Border.BorderBrushProperty, "DividerBrush"); previewRow.Children.Add(cameraPreview);
        _captureTestButton.Click += async (_, _) =>
        {
            try { await RunCaptureTestAsync(int.Parse(camera.Text), buttons); }
            catch (Exception error) { _captureTestReadout.Text = error.Message; }
        };
        var help = new StackPanel { Margin = new Thickness(4, 4, 0, 8) }; Grid.SetColumn(help, 1); previewRow.Children.Add(help);
        help.Children.Add(_trackingStatus); help.Children.Add(_processingRate);
        help.Children.Add(new TextBlock { Text = "Start camera → Look straight ahead → Set neutral", TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        help.Children.Add(new TextBlock { Text = "Adjust the mappings below, then Apply to try them. Save configuration keeps them for next time.", TextWrapping = TextWrapping.Wrap });
        help.Children.Add(_preview); _preview.Margin = new Thickness(0, 12, 0, 12);
        _servoSpeeds.IsChecked = _draft.EnforceServoSpeeds;
        _servoSpeeds.Checked += (_, _) => _draft.EnforceServoSpeeds = true;
        _servoSpeeds.Unchecked += (_, _) => _draft.EnforceServoSpeeds = false;
        help.Children.Add(_servoSpeeds);
        _servoSpeeds.ToolTip += " Nod and tilt must use matching Speed profiles. N/C keeps the current profile.";
        _preview.Checked += (_, _) => PreviewChanged?.Invoke();
        _preview.Unchecked += (_, _) => PreviewChanged?.Invoke();
        _preview.ToolTip = "Uses the last applied mappings. Draft mapping edits take effect when you click Apply.";
        help.Children.Add(_status);
        help.Children.Add(new TextBlock { Text = "To record: Apply, close this window, then choose Webcam in Record. The camera keeps running. Applied settings also remain active for Meetings until you exit the editor.", Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap });
        var details = new StackPanel(); details.Children.Add(_captureDiagnostics); details.Children.Add(_captureTestButton); details.Children.Add(_captureTestReadout); details.Children.Add(_values);
        _performanceDetails.SetResourceReference(ForegroundProperty, "PrimaryText");
        _performanceDetails.Content = details; upper.Children.Add(_performanceDetails);
        var pistonPanel = new StackPanel(); pistonPanel.Children.Add(CreatePistonSettings());
        var addPistons = new Button { Content = "Add piston mappings", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 5, 0, 5) };
        addPistons.Click += (_, _) => AddPistonMappings(); pistonPanel.Children.Add(addPistons);
        var pistonSettings = new Expander { Header = "Advanced neck calibration", Content = pistonPanel, Margin = new Thickness(0, 6, 0, 8) };
        pistonSettings.SetResourceReference(ForegroundProperty, "PrimaryText");
        HelpSystem.SetTopic(pistonSettings, "piston-signals"); upper.Children.Add(pistonSettings);
        var mappingHeading = new WrapPanel { Margin = new Thickness(0, 8, 0, 8) };
        mappingHeading.Children.Add(new TextBlock { Text = "Face movement → Robot control", FontSize = 16, FontWeight = FontWeights.SemiBold });
        mappingHeading.Children.Add(_advancedMappings);
        var learn = new Button { Content = "Learn range for selected mapping…", Margin = new Thickness(12,0,0,0), Padding = new Thickness(8,3,8,3) };
        learn.Click += (_, _) => LearnSelectedRange();
        mappingHeading.Children.Add(learn);
        upper.Children.Add(mappingHeading);
        upper.Children.Add(new TextBlock { Text = "Live meters show input relative to neutral and the draft target. The cyan marker shows the applied target after smoothing. Servo speed limits can make the model follow more slowly.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,6), FontSize = 11 });
        void FooterButton(string text, Action action)
        {
            var button = new Button { Content = text, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0) };
            button.Click += (_, _) => { try { action(); } catch (Exception ex) { _message = ex.Message; } Refresh(); };
            _configurationActions.Children.Add(button);
        }
        FooterButton("Apply", Apply); FooterButton("Save configuration", SaveConfiguration);
        FooterButton("Close", Close); _configurationActions.Children.Add(_configurationState);
        HelpSystem.SetTopic(_grid, "face-signals");
        _grid.ItemsSource = _draft.Mappings;
        var mappingsPanel = new StackPanel(); mappingsPanel.Children.Add(_grid);
        ScrollViewer.SetVerticalScrollBarVisibility(_grid, ScrollBarVisibility.Disabled);
        var add = new Button { Content = "+", ToolTip = "Add a signal mapping", Width = 34, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 5, 0, 0), Padding = new Thickness(4) };
        add.Click += (_, _) => { if (!CommitMappings()) return; var mapping = new FaceMapping(); _draft.Mappings.Add(mapping); _grid.Items.Refresh(); _grid.SelectedItem = mapping; add.BringIntoView(); };
        mappingsPanel.Children.Add(add);
        var body = new StackPanel(); body.Children.Add(upper); body.Children.Add(mappingsPanel);
        root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        // DataGridComboBoxColumn supplies its own styles, bypassing the app's
        // implicit dark ComboBox template unless it is assigned explicitly.
        var mappingPicklistStyle = new Style(typeof(ComboBox), (Style)FindResource(typeof(ComboBox)));
        mappingPicklistStyle.Setters.Add(new Setter(ItemsControl.ItemContainerStyleProperty, (Style)FindResource(typeof(ComboBoxItem))));
        mappingPicklistStyle.Setters.Add(new Setter(ToolTipProperty, new Binding("SelectedItem.Label") { RelativeSource = RelativeSource.Self }));
        _grid.HeadersVisibility = DataGridHeadersVisibility.Column;
        var heading = new FrameworkElementFactory(typeof(TextBlock)); heading.SetBinding(TextBlock.TextProperty, new Binding()); heading.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        _grid.ColumnHeaderStyle = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader), (Style)FindResource(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader)));
        _grid.ColumnHeaderStyle.Setters.Add(new Setter(ContentControl.ContentTemplateProperty, new DataTemplate { VisualTree = heading }));
        _grid.Columns.Add(new DataGridCheckBoxColumn { Header = "On", Binding = new Binding("Enabled"), Width = 32 });
        _grid.Columns.Add(new DataGridComboBoxColumn { Header = "Face movement", ItemsSource = FaceTrackingSettings.Signals.Select(s => new Choice<string>(s, UiNames.Words(s))).ToArray(), SelectedValueBinding = new Binding("Signal") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, SelectedValuePath = "Value", DisplayMemberPath = "Label", Width = 180, ElementStyle = mappingPicklistStyle, EditingElementStyle = mappingPicklistStyle });
        var targets = new List<Choice<string>>();
        foreach (var servo in Enum.GetValues<ServoNames>().Where(s => !ServoCommand.IsTextValued(s) && !ServoCommand.IsUrdfOnly(s)))
        {
            targets.Add(new("Servo:" + servo, ServoCommand.DisplayName(servo)));
            foreach (var child in ServoConfiguration.ControlsFor(servo)) targets.Add(new($"Child:{servo}:{child}", UiNames.Target($"Child:{servo}:{child}")));
        }
        _grid.Columns.Add(new DataGridComboBoxColumn { Header = "Robot control", ItemsSource = targets, SelectedValueBinding = new Binding("Target") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, SelectedValuePath = "Value", DisplayMemberPath = "Label", Width = 250, ElementStyle = mappingPicklistStyle, EditingElementStyle = mappingPicklistStyle });
        _grid.Columns.Add(new DataGridComboBoxColumn { Header = "Speed", ItemsSource = Enum.GetValues<ServoSpeed>().Select(s => new Choice<ServoSpeed>(s, s == ServoSpeed.NoChange ? "N/C · Keep current" : s.ToString())).ToArray(), SelectedValueBinding = new Binding("Speed") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, SelectedValuePath = "Value", DisplayMemberPath = "Label", Width = 125, ElementStyle = mappingPicklistStyle, EditingElementStyle = mappingPicklistStyle });
        foreach (var (name, label, width) in new[] { ("InputMin", "Input minimum", 72), ("InputMax", "Input maximum", 72), ("OutputMin", "Output minimum", 72), ("OutputMax", "Output maximum", 72), ("DeadZone", "Dead zone", 65), ("SmoothingMs", "Smoothing (ms)", 80) })
            _grid.Columns.Add(new DataGridTextColumn { Header = label, Binding = new Binding(name) { StringFormat = "0.####" }, Width = width });
        var neutralText = new FrameworkElementFactory(typeof(TextBlock)); neutralText.SetBinding(TextBlock.TextProperty, new Binding("Neutral") { StringFormat = "0.#####" }); neutralText.SetValue(FrameworkElement.MarginProperty, new Thickness(3));
        var neutralEdit = new FrameworkElementFactory(typeof(TextBox)); neutralEdit.SetBinding(TextBox.TextProperty, new Binding("Neutral") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus });
        _grid.Columns.Add(new DataGridTemplateColumn { Header = "Neutral", Width = 85, CellTemplate = new DataTemplate { VisualTree = neutralText }, CellEditingTemplate = new DataTemplate { VisualTree = neutralEdit } });
        _advancedColumns.AddRange(_grid.Columns.Skip(4));
        foreach (var column in _advancedColumns) column.Visibility = Visibility.Collapsed;
        void ShowAdvancedMappings()
        {
            if (!CommitMappings()) { _advancedMappings.IsChecked = true; return; }
            foreach (var column in _advancedColumns) column.Visibility = _advancedMappings.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }
        _advancedMappings.Checked += (_, _) => ShowAdvancedMappings();
        _advancedMappings.Unchecked += (_, _) => ShowAdvancedMappings();
        _advancedMappings.IsChecked = true;
        AddLiveMappingColumn();
        _grid.Columns.Add(new DataGridCheckBoxColumn { Header = "Reverse", Binding = new Binding("Reverse"), Width = 62 });
        var remove = new FrameworkElementFactory(typeof(Button)); remove.SetValue(ContentControl.ContentProperty, "X"); remove.SetValue(FrameworkElement.ToolTipProperty, "Remove this signal mapping"); remove.SetValue(Control.PaddingProperty, new Thickness(4, 0, 4, 0));
        remove.AddHandler(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, new RoutedEventHandler((sender, _) => { if (((FrameworkElement)sender).DataContext is FaceMapping mapping) RemoveMapping(mapping); }));
        _grid.Columns.Add(new DataGridTemplateColumn { Header = "", Width = 34, CellTemplate = new DataTemplate { VisualTree = remove } });
        _timer.Tick += (_, _) => Refresh(); Loaded += (_, _) => _timer.Start(); Closed += (_, _) => { _timer.Stop(); _preview.IsChecked = false; };
        Closed += (_, _) => { _captureWindowClosed = true; _captureTestCancellation?.Cancel(); };
        Refresh();
    }
    private void Apply()
    {
        if (!CommitMappings()) throw new InvalidOperationException("Correct the mapping values first.");
        ReadCameraIndex(); _draft.Validate();
        if (_client.Running && (_client.CameraIndex != _draft.Camera || _client.RequestedFrameRate != _draft.FrameRate || _client.MirrorVideo != _draft.MirrorVideo)) StartCamera();
        ApplyRequested?.Invoke(System.Text.Json.JsonSerializer.Deserialize<FaceTrackingSettings>(Signature(_draft)));
        PreviewSettings = System.Text.Json.JsonSerializer.Deserialize<FaceTrackingSettings>(System.Text.Json.JsonSerializer.Serialize(_draft));
        _appliedSignature = Signature(_draft);
        _status.Text = _message = "Configuration applied for this session.";
        Refresh();
    }
    private static string Signature(FaceTrackingSettings settings) => System.Text.Json.JsonSerializer.Serialize(settings);
    private void SaveConfiguration()
    {
        Apply();
        SaveRequested?.Invoke(System.Text.Json.JsonSerializer.Deserialize<FaceTrackingSettings>(_appliedSignature));
        _savedSignature = _appliedSignature;
        _status.Text = _message = "Configuration saved and applied.";
        Refresh();
    }
    private void ReadCameraIndex()
    {
        if (!int.TryParse(_camera.Text, out int camera) || camera is < 0 or > 20) throw new InvalidOperationException("Enter a camera index from 0 to 20.");
        _draft.Camera = camera;
    }
    private void StartCamera()
    {
        ReadCameraIndex(); _client.Start(_draft.Camera, _draft.FrameRate, _draft.MirrorVideo);
        _lastPreview = null; _image.Source = null;
    }
    private bool CommitMappings()
    {
        if (_grid.CommitEdit(DataGridEditingUnit.Cell, true) && _grid.CommitEdit(DataGridEditingUnit.Row, true)) return true;
        _status.Text = _message = "Correct the mapping values first."; return false;
    }
    private void RemoveMapping(FaceMapping mapping)
    {
        if (_grid.CurrentItem == mapping)
        {
            _grid.CancelEdit(DataGridEditingUnit.Cell); _grid.CancelEdit(DataGridEditingUnit.Row);
        }
        else if (!CommitMappings()) return;
        _draft.Mappings.Remove(mapping); _grid.Items.Refresh();
    }
    private void Refresh()
    {
        _cameraToggle.Content = _client.Running ? "Stop camera" : "Start camera";
        _trackingStatus.Text = _client.Fresh ? "Tracking face" : _client.Running || _client.HasRecentResults ? "Looking for face · output held" : "Camera stopped";
        _trackingStatus.Foreground = _client.Fresh ? Brushes.LightGreen : (Brush)FindResource("HeaderText");
        string draftSignature = Signature(_draft);
        _configurationState.Text = !int.TryParse(_camera.Text, out int camera) || camera is < 0 or > 20
            ? "Enter a camera index from 0 to 20."
            : draftSignature != _appliedSignature ? "Changes pending · Apply to try them"
            : _appliedSignature != _savedSignature ? "Applied · not saved" : "Saved configuration";
        _warnings.Text = string.Join("\n", _draft.PistonWarnings());
        var packet = _client.Latest;
        RefreshMappingMeters(packet);
        _processingRate.Text = _client.ProcessingFps is double fps
            ? $"Processing: {fps:0.0} fps average · target {_client.RequestedFrameRate} fps"
            : _client.HasRecentResults ? "Processing: measuring average…" : "Processing: — fps · no recent results";
        static string Milliseconds(double? value) => value is double ms && double.IsFinite(ms) && ms >= 0 ? $"{ms:0.0} ms" : "measuring…";
        if (_client.HasRecentResults && packet != null)
        {
            string resolution = packet.CaptureWidth is > 0 && packet.CaptureHeight is > 0 ? $"{packet.CaptureWidth} × {packet.CaptureHeight}" : "resolution unavailable";
            string reported = packet.ReportedCaptureFps is double rate && double.IsFinite(rate) && rate > 0 ? $"driver reports {rate:0.##} fps" : "driver rate unavailable";
            if (!string.IsNullOrWhiteSpace(packet.PixelFormat)) reported += " · " + packet.PixelFormat;
            if (!string.IsNullOrWhiteSpace(packet.Backend)) reported += " · " + packet.Backend;
            _captureDiagnostics.Text = $"Capture: {resolution} · {reported}\nCamera wait: {Milliseconds(packet.CameraWaitMs)} avg · Face processing: {Milliseconds(packet.FaceProcessingMs)} avg";
            if (packet.FormatRequestAccepted == false) _captureDiagnostics.Text += $"\nMJPG request not accepted; capture reports {packet.PixelFormat}.";
        }
        else _captureDiagnostics.Text = "Capture: —\nCamera wait: — · Face processing: —";
        _status.Text = _message ?? _client.Error ?? "";
        if (_client.HasRecentResults && packet != null) _captureDiagnostics.Text += $"\nLast frame total: {packet.LatencyMs:0} ms";
        if (packet == null) { _values.Text = "No recent face signals."; return; }
        var signalValues = _draft.SignalValues(packet, out bool clipped);
        if (clipped) _status.Text += " · Piston clipping (" + _draft.Pistons.ClipMode + ")";
        _values.Text = string.Join("  ", _draft.Mappings.Select(m => signalValues.TryGetValue(m.Signal, out double value) ? $"{UiNames.Words(m.Signal)}: {value:0.00} → {m.Map(value):0.0}" : $"{UiNames.Words(m.Signal)}: unavailable"));
        string preview = _client.Preview;
        if (preview == null || preview == _lastPreview) return;
        _lastPreview = preview;
        try
        {
            using var stream = new MemoryStream(Convert.FromBase64String(preview));
            var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); _image.Source = bitmap;
        }
        catch { }
    }
}
