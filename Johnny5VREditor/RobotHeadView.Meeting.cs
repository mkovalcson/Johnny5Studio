using System.Windows;
using System.Windows.Media;

namespace ServoAnimator;

public sealed partial class RobotHeadView
{
    private bool _meetingCleanRequested, _cleanViewActive, _beforeCleanWarnings;
    private Brush _meetingOverride, _configuredBackground;
    private string _presentationSignature;
    private Visibility[] _cleanVisibility;
    private UrdfFraming _beforeCleanFrame;
    private UrdfPresentationSettings _meetingPresentation;
    internal string PresentationRootOverride { get; set; }
    internal void ConfigureMeetingPreview(RobotHeadView source, string root)
    {
        PresentationRootOverride = root;
        if (source != null)
        {
            SetServoConfiguration(source._servoConfiguration);
            SetUrdfConfiguration(source._urdfConfiguration);
        }
        IsHitTestVisible = false;
        SetUrdfDriveEnabled(true); SetDetachedHostState(); SetMeetingView(true);
        SetCollisionWarningsEnabled(false);
    }
    internal void MirrorMeetingPose(RobotHeadView source)
    {
        if (source == null || _scene == null) return;
        CopyPoseValues(source.CapturePose(), _pose);
        ApplyPoseEditorState(notify: false);
    }
    internal void PreviewMeetingMouth(double level)
    {
        bool enabled = _urdfDriveEnabled;
        try { _urdfDriveEnabled = true; SetMouth(level); }
        finally { _urdfDriveEnabled = enabled; }
    }
    public void SetMeetingView(bool clean, Brush background = null, UrdfPresentationSettings presentation = null)
    {
        _meetingCleanRequested = clean; _meetingOverride = clean ? background : null;
        _meetingPresentation = clean ? presentation?.Clone() : null;
        ApplyPresentation();
    }
    private void ApplyPresentation()
    {
        var settings = _timelineBackground ?? _meetingPresentation ?? _urdfConfiguration?.Presentation ?? new UrdfPresentationSettings();
        string root = PresentationRootOverride ?? FolderSettings.Load()?.ConfigFolderOrDefault ?? System.AppContext.BaseDirectory;
        string signature = root + "|" + System.Text.Json.JsonSerializer.Serialize(settings);
        if (signature != _presentationSignature)
        {
            try { settings.EnsureLocalImage(root); _configuredBackground = settings.CreateBrush(root, (Brush)FindResource("ViewportBackground")); }
            catch (System.Exception ex) { _configuredBackground = (Brush)FindResource("ViewportBackground"); _status.Text = "Background: " + ex.Message; }
            _staticBackdropAspect = settings.BackgroundMode == UrdfBackgroundMode.Image && _configuredBackground is ImageBrush image
                && image.ImageSource.Width > 0 && image.ImageSource.Height > 0 ? image.ImageSource.Width / image.ImageSource.Height : 0;
            ConfigureMovingBackground(settings);
            try { ConfigureStage(settings); }
            catch (System.Exception ex) { _status.Text = "3D backdrop: " + ex.Message; }
            _presentationSignature = signature;
        }
        Background = _exportBackground ?? _meetingOverride ?? _configuredBackground;
        UpdateMovingBackgroundSubscription();
        bool clean = !_hostIsDocked && (settings.CleanUndockedView || _meetingCleanRequested);
        UIElement[] controls = { _poseOverlay, _bottomControls, _status, _fps, _verticalResizeHandle };
        if (clean && !_cleanViewActive)
        {
            _beforeCleanFrame = CaptureFraming(); _beforeCleanWarnings = _collisionWarningsEnabled;
            _cleanVisibility = controls.Select(c => c.Visibility).ToArray();
            _orbiting = false; ReleaseMouseCapture();
            if (!_meetingCleanRequested && settings.BackgroundMode != UrdfBackgroundMode.Urdf3D) ResetCamera();
            SetCollisionWarningsEnabled(false); _cleanViewActive = true;
        }
        else if (!clean && _cleanViewActive)
        {
            _cleanViewActive = false;
            for (int i = 0; i < controls.Length; i++) controls[i].Visibility = _cleanVisibility[i];
            ApplyFraming(_beforeCleanFrame); SetCollisionWarningsEnabled(_beforeCleanWarnings);
            _cleanVisibility = null;
        }
        if (clean) foreach (var control in controls) control.Visibility = Visibility.Collapsed;
        UpdateCamera();
    }
}
