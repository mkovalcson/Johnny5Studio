using System.Windows;
using System.Windows.Media.Imaging;

namespace ServoAnimator
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // App.xaml resources are loaded by Program.InitializeComponent()
            // before Run() reaches this point, so the saved palette can be
            // applied before StartupUri creates the main window.
            ThemeManager.LoadAndApply();
            var icon = BitmapFrame.Create(new Uri(
                "pack://application:,,,/AnimationEditorPlayer;component/Assets/AppIcon/animation-editor.ico",
                UriKind.Absolute));
            icon.Freeze();
            // All editor windows, including code-created dialogs and the undocked
            // robot, share the approved icon. Explicit per-window icons still win.
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
                new RoutedEventHandler((sender, args) =>
                {
                    if (sender is Window window && ReferenceEquals(args.OriginalSource, window) && window.Icon == null)
                        window.SetCurrentValue(Window.IconProperty, icon);
                }));
            base.OnStartup(e);
        }
    }
}
