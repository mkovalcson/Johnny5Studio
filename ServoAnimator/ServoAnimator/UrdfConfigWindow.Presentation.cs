using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ServoAnimator;

internal static class UrdfBackgroundControls
{
    internal static void Build(StackPanel PresentationPanel, Window owner, UrdfConfiguration _config, string root, Action _restored, Func<UrdfFraming> _captureFraming, bool sceneSetup = false)
    {
        PresentationPanel.Children.Clear();
        var brightText = new Style(typeof(TextBlock));
        brightText.Setters.Add(new Setter(TextBlock.ForegroundProperty, Brushes.WhiteSmoke));
        PresentationPanel.Resources[typeof(TextBlock)] = brightText;
        var settings = _config.Presentation;
        var row = new WrapPanel(); PresentationPanel.Children.Add(row);
        row.Children.Add(new TextBlock { Text = "Background type", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4) });
        var mode = new ComboBox { ItemsSource = Enum.GetValues<UrdfBackgroundMode>().Select(v => new ComboBoxItem { Content = v switch { UrdfBackgroundMode.Urdf3D => "3D URDF scenery", UrdfBackgroundMode.MovingImage => "Moving image", UrdfBackgroundMode.Color => "Solid color", _ => v.ToString() }, Tag = v }), Width = 170, Margin = new Thickness(4), ToolTip = "Moving image animates the selected image with smooth random zoom and pan." }; row.Children.Add(mode);
        mode.SelectedItem = mode.Items.Cast<ComboBoxItem>().First(i => Equals(i.Tag, settings.BackgroundMode));
        void SelectMode(UrdfBackgroundMode value) => mode.SelectedItem = mode.Items.Cast<ComboBoxItem>().First(i => Equals(i.Tag, value));
        var stageButton = new Button { Content = sceneSetup ? "Choose background URDF…" : "Configure 3D stage…", Margin = new Thickness(4) }; row.Children.Add(stageButton);
        var stagePath = new TextBlock { Text = ConfigPathService.Reference(root, settings.Stage?.ModelFile), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4), ToolTip = "Path relative to Config" };
        PresentationPanel.Children.Add(stagePath);
        stageButton.Click += (_, _) =>
        {
            if (sceneSetup)
            {
                var picker = new Microsoft.Win32.OpenFileDialog { Filter = "Background URDF|*.urdf", Title = "Choose scenery (not the robot model)",
                    InitialDirectory = Directory.Exists(ModelStorage.Folder(root, true)) ? ModelStorage.Folder(root, true) : root };
                if (picker.ShowDialog(owner) == true)
                {
                    settings.Stage ??= new(); settings.Stage.ModelFile = ConfigPathService.Reference(root, picker.FileName); stagePath.Text = settings.Stage.ModelFile;
                    _restored?.Invoke();
                }
                return;
            }
            var editor = new BackdropEditorWindow(new BackdropCommand { Background = settings, Framing = _captureFraming?.Invoke() }, root) { Owner = owner };
            if (editor.ShowDialog() != true) return;
            _config.Presentation = editor.Result.Background;
            Build(PresentationPanel, owner, _config, root, _restored, _captureFraming, sceneSetup); _restored?.Invoke();
        };
        var color = new TextBox { Text = settings.BackgroundColor, Width = 100, Margin = new Thickness(4) }; row.Children.Add(color);
        var green = new Button { Content = "Chromakey Green", Margin = new Thickness(4) }; row.Children.Add(green);
        var image = new TextBlock { Text = ConfigPathService.Reference(root, settings.BackgroundImage), TextWrapping = TextWrapping.Wrap }; PresentationPanel.Children.Add(image);
        var choose = new Button { Content = "Choose background image…", Margin = new Thickness(4) }; row.Children.Add(choose);
        SceneSetupButtonHelp.Attach(stageButton, owner);
        SceneSetupButtonHelp.Attach(green, owner);
        SceneSetupButtonHelp.Attach(choose, owner);
        var imageFit = new TextBlock { Text = "Fit entire image • Robot framing follows the image", Margin = new Thickness(4), VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(imageFit);
        var motion = new StackPanel { Margin = new Thickness(4, 8, 4, 8) }; PresentationPanel.Children.Add(motion);
        motion.Children.Add(new TextBlock { Text = "Moving background", FontWeight = FontWeights.SemiBold });
        var motionFields = new WrapPanel(); motion.Children.Add(motionFields);
        var clean = new CheckBox { Content = "Clean Undocked URDF View", Foreground = Brushes.WhiteSmoke, IsChecked = settings.CleanUndockedView, Margin = new Thickness(4) }; PresentationPanel.Children.Add(clean);
        clean.Visibility = sceneSetup ? Visibility.Collapsed : Visibility.Visible;
        var status = new TextBlock { Text = "Changes preview immediately. Apply keeps the setup; Save Scene stores a reusable scene.", TextWrapping = TextWrapping.Wrap }; PresentationPanel.Children.Add(status);
        var preview = new Border { Height = 45, Margin = new Thickness(4) }; PresentationPanel.Children.Add(preview);
        void Apply()
        {
            try
            {
                settings.EnsureLocalImage(root); image.Text = settings.BackgroundImage;
                preview.Background = settings.CreateBrush(root, (Brush)owner.FindResource("ViewportBackground"));
                _restored?.Invoke(); status.Text = "Preview updated. Apply to keep this setup, or Save Scene to reuse it later.";
            }
            catch (Exception ex) { status.Text = ex.Message; }
        }
        Slider MotionSlider(string title, double value, double minimum, double maximum, double step, string suffix, Action<double> changed)
        {
            var field = new StackPanel { Width = 220, Margin = new Thickness(4) }; motionFields.Children.Add(field);
            var label = new TextBlock(); field.Children.Add(label);
            var slider = new Slider { Minimum = minimum, Maximum = maximum, Value = double.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : minimum,
                TickFrequency = step, IsSnapToTickEnabled = true, SmallChange = step, LargeChange = step * 5 };
            field.Children.Add(slider);
            label.Text = $"{title}: {slider.Value:0.##}{suffix}";
            slider.ValueChanged += (_, _) => { label.Text = $"{title}: {slider.Value:0.##}{suffix}"; changed(slider.Value); Apply(); };
            return slider;
        }
        MotionSlider("Motion speed", settings.BackgroundMotionSpeed, .1, 5, .1, "×", v => settings.BackgroundMotionSpeed = v);
        MotionSlider("Background frame rate cap", settings.BackgroundFrameRate, 1, 60, 1, " fps", v => settings.BackgroundFrameRate = (int)v);
        Slider minimumZoom = null, maximumZoom = null;
        minimumZoom = MotionSlider("Minimum zoom", settings.BackgroundZoomMinimum, 100, 500, 1, "%", v =>
        {
            settings.BackgroundZoomMinimum = v;
            if (maximumZoom != null && maximumZoom.Value < v) maximumZoom.Value = v;
        });
        maximumZoom = MotionSlider("Maximum zoom", Math.Max(settings.BackgroundZoomMinimum, settings.BackgroundZoomMaximum), 100, 500, 1, "%", v =>
        {
            settings.BackgroundZoomMaximum = v;
            if (minimumZoom.Value > v) minimumZoom.Value = v;
        });
        MotionSlider("Pan amount", settings.BackgroundPanAmount, 0, 100, 1, "%", v => settings.BackgroundPanAmount = v);
        MotionSlider("New target interval at 1×", settings.BackgroundTargetSeconds, .5, 120, .5, " sec", v => settings.BackgroundTargetSeconds = v);
        var seedRow = new WrapPanel(); motion.Children.Add(seedRow);
        seedRow.Children.Add(new TextBlock { Text = "Random seed", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4) });
        var seed = new TextBox { Text = settings.BackgroundSeed.ToString(), Width = 110, Margin = new Thickness(4) }; seedRow.Children.Add(seed);
        seed.LostFocus += (_, _) =>
        {
            if (int.TryParse(seed.Text, out int number)) { settings.BackgroundSeed = number; Apply(); }
            else { seed.Text = settings.BackgroundSeed.ToString(); status.Text = "Random seed must be a whole number."; }
        };
        var rotation = new CheckBox { Content = "Include gentle rotation (±15°)", Foreground = Brushes.WhiteSmoke, IsChecked = settings.BackgroundRotation, Margin = new Thickness(8, 4, 4, 4) }; seedRow.Children.Add(rotation);
        rotation.Click += (_, _) => { settings.BackgroundRotation = rotation.IsChecked == true; Apply(); };
        var swaps = new CheckBox { Content = "Allow random 180° swaps", Foreground = Brushes.WhiteSmoke, IsChecked = settings.BackgroundHalfTurns, Margin = new Thickness(8, 4, 4, 4) }; seedRow.Children.Add(swaps);
        swaps.Click += (_, _) => { settings.BackgroundHalfTurns = swaps.IsChecked == true; Apply(); };
        motion.Children.Add(new TextBlock { Text = "Uses Cover layout. Pan is limited to safe image margins; 100% zoom has no pan. Rotation adds coverage zoom to hide edges. Speed scales the target interval. The same seed and settings repeat the motion. Changes restart the motion; Save persists settings.", TextWrapping = TextWrapping.Wrap });
        void UpdateMotionAvailability()
        {
            motion.IsEnabled = settings.BackgroundMode == UrdfBackgroundMode.MovingImage;
            if (sceneSetup)
            {
                bool usesImage = settings.BackgroundMode is UrdfBackgroundMode.Image or UrdfBackgroundMode.MovingImage;
                motion.Visibility = motion.IsEnabled ? Visibility.Visible : Visibility.Collapsed;
                choose.Visibility = image.Visibility = usesImage ? Visibility.Visible : Visibility.Collapsed;
                color.Visibility = green.Visibility = settings.BackgroundMode == UrdfBackgroundMode.Color ? Visibility.Visible : Visibility.Collapsed;
                stageButton.Visibility = settings.BackgroundMode == UrdfBackgroundMode.Urdf3D ? Visibility.Visible : Visibility.Collapsed;
            }
            imageFit.Visibility = settings.BackgroundMode == UrdfBackgroundMode.Image ? Visibility.Visible : Visibility.Collapsed;
            stageButton.IsEnabled = settings.BackgroundMode == UrdfBackgroundMode.Urdf3D;
            stagePath.Visibility = stageButton.IsEnabled ? Visibility.Visible : Visibility.Collapsed;
        }
        UpdateMotionAvailability();
        mode.SelectionChanged += (_, _) => { settings.BackgroundMode = (UrdfBackgroundMode)((ComboBoxItem)mode.SelectedItem).Tag; UpdateMotionAvailability(); Apply(); };
        color.LostFocus += (_, _) =>
        {
            try { ColorConverter.ConvertFromString(color.Text); settings.BackgroundColor = color.Text; Apply(); }
            catch { color.Text = settings.BackgroundColor; status.Text = "Enter a valid color, for example #00FF00."; }
        };
        green.Click += (_, _) => { color.Text = settings.BackgroundColor = "#00FF00"; SelectMode(UrdfBackgroundMode.Color); Apply(); };
        choose.Click += (_, _) =>
        {
            var imageFolder = System.IO.Path.Combine(root, UrdfPresentationSettings.ImageFolder);
            var file = new Microsoft.Win32.OpenFileDialog { InitialDirectory = System.IO.Directory.Exists(imageFolder) ? imageFolder : root, Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff" };
            if (file.ShowDialog(owner) != true) return;
            try { settings.BackgroundImage = UrdfPresentationSettings.ImportImage(root, file.FileName); image.Text = settings.BackgroundImage; if (settings.BackgroundMode != UrdfBackgroundMode.MovingImage) SelectMode(UrdfBackgroundMode.Image); Apply(); }
            catch (Exception ex) { status.Text = ex.Message; }
        };
        clean.Click += (_, _) => { settings.CleanUndockedView = clean.IsChecked == true; Apply(); };
        try { settings.EnsureLocalImage(root); image.Text = settings.BackgroundImage; preview.Background = settings.CreateBrush(root, (Brush)owner.FindResource("ViewportBackground")); } catch (Exception ex) { status.Text = ex.Message; }
    }
}
