using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ServoAnimator;

internal sealed class BackgroundLibraryWindow : Window
{
    internal BackgroundLibraryItem Selected { get; private set; }
    internal BackgroundLibraryWindow(string root, BackdropCommand current, bool selectOnly = false, string selectedFile = null, bool scenes = false, string robotModel = null, bool saveOnly = false)
    {
        Title = saveOnly ? (scenes ? "Save Scene As" : "Save Background As") : selectOnly ? "Select Sequence Scene" : scenes ? "Manage Library Scenes" : "Manage Library Backgrounds";
        Width = 700; Height = 620; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "AppBackground"); SetResourceReference(ForegroundProperty, "PrimaryText");
        HelpSystem.EnableContextHelp(this, "urdf-framing");
        var panel = new StackPanel { Margin = new Thickness(16) }; Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var list = new ListBox { Height = 200, Visibility = saveOnly ? Visibility.Collapsed : Visibility.Visible }; panel.Children.Add(list);
        TextBox Field(string label, bool multi = false)
        {
            panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 8, 0, 3) });
            var box = new TextBox { AcceptsReturn = multi, Height = multi ? 70 : 28 }; panel.Children.Add(box); return box;
        }
        var name = Field("Name"); var category = Field("Category"); var description = Field("Description", true);
        TextBox modelBox = null;
        if (scenes)
        {
            modelBox = Field("Robot URDF model (relative to Config)"); modelBox.Text = ConfigPathService.Reference(root, robotModel); modelBox.IsReadOnly = selectOnly || saveOnly;
        }
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gold }; panel.Children.Add(status);
        var buttons = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) }; panel.Children.Add(buttons);
        BackdropCommand draft = BackdropCommand.Read(current.ToJson());
        string editingFile = null;
        string draftModel = robotModel ?? "";
        void Refresh()
        {
            list.ItemsSource = BackgroundLibrary.List(root, out var errors, scenes); status.Text = errors;
        }
        void Button(string text, Action action)
        {
            var button = new Button { Content = text, Margin = new Thickness(3), Padding = new Thickness(8, 5, 8, 5) };
            buttons.Children.Add(button); button.Click += (_, _) => { try { action(); } catch (Exception ex) { status.Text = ex.Message; } };
        }
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is not BackgroundLibraryItem item) return;
            editingFile = item.FileName; name.Text = item.Name; category.Text = item.Category; description.Text = item.Description;
            draft = BackdropCommand.Read(item.Settings.ToJson());
            draftModel = item.RobotModelFile; if (modelBox != null) modelBox.Text = ConfigPathService.Reference(root, draftModel);
        };
        if (!selectOnly)
        {
            if (!saveOnly) Button("New from current view", () =>
            {
                list.SelectedItem = null; editingFile = null; name.Text = ""; category.Text = ""; description.Text = "";
                draft = BackdropCommand.Read(current.ToJson()); draftModel = robotModel ?? ""; if (modelBox != null) modelBox.Text = ConfigPathService.Reference(root, draftModel); name.Focus();
            });
            if (!saveOnly) Button(scenes ? "Edit background / placement…" : "Edit background…", () =>
            {
                var edit = new BackdropEditorWindow(draft, root, backgroundOnly: !scenes, scene: scenes) { Owner = this };
                if (edit.ShowDialog() == true) draft = edit.Result;
            });
            void Save(bool copy)
            {
                var item = new BackgroundLibraryItem { Name = name.Text.Trim(), Category = category.Text.Trim(), Description = description.Text.Trim(),
                    FileName = copy ? null : editingFile, Settings = draft, RobotModelFile = ConfigPathService.Resolve(root, modelBox?.Text.Trim()) };
                BackgroundLibrary.Save(root, item, scenes);
                if (saveOnly) { Selected = item; DialogResult = true; return; }
                Refresh();
                list.SelectedItem = list.Items.Cast<BackgroundLibraryItem>().First(i => i.FileName == item.FileName);
                status.Text = scenes ? "Scene saved." : "Background saved.";
            }
            Button("Save", () => Save(false)); if (!saveOnly) Button("Save As…", () => Save(true));
        }
        else
        {
            name.IsReadOnly = category.IsReadOnly = description.IsReadOnly = true;
            Button("No sequence preset", () => { Selected = null; DialogResult = true; });
        }
        if (!saveOnly) Button(selectOnly ? "Use for sequence" : "Load selected", () =>
        {
            if (list.SelectedItem is not BackgroundLibraryItem item) throw new InvalidOperationException("Select a saved item first.");
            Selected = BackgroundLibrary.Read(root, item.FileName); DialogResult = true;
        });
        Button("Close", () => Close()); Refresh();
        if (!string.IsNullOrWhiteSpace(selectedFile))
            list.SelectedItem = list.Items.Cast<BackgroundLibraryItem>().FirstOrDefault(i => string.Equals(i.FileName, selectedFile, StringComparison.OrdinalIgnoreCase));
    }
}
