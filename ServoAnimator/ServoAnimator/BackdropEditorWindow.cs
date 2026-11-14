using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ServoAnimator;

internal sealed class BackdropEditorWindow : Window
{
    internal BackdropCommand Result { get; }
    private readonly List<Action> _commit = new();
    internal BackdropEditorWindow(BackdropCommand initial, string root, bool backgroundOnly = false, bool scene = false)
    {
        Result = BackdropCommand.Read(initial.ToJson()); Result.Background.Stage ??= new();
        Title = scene ? "Scene settings" : "Background settings — background and 3D stage"; Width = 780; Height = 720;
        Background = new SolidColorBrush(Color.FromRgb(28, 34, 44)); Foreground = Brushes.WhiteSmoke;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var dock = new DockPanel { Margin = new Thickness(14) }; Content = dock;
        var bottom = new StackPanel(); DockPanel.SetDock(bottom, Dock.Bottom); dock.Children.Add(bottom);
        var status = new TextBlock { Foreground = Brushes.Gold, TextWrapping = TextWrapping.Wrap }; bottom.Children.Add(status);
        var save = new Button { Content = "Apply changes", HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(16, 7, 16, 7), Margin = new Thickness(0, 8, 0, 0) }; bottom.Children.Add(save);
        var tabs = new TabControl { Background = Brushes.WhiteSmoke, Foreground = Brushes.Black }; dock.Children.Add(tabs);
        StackPanel Tab(string name)
        {
            var panel = new StackPanel { Margin = new Thickness(12), Background = Brushes.WhiteSmoke };
            HelpSystem.SetTopic(panel, name == "Robot framing" ? "urdf-framing" : "urdf-backdrops");
            // Keep header contrast independent of the window's light foreground.
            tabs.Items.Add(new TabItem
            {
                Background = Brushes.Gainsboro, Foreground = Brushes.Black,
                Header = new TextBlock { Text = name, Foreground = Brushes.Black, FontWeight = FontWeights.SemiBold },
                Content = new ScrollViewer { Background = Brushes.WhiteSmoke, Foreground = Brushes.Black, Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }
            }); return panel;
        }
        TextBox Fields(StackPanel panel, object settings, string selectedPath = null)
        {
            TextBox pathBox = null;
            foreach (var property in settings.GetType().GetProperties().Where(p => p.CanWrite && p.Name is not ("Stage" or "RobotFraming" or "RotatePercent" or "Distance")))
            {
                if (backgroundOnly && settings is UrdfStageSettings && property.Name is ("RobotX" or "RobotY" or "HeadingDegrees" or "PreserveRobotFraming")) continue;
                if (!(property.PropertyType.IsEnum || property.PropertyType == typeof(bool) || property.PropertyType == typeof(string) || property.PropertyType == typeof(double) || property.PropertyType == typeof(int))) continue;
                var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) }; panel.Children.Add(row);
                row.Children.Add(new TextBlock { Text = Regex.Replace(property.Name, "([a-z])([A-Z])", "$1 $2"), Width = 240, Foreground = Brushes.Black, VerticalAlignment = VerticalAlignment.Center });
                if (property.PropertyType == typeof(bool))
                {
                    var check = new CheckBox { IsChecked = (bool)property.GetValue(settings) }; row.Children.Add(check);
                    _commit.Add(() => property.SetValue(settings, check.IsChecked == true));
                }
                else if (property.PropertyType.IsEnum)
                {
                    var choice = new ComboBox(); foreach (var item in Enum.GetValues(property.PropertyType)) choice.Items.Add(new ComboBoxItem { Content = item is UrdfBackgroundMode.Urdf3D ? "3D URDF" : item.ToString(), Tag = item });
                    choice.SelectedItem = choice.Items.Cast<ComboBoxItem>().First(c => Equals(c.Tag, property.GetValue(settings))); row.Children.Add(choice);
                    _commit.Add(() => property.SetValue(settings, ((ComboBoxItem)choice.SelectedItem).Tag));
                }
                else
                {
                    var box = new TextBox { Text = Convert.ToString(property.GetValue(settings), CultureInfo.CurrentCulture) }; row.Children.Add(box);
                    if (property.Name is "ModelFile" or "BackgroundImage") { box.Text = ConfigPathService.Reference(root, box.Text); box.ToolTip = "Path relative to Config"; }
                    if (property.Name == selectedPath) pathBox = box;
                    _commit.Add(() =>
                    {
                        object value = property.PropertyType == typeof(string) ? box.Text : Convert.ChangeType(box.Text, property.PropertyType, CultureInfo.CurrentCulture);
                        if (value is double d && !double.IsFinite(d)) throw new InvalidDataException(property.Name + " must be finite.");
                        property.SetValue(settings, value);
                    });
                }
            }
            return pathBox;
        }
        var background = Tab("Background");
        var image = Fields(background, Result.Background, "BackgroundImage");
        var chooseImage = new Button { Content = "Import background image…", Margin = new Thickness(0, 8, 0, 0) }; background.Children.Add(chooseImage);
        chooseImage.Click += (_, _) =>
        {
            string imageFolder = Path.Combine(root, UrdfPresentationSettings.ImageFolder);
            var picker = new Microsoft.Win32.OpenFileDialog { InitialDirectory = Directory.Exists(imageFolder) ? imageFolder : root, Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff" };
            if (picker.ShowDialog(this) != true) return;
            try { image.Text = UrdfPresentationSettings.ImportImage(root, picker.FileName); }
            catch (Exception ex) { status.Text = ex.Message; }
        };
        var stage = Tab("3D URDF stage");
        stage.Children.Add(new TextBlock { Text = "Position/pan: metres. Heading/view yaw: degrees. +X is forward at heading 0; positive heading turns toward +Y (robot left). Robot Centric keeps robot size during stage zoom. Room Centric uses perspective and follows the robot near the side edges.", Foreground = Brushes.DarkSlateBlue, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        var model = Fields(stage, Result.Background.Stage, "ModelFile");
        var chooseModel = new Button { Content = "Choose backdrop URDF…", Margin = new Thickness(0, 8, 0, 0) }; stage.Children.Add(chooseModel);
        chooseModel.Click += (_, _) => { var picker = new Microsoft.Win32.OpenFileDialog { Filter = "URDF model|*.urdf", FileName = ConfigPathService.Resolve(root, model.Text),
            InitialDirectory = Directory.Exists(ModelStorage.Folder(root, true)) ? ModelStorage.Folder(root, true) : root }; if (picker.ShowDialog(this) == true) model.Text = ConfigPathService.Reference(root, picker.FileName); };
        var frame = Tab("Robot framing");
        if (backgroundOnly) ((TabItem)tabs.Items[tabs.Items.Count - 1]).Visibility = Visibility.Collapsed;
        var includeFrame = new CheckBox { Content = "Apply these robot framing settings", Foreground = Brushes.Black, IsChecked = scene || Result.Framing != null, IsEnabled = !scene, Margin = new Thickness(0, 8, 0, 8) };
        frame.Children.Add(includeFrame);
        var editableFrame = Result.Framing ?? new UrdfFraming();
        frame.Children.Add(new TextBlock { Text = "Horizontal/vertical use viewport percentages. Camera Zoom uses percentages. Yaw/pitch are radians. Spin/rotate are degrees and remain independent of stage heading.", Foreground = Brushes.DarkSlateBlue, TextWrapping = TextWrapping.Wrap });
        Fields(frame, editableFrame);
        var zoomLabel = new TextBlock { Foreground = Brushes.Black, Margin = new Thickness(0, 8, 0, 4) }; frame.Children.Add(zoomLabel);
        var zoom = new Slider { Minimum = 10, Maximum = 209, Value = Math.Clamp(115 / editableFrame.Distance, 10, 209) }; frame.Children.Add(zoom);
        void UpdateZoom() => zoomLabel.Text = $"Camera Zoom: {zoom.Value:0}%";
        zoom.ValueChanged += (_, _) => UpdateZoom(); UpdateZoom();
        _commit.Add(() => editableFrame.Distance = 115 / zoom.Value);
        save.Click += (_, _) =>
        {
            try
            {
                foreach (var action in _commit) action();
                Result.Framing = !backgroundOnly && includeFrame.IsChecked == true ? editableFrame : null;
                if (!scene && Result.Background.BackgroundMode == UrdfBackgroundMode.Urdf3D && Result.Background.Stage.PreserveRobotFraming && Result.Framing != null)
                { Result.Framing.SpinDegrees = 0; Result.Framing.RotateDegrees = 0; }
                Result.Background.Stage.RobotFraming = Result.Framing;
                Result.Background.EnsureLocalImage(root);
                Result.Validate(root); DialogResult = true;
            }
            catch (Exception ex) { status.Text = ex.Message; }
        };
        HelpSystem.EnableContextHelp(this, "urdf-backdrops");
    }
}

