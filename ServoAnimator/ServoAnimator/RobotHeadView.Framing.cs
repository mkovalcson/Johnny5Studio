using System.Windows;
using System.Windows.Media.Media3D;

namespace ServoAnimator;

public sealed partial class RobotHeadView
{
    internal UrdfScene CurrentModelScene => _scene;
    internal void InstallModel(string path, UrdfScene scene, UrdfFraming frame) => LoadUrdf(path, scene, frame);
    internal string LoadedModelFile { get; private set; }
    private UrdfFraming _startupFraming;
    private double _frameHorizontalPercent, _frameVerticalPercent;
    private const double DefaultHorizontalFieldOfView = 38;
    private double _staticBackdropAspect;

    // A fitted image is the composition frame, independent of the host window's
    // aspect ratio. Camera zoom and placement remain stored in their native units.
    private Rect StaticBackdropArea(double width, double height)
    {
        if (_staticBackdropAspect <= 0 || _meetingOverride != null || width <= 1 || height <= 1) return Rect.Empty;
        double fittedWidth = Math.Min(width, height * _staticBackdropAspect);
        double fittedHeight = fittedWidth / _staticBackdropAspect;
        return new Rect((width - fittedWidth) / 2, (height - fittedHeight) / 2, fittedWidth, fittedHeight);
    }

    private void UpdateBackdropCameraProjection()
    {
        double width = _viewport.ActualWidth > 1 ? _viewport.ActualWidth : ActualWidth;
        double height = _viewport.ActualHeight > 1 ? _viewport.ActualHeight : ActualHeight;
        var area = StaticBackdropArea(width, height);
        double expansion = area.IsEmpty ? 1 : width / area.Width;
        _camera.FieldOfView = 2 * Math.Atan(Math.Tan(DefaultHorizontalFieldOfView * Deg / 2) * expansion) / Deg;
    }
    private double _modelSpinDegrees, _modelRotateDegrees;
    private Model3DGroup _presentationModel;
    private Point3D _modelRotateCenter = NeckBaseScreenAnchor;
    internal UrdfFraming CaptureFraming() => _cleanViewActive && _beforeCleanFrame != null ? _beforeCleanFrame : CaptureCurrentFraming();
    internal UrdfFraming CaptureCurrentFraming() => new()
    {
        HorizontalPercent = _frameHorizontalPercent, VerticalPercent = _frameVerticalPercent,
        SpinDegrees = _modelSpinDegrees, RotateDegrees = _modelRotateDegrees,
        Yaw = _cameraYaw, Pitch = _cameraPitch, Distance = _cameraDistance
    };
    // An explicit Save in Model and Framing supersedes the pre-clean snapshot.
    // Ordinary timeline/preview updates must not change that restore point.
    internal void RememberSavedFraming()
    {
        if (_cleanViewActive) _beforeCleanFrame = CaptureCurrentFraming();
    }
    internal void ApplyFraming(UrdfFraming frame)
    {
        _frameHorizontalPercent = double.IsFinite(frame.HorizontalPercent) ? Math.Clamp(frame.HorizontalPercent, -200, 200) : 0;
        _frameVerticalPercent = double.IsFinite(frame.VerticalPercent) ? Math.Clamp(frame.VerticalPercent, -200, 200) : 0;
        double spin = double.IsFinite(frame.SpinDegrees) ? frame.SpinDegrees : 0;
        double rotate = double.IsFinite(frame.RotateDegrees) ? Math.Clamp(frame.RotateDegrees, -360, 360) : 0;
        if (_modelSpinDegrees != spin || _modelRotateDegrees != rotate)
        {
            AdvanceTreads(0, spin - _modelSpinDegrees);
            _modelSpinDegrees = spin; _modelRotateDegrees = rotate;
            UpdateModelPresentationTransform();
        }
        ApplyCameraState(frame.Yaw, frame.Pitch, frame.Distance);
    }
    private void UpdateModelPresentationTransform()
    {
        if (_presentationModel == null) return;
        var transform = new Transform3DGroup();
        transform.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 0, 1), _modelSpinDegrees % 360), NeckBaseScreenAnchor));
        // Looking from the front (+X), negative X-axis rotation is clockwise.
        transform.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), -_modelRotateDegrees), _modelRotateCenter));
        AppendStageRobotTransform(transform);
        _presentationModel.Transform = transform;
    }
    internal void ApplyUrdfCommand(ServoNames command, double value)
    {
        if (UrdfCameraCommands.IsStageCamera(command))
        {
            ApplyStageCameraCommand(command, value); return;
        }
        if (BodyControlCatalog.IsBody(command)) { ApplyBodyControl(command, value); return; }
        if (UrdfStageSettings.IsMotion(command))
        {
            if (_stage != null) ApplyStageMotion(command == ServoNames.URDFStageX ? value / 100 : _stage.RobotX,
                command == ServoNames.URDFStageY ? value / 100 : _stage.RobotY, command == ServoNames.URDFStageHeading ? value : _stage.HeadingDegrees);
            return;
        }
        if (!double.IsFinite(value)) return;
        var frame = CaptureCurrentFraming();
        var (min, max) = ServoCommand.RangeFor(command);
        if (command != ServoNames.URDFSpin) value = Math.Clamp(value, min, max);
        switch (command)
        {
            case ServoNames.URDFCameraYaw: frame.Yaw = value * Math.PI / 180; break;
            case ServoNames.URDFCameraPitch: frame.Pitch = value * Math.PI / 180; break;
            case ServoNames.URDFSpin: frame.SpinDegrees = value; break;
            case ServoNames.URDFRotate: frame.RotateDegrees = value; break;
            case ServoNames.URDFSlide: frame.HorizontalPercent = value; break;
            case ServoNames.URDFVertical: frame.VerticalPercent = value; break;
            case ServoNames.URDFZoom: frame.Distance = 115 / value; break;
            default: return;
        }
        ApplyFraming(frame);
    }
    private Point3D OffsetCameraTarget(Point3D target)
    {
        double width = _viewport.ActualWidth > 1 ? _viewport.ActualWidth : ActualWidth;
        double height = _viewport.ActualHeight > 1 ? _viewport.ActualHeight : ActualHeight;
        if (width <= 1 || height <= 1) return target;
        double span = 2 * _cameraDistance * Math.Tan(_camera.FieldOfView * Deg * .5);
        var area = StaticBackdropArea(width, height);
        double frameWidth = area.IsEmpty ? width : area.Width;
        double frameHeight = area.IsEmpty ? height : area.Height;
        var right = new Vector3D(-Math.Sin(_cameraYaw), Math.Cos(_cameraYaw), 0);
        var up = new Vector3D(-Math.Sin(_cameraPitch) * Math.Cos(_cameraYaw), -Math.Sin(_cameraPitch) * Math.Sin(_cameraYaw), Math.Cos(_cameraPitch));
        return target - right * (span * frameWidth / width * _frameHorizontalPercent / 100)
            + up * (span * frameHeight / width * _frameVerticalPercent / 100);
    }
}
