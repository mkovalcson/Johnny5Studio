using System.Windows;
using System.Windows.Controls;

namespace ServoAnimator;

internal sealed class ScenePreviewCloseWindow : Window
{
    internal bool ApplyChanges { get; private set; }
    internal ScenePreviewCloseWindow()
    {
        Title = "Keep preview changes?"; Width = 520; SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "AppBackground"); SetResourceReference(ForegroundProperty, "PrimaryText");
        var body = new StackPanel { Margin = new Thickness(20) }; Content = body;
        body.Children.Add(new TextBlock { Text = "Apply keeps this setup after closing. Your saved scene stays unchanged.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) });
        var buttons = new WrapPanel(); body.Children.Add(buttons);
        foreach (string label in new[] { "Apply and Close", "Discard Preview", "Keep Editing" })
        {
            var button = new Button { Content = label, Margin = new Thickness(3), Padding = new Thickness(10, 6, 10, 6), IsCancel = label == "Keep Editing" };
            buttons.Children.Add(button);
            button.Click += (_, _) => { ApplyChanges = label == "Apply and Close"; DialogResult = label != "Keep Editing"; };
        }
    }
}
