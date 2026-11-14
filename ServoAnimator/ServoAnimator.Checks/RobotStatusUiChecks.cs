using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ServoAnimator;

internal static partial class Program
{
    private static void RobotStatusUiChecks()
    {
        var app = new App(); app.InitializeComponent(); DisableTestStartup(app);
        var window = new MainWindow();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        ContextMenu CreateMenu() => (ContextMenu)typeof(MainWindow).GetMethod("CreateRobotControlStatusMenu", flags).Invoke(window, null);
        string previews = PreviewDirectory("status-previews"); Directory.CreateDirectory(previews);
        var menu = CreateMenu();
        void Layout() { menu.Measure(new Size(440, 900)); menu.Arrange(new Rect(menu.DesiredSize)); menu.UpdateLayout(); }
        Layout();
        var items = menu.Items.OfType<MenuItem>().ToArray();
        foreach (var item in items)
        {
            Check(Contrast(item.Foreground, menu.Background) >= 4.5, $"Readable status row: {item.Header}");
            Check(item.Template.FindName("StatusItemChrome", item) is Border, "Status rows use the dark popup template without the stock icon gutter");
        }
        var dismiss = items.Single(i => Equals(i.Header, "Dismiss status message"));
        // Exercise the actual template trigger, including keyboard selection.
        typeof(MenuItem).GetProperty("IsHighlighted").GetSetMethod(true).Invoke(dismiss, new object[] { true });
        Layout();
        var chrome = (Border)dismiss.Template.FindName("StatusItemChrome", dismiss);
        Check(Contrast(dismiss.Foreground, chrome.Background) >= 4.5 && chrome.Background != Brushes.Transparent, "Highlighted action retains readable light text");
        RenderControl(menu, Path.Combine(previews, "RobotStatus.png"), (int)Math.Ceiling(menu.DesiredSize.Width), (int)Math.Ceiling(menu.DesiredSize.Height));

        typeof(MainWindow).GetMethod("SetRobotStatusMessage", flags).Invoke(window, new object[] {
            "Could not connect to the physical robot. Check that the controller is powered on and the configured hardware connection is available, then try again." });
        menu = CreateMenu(); Layout();
        var message = menu.Items.OfType<MenuItem>().Single(i => i.Header.ToString().StartsWith("Last message:"));
        Check(message.ActualHeight > items[0].ActualHeight && menu.ActualWidth <= 440, "Long status messages wrap within the popup");
        var jobs=typeof(UrdfScene).GetField("_weatheringJobs",BindingFlags.Static|BindingFlags.NonPublic);
        var progress=typeof(UrdfScene).GetField("_weatheringProgress",BindingFlags.Static|BindingFlags.NonPublic);
        var status=(TextBlock)window.FindName("WeatheringStatusText");
        var collapsedStatus=(TextBlock)window.FindName("CollapsedWeatheringStatusText");
        string warning=((TextBlock)window.FindName("MovieStatusText")).Text;
        void RefreshStatus()
        {
            typeof(MainWindow).GetField("_robotStatusRefresh",flags).SetValue(window,0L);
            typeof(MainWindow).GetMethod("RefreshRobotControlSummary",flags).Invoke(window,null);
            window.Dispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.DataBind);
        }
        int oldJobs=(int)jobs.GetValue(null);string oldProgress=(string)progress.GetValue(null);
        try
        {
            jobs.SetValue(null,1);progress.SetValue(null,"Preparing weathering… 37% · You can keep using the editor.");RefreshStatus();
            Check(status.Visibility==Visibility.Visible&&status.Text=="Preparing weathering… 37%","Bottom-right status displays active weathering percentage without opening configuration");
            Check(collapsedStatus.Visibility==Visibility.Visible&&collapsedStatus.Text==status.Text,"Weathering progress remains available with the Movie Timeline collapsed");
            Check(((TextBlock)window.FindName("MovieStatusText")).Text==warning,"Weathering progress preserves the existing hardware warning");
            RenderControl((FrameworkElement)window.FindName("RobotControlStatusButton"),Path.Combine(previews,"WeatheringStatus.png"),440,95);
            jobs.SetValue(null,0);RefreshStatus();
            Check(status.Visibility==Visibility.Collapsed&&collapsedStatus.Visibility==Visibility.Collapsed,"Weathering status clears when preparation finishes");
            Check(((TextBlock)window.FindName("MovieStatusText")).Text==warning,"Completing preparation leaves other status messages intact");
        }
        finally{jobs.SetValue(null,oldJobs);progress.SetValue(null,oldProgress);RefreshStatus();}
        RenderControl(menu, Path.Combine(previews, "RobotStatusLongMessage.png"), (int)Math.Ceiling(menu.DesiredSize.Width), (int)Math.Ceiling(menu.DesiredSize.Height));
        menu.Items.OfType<MenuItem>().Single(i => Equals(i.Header, "Dismiss status message")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Check((string)typeof(MainWindow).GetField("_robotStatusMessage", flags).GetValue(window) == "", "Dismiss action still clears the status message");
        Check(!window.IsLoaded, "Popup verification never starts the editor or hardware");
    }

    private static double Contrast(Brush foreground, Brush background)
    {
        double Luminance(Brush brush)
        {
            var color = ((SolidColorBrush)brush).Color;
            double Linear(byte value) { double c = value / 255d; return c <= .04045 ? c / 12.92 : Math.Pow((c + .055) / 1.055, 2.4); }
            return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
        }
        double a = Luminance(foreground), b = Luminance(background);
        return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
    }
}
