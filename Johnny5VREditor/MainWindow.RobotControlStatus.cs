using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace ServoAnimator;

public partial class MainWindow
{
    private string _robotStatusMessage = "";
    private long _robotStatusExpires, _robotStatusRefresh;
    private bool _robotStatusSticky;
    private void SetRobotStatusMessage(string text)
    {
        _robotStatusMessage = text ?? "";
        _robotStatusSticky = new[] { "failed", "could not", "error", "missing", "not found" }
            .Any(word => _robotStatusMessage.Contains(word, StringComparison.OrdinalIgnoreCase));
        _robotStatusExpires = Environment.TickCount64 + 7000;
        _robotStatusRefresh = 0;
        RefreshRobotControlSummary();
    }
    private (string Source, bool Urdf, bool Hardware, string Reason) RobotControlState()
    {
        var view = _urdfUndocked ? _head?.HeadView : EmbeddedHeadView;
        string controller = _enabledController == ControllerKind.Xbox ? "Xbox" : _enabledController == ControllerKind.Steam ? "Steam" : "";
        bool face = FaceHasPriority;
        string source = _meetingActive ? "Meeting: " + (MeetingUsesFace ? "Webcam" + (MeetingController.HasValue ? " + " + controller : "") : controller)
            : IsRunning ? "Timeline" : _controllerLibraryRun != null ? "Library playback"
            : face ? "Webcam" + (controller.Length > 0 ? " + " + controller : "")
            : controller.Length > 0 ? controller : "Manual";
        bool urdf = view?.UrdfDriveEnabled == true && (IsRunning ? PlaybackOutputAllowed(false) : ControllerOutputAllowed(false));
        bool hardware = LiveDrive && _hw.Connected && (IsRunning ? PlaybackOutputAllowed(true) : ControllerOutputAllowed(true));
        string reason = _safeguardHolding ? "Collision safeguard is preventing movement."
            : _speedCalibrationBusy ? "Speed calibration owns robot output."
            : view?.UrdfDriveEnabled != true ? "URDF preview is off."
            : !urdf ? "URDF output is blocked by focus or session settings."
            : face && !_faceClient.Fresh ? "Waiting for fresh face tracking: check camera and face visibility."
            : _faceTrackingOverride ? "Controller override held: webcam control is suspended for the override controls."
            : face ? "Webcam owns its mapped actuators. Hold Face Tracking Override for controller takeover."
            : _enabledController.HasValue && !_controllerConnections[_enabledController.Value].Enabled ? "Selected controller is disabled or disconnected."
            : "No movement blocker detected.";
        return (source, urdf, hardware, reason);
    }
    private void RefreshRobotControlSummary()
    {
        if (MovieStatusText == null || Environment.TickCount64 < _robotStatusRefresh) return;
        _robotStatusRefresh = Environment.TickCount64 + 250;
        string weathering=UrdfScene.WeatheringActivity;
        string preparation=weathering.Split('·')[0].Trim();
        if(WeatheringStatusText.Text!=preparation)WeatheringStatusText.Text=preparation;
        WeatheringStatusText.ToolTip=weathering;
        WeatheringStatusText.Visibility=string.IsNullOrEmpty(preparation)?Visibility.Collapsed:Visibility.Visible;
        var state = RobotControlState();
        string summary = $"{state.Source} · URDF {(state.Urdf ? "ON" : "OFF")} · HW {(state.Hardware ? "ON" : "OFF")} · ⓘ";
        bool message = _robotStatusMessage.Length > 0 && (_robotStatusSticky || Environment.TickCount64 < _robotStatusExpires);
        string text = message ? (_robotStatusSticky ? "⚠ " : "") + _robotStatusMessage + $" · HW {(state.Hardware ? "ON" : "OFF")}" : summary;
        if (MovieStatusText.Text != text) MovieStatusText.Text = text;
        MovieStatusText.Foreground = _robotStatusSticky && message || _safeguardHolding ? Brushes.Orange : state.Hardware ? Brushes.LightGreen : Brushes.White;
        MovieStatusText.ToolTip = summary + "\n" + state.Reason + "\nClick for details and settings.";
    }
    private void RobotControlStatus_Click(object sender, RoutedEventArgs e)
    {
        var menu = CreateRobotControlStatusMenu();
        var target = sender as Button ?? RobotControlStatusButton;
        menu.PlacementTarget = target;
        target.ContextMenu = menu; menu.IsOpen = true;
    }
    private ContextMenu CreateRobotControlStatusMenu()
    {
        var state = RobotControlState();
        var menu = new ContextMenu { PlacementTarget = RobotControlStatusButton, Placement = PlacementMode.Top,
            Style = (Style)FindResource("RobotStatusMenuStyle") };
        var itemStyle = (Style)FindResource("RobotStatusMenuItemStyle");
        void Info(string text) => menu.Items.Add(new MenuItem { Header = text, Style = itemStyle, IsHitTestVisible = false, Focusable = false });
        void ActionItem(string text, Action action) { var item = new MenuItem { Header = text, Style = itemStyle }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        void Divider() => menu.Items.Add(new Separator { Style = (Style)FindResource("AppSeparatorStyle") });
        Info("Source: " + state.Source);
        Info("URDF output: " + (state.Urdf ? "enabled" : "disabled"));
        Info("Physical output: " + (state.Hardware ? "enabled" : "disabled") + " · hardware " + (_hw.Connected ? "connected" : "not connected"));
        Info(state.Reason);
        foreach (var kind in Enum.GetValues<ControllerKind>())
            Info($"{kind}: {(_controllerConnections[kind].Connected ? "connected" : "not connected")} · {(_controllerConnections[kind].Enabled ? "enabled" : "disabled")}");
        Info("Camera: " + (!_faceClient.Running ? "stopped" : _faceClient.Fresh ? "tracking face" : "waiting for face") + (_faceClient.Error == null ? "" : " · " + _faceClient.Error));
        if (_robotStatusMessage.Length > 0) { Divider(); Info("Last message: " + _robotStatusMessage); }
        ActionItem("Dismiss status message", () => { _robotStatusMessage = ""; _robotStatusSticky = false; _robotStatusRefresh = 0; RefreshRobotControlSummary(); });
        Divider();
        ActionItem("Focus Control…", () => FocusControl_Click(this, new RoutedEventArgs()));
        ActionItem("Xbox Mapping…", () => OpenControllerMapping(ControllerKind.Xbox));
        ActionItem("Steam Mapping…", () => OpenControllerMapping(ControllerKind.Steam));
        ActionItem("Webcam Face Tracking…", () => FaceTracking_Click(this, new RoutedEventArgs()));
        if (!_meetingActive) ActionItem("Meeting Configuration…", () => Meeting_Click(this, new RoutedEventArgs()));
        return menu;
    }
}
