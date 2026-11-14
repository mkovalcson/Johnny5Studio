using System.IO;
using System.Windows.Media.Media3D;
using System.Xml.Linq;
using System.Globalization;

namespace ServoAnimator;

public sealed partial class RobotHeadView
{
    private Model3DGroup _sceneLightingRoot, _stageModel;
    private UrdfScene _stageScene;
    private UrdfScene _preparedStageScene;
    private string _preparedStageFile;
    private string _stageFile;
    private UrdfStageSettings _stage;
    private string _preparedPropKey, _stagePropKey;
    private Model3DGroup _preparedProps, _stageProps;
    private static string PropKey(string root,UrdfStageSettings stage)
        =>root+"|"+System.Text.Json.JsonSerializer.Serialize(stage.Props??new());
    private void PrepareStageProps(UrdfStageSettings stage,string root)
    {
        string key=PropKey(root,stage);
        if(key==_stagePropKey||key==_preparedPropKey)return;
        _preparedProps=UrdfStagePropModels.Build(stage,root);_preparedPropKey=key;
    }
    private readonly UrdfStageSettings _vrRootPlacement=new();
    private Point3D _stageRobotAnchor;
    private double _stageFloor;
    private UrdfPresentationSettings _timelineBackground;
    internal UrdfPresentationSettings CaptureTimelineBackground() => _timelineBackground?.Clone();
    internal UrdfStageSettings CaptureStage() => (_stage??(VrEditorMode.Active?_vrRootPlacement:null))?.Clone();
    internal UrdfPresentationSettings CaptureBackground() => (_timelineBackground ?? _meetingPresentation ?? _urdfConfiguration?.Presentation ?? new()).Clone();
    internal void SetTimelineBackground(UrdfPresentationSettings settings)
    {
        if (settings != null) PrepareBackdrop(settings);
        _timelineBackground = settings?.Clone(); ApplyPresentation();
    }

    // Load before changing the active presentation. Failed model loads leave the previous backdrop intact.
    internal void PrepareBackdrop(UrdfPresentationSettings settings)
    {
        if (settings.BackgroundMode != UrdfBackgroundMode.Urdf3D) return;
        var stage = settings.Stage ?? new();
        string root = PresentationRootOverride ?? FolderSettings.Load()?.ConfigFolderOrDefault ?? AppContext.BaseDirectory;
        stage.Validate(root);
        PrepareStageProps(stage,root);
        string path = stage.ResolveModelFile(root);
        if (_stageScene != null && string.Equals(path, _stageFile, StringComparison.OrdinalIgnoreCase)) return;
        if (_preparedStageScene != null && string.Equals(path, _preparedStageFile, StringComparison.OrdinalIgnoreCase)) return;
        var loaded = UrdfScene.Load(path, optimizeExterior: false);
        _preparedStageScene = loaded; _preparedStageFile = path;
    }

    private void ConfigureStage(UrdfPresentationSettings settings)
    {
        if (_sceneLightingRoot == null) return;
        if (settings.BackgroundMode != UrdfBackgroundMode.Urdf3D)
        {
            if (_stageModel != null) _sceneLightingRoot.Children.Remove(_stageModel);
            _stage = null; UpdateModelPresentationTransform(); UpdateCamera(); return;
        }
        PrepareBackdrop(settings);
        var next = (settings.Stage ?? new()).Clone();
        string root = PresentationRootOverride ?? FolderSettings.Load()?.ConfigFolderOrDefault ?? AppContext.BaseDirectory;
        string path = next.ResolveModelFile(root);
        string propKey=PropKey(root,next);
        if (_stageScene == null || !string.Equals(path, _stageFile, StringComparison.OrdinalIgnoreCase))
        {
            var model = new Model3DGroup(); model.Children.Add(_preparedStageScene.RootModel);
            if (_stageModel != null)
            {
                _sceneLightingRoot.Children.Remove(_stageModel);
                if (_stageProps != null) _stageModel.Children.Remove(_stageProps);
            }
            _stageScene = _preparedStageScene; _stageFile = path; _stageModel = model;
            _preparedStageScene = null; _preparedStageFile = null;
        }
        if (StageHidden) _sceneLightingRoot.Children.Remove(_stageModel);   // a playing video clip or a robot-only export
        else if (!_sceneLightingRoot.Children.Contains(_stageModel)) _sceneLightingRoot.Children.Add(_stageModel);
        _stageFloor = next.FloorHeight;
        if (next.DetectFloor)
        {
            foreach (var link in XDocument.Load(path).Root.Elements("link").Where(l => ((string)l.Attribute("name") ?? "").Contains("floor", StringComparison.OrdinalIgnoreCase)))
            {
                var visual = link.Element("visual");
                var box = visual?.Element("geometry")?.Element("box");
                var size = ((string)box?.Attribute("size") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (size.Length == 3 && double.TryParse(size[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var height) &&
                    _stageScene.TryTransformLinkPoint((string)link.Attribute("name"), FloorVisualPoint(visual, height), out var top))
                { _stageFloor = top.Z; break; }
            }
        }
        if (propKey != _stagePropKey)
        {
            if (_stageProps != null) _stageModel.Children.Remove(_stageProps);
            _stageProps=_preparedProps;_stagePropKey=propKey;
            _preparedProps=null;_preparedPropKey=null;
        }
        if (_stageProps != null)
        {
            if (!_stageModel.Children.Contains(_stageProps)) _stageModel.Children.Add(_stageProps);
            _stageProps.Transform=new TranslateTransform3D(0,0,_stageFloor);
        }
        bool entering = _stage == null;
        _stage = next;
        if (entering && next.PreserveRobotFraming)
        {
            // The configured startup backdrop must preserve the model framing just
            // restored from UrdfModelSettings, not an older backdrop-editor snapshot.
            // Timeline/meeting presets may explicitly carry their own framing.
            bool explicitPreset = _timelineBackground != null || _meetingPresentation != null;
            var frame = explicitPreset && next.RobotFraming != null
                ? next.RobotFraming.Clone() : CaptureCurrentFraming();
            frame.SpinDegrees = 0; frame.RotateDegrees = 0;
            ApplyFraming(frame);
        }
        else if (next.RobotFraming != null && !next.PreserveRobotFraming) ApplyFraming(next.RobotFraming);
        UpdateStageTransforms();
    }

    private static Point3D FloorVisualPoint(XElement visual, double height)
    {
        Vector3D Vector(string value)
        {
            var parts = (value ?? "0 0 0").Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 3 ? new Vector3D(double.Parse(parts[0], CultureInfo.InvariantCulture),
                double.Parse(parts[1], CultureInfo.InvariantCulture), double.Parse(parts[2], CultureInfo.InvariantCulture)) : new Vector3D();
        }
        var origin = visual?.Element("origin");
        return UrdfScene.BuildTransform(Vector((string)origin?.Attribute("xyz")), Vector((string)origin?.Attribute("rpy")))
            .Transform(new Point3D(0, 0, height / 2));
    }

    private void AppendStageRobotTransform(Transform3DGroup transform)
    {
        if(VrEditorMode.Active&&_stage==null)
        {
            transform.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0,0,1),_vrRootPlacement.HeadingDegrees),NeckBaseScreenAnchor));
            transform.Children.Add(new TranslateTransform3D(_vrRootPlacement.RobotX,_vrRootPlacement.RobotY,0));return;
        }
        if (_stage == null) return;
        if(VrEditorMode.Active)
        {
            transform.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0,0,1),_stage.HeadingDegrees),_stageRobotAnchor));
            transform.Children.Add(new TranslateTransform3D(_stage.RobotX,_stage.RobotY,0));return;
        }
        transform.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 0, 1), _stage.HeadingDegrees - (_stage.MovementMode == UrdfStageMode.RobotCentric ? _stage.ViewYawDegrees : 0)), _stageRobotAnchor));
        if (_stage.MovementMode == UrdfStageMode.RoomCentric)
            transform.Children.Add(new TranslateTransform3D(_stage.RobotX, _stage.RobotY, 0));
    }

    private void UpdateStageTransforms()
    {
        if (_stage == null || _stageModel == null) return;
        if(VrEditorMode.Active){_stageModel.Transform=new TranslateTransform3D(0,0,_vrRestBounds.Z-_stageFloor);UpdateModelPresentationTransform();return;}
        bool robotCentric = _stage.MovementMode == UrdfStageMode.RobotCentric;
        double scale = robotCentric ? _stage.ViewZoomPercent / 100 : 1;
        var transform = new Transform3DGroup();
        transform.Children.Add(new TranslateTransform3D(-_stage.RobotX * (robotCentric ? 1 : 0), -_stage.RobotY * (robotCentric ? 1 : 0), -_stageFloor));
        transform.Children.Add(new ScaleTransform3D(scale, scale, scale));
        if (robotCentric) transform.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 0, 1), -_stage.ViewYawDegrees)));
        transform.Children.Add(new TranslateTransform3D(_stageRobotAnchor.X + (robotCentric ? _stage.ViewPanX : 0),
            _stageRobotAnchor.Y + (robotCentric ? _stage.ViewPanY : 0), _stageRobotAnchor.Z + (robotCentric ? _stage.ViewPanZ : 0)));
        _stageModel.Transform = transform;
        if (_stageProps?.Transform is TranslateTransform3D propFloor && propFloor.OffsetZ != _stageFloor)
            propFloor.OffsetZ = _stageFloor;
        UpdateModelPresentationTransform(); UpdateCamera();
    }

    internal void ApplyStageMotion(double x, double y, double heading)
    {
        if(VrEditorMode.Active&&_stage==null)
        {
            if(!double.IsFinite(x+y+heading))return;
            if(_vrRootPlacement.RobotX==x&&_vrRootPlacement.RobotY==y&&_vrRootPlacement.HeadingDegrees==heading)return;
            _vrRootPlacement.RobotX=x;_vrRootPlacement.RobotY=y;_vrRootPlacement.HeadingDegrees=heading;UpdateModelPresentationTransform();return;
        }
        if (_stage == null || !double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(heading)) return;
        if (_stage.RobotX == x && _stage.RobotY == y && _stage.HeadingDegrees == heading) return;
        AdvanceTreads(0, heading - _stage.HeadingDegrees);
        _stage.RobotX = x; _stage.RobotY = y; _stage.HeadingDegrees = heading; UpdateStageTransforms();
    }

    internal void ApplyStageCameraSettings(UrdfStageSettings settings)
    {
        if (_stage == null) return;
        bool changed = false;
        foreach (var name in UrdfCameraCommands.StageChannels)
        {
            double value = UrdfCameraCommands.Read(settings, name);
            if (!double.IsFinite(value) || Math.Abs(UrdfCameraCommands.Read(_stage, name) - value) < 1e-9) continue;
            UrdfCameraCommands.Write(_stage, name, value); changed = true;
        }
        if (!changed) return;
        if (!_stage.DetectFloor) _stageFloor = _stage.FloorHeight;
        UpdateStageTransforms();
    }

    internal void ApplyStageCameraCommand(ServoNames name, double value)
    {
        if (_stage == null || !double.IsFinite(value) || !UrdfCameraCommands.IsStageCamera(name)) return;
        if (Math.Abs(UrdfCameraCommands.Read(_stage, name) - value) < 1e-9) return;
        UrdfCameraCommands.Write(_stage, name, value);
        if (name == ServoNames.URDFFloorHeight && !_stage.DetectFloor) _stageFloor = _stage.FloorHeight;
        UpdateStageTransforms();
    }

    internal void DriveTrackForward(double centimetresPerSecond, double seconds)
    {
        if (_stage == null || !double.IsFinite(centimetresPerSecond) || !double.IsFinite(seconds) || seconds <= 0) return;
        double distance = Math.Clamp(centimetresPerSecond, -500, 500) * seconds / 100;
        AdvanceTreads(distance, 0);
        var front = TrackForwardDirection.OnFloor(_stage.HeadingDegrees, _modelSpinDegrees, _modelRotateDegrees);
        ApplyStageMotion(_stage.RobotX + distance * front.X, _stage.RobotY + distance * front.Y, _stage.HeadingDegrees);
    }

    private void AdjustStageCamera(ref Point3D target, ref double distance, ref double yaw)
    {
        if (_stage?.MovementMode != UrdfStageMode.RoomCentric) return;
        distance *= 100 / _stage.ViewZoomPercent;
        yaw += _stage.ViewYawDegrees * Deg;
        var right = new Vector3D(-Math.Sin(yaw), Math.Cos(yaw), 0);
        var forward = new Vector3D(Math.Cos(yaw), Math.Sin(yaw), 0);
        target += new Vector3D(_stage.ViewPanX, _stage.ViewPanY, _stage.ViewPanZ);
        var movement = _stageRobotAnchor + new Vector3D(_stage.RobotX, _stage.RobotY, 0) - target;
        // Follow only enough sideways to preserve the configured edge margin. Depth remains
        // perspective-correct; stop the robot crossing the camera's near plane when approaching.
        double depth = Vector3D.DotProduct(movement, forward);
        double followDepth = Math.Max(0, depth - Math.Max(.2, distance * .8));
        double visibleHalf = Math.Max(.1, distance - depth + followDepth) * Math.Tan(_camera.FieldOfView * Deg * .5);
        double limit = visibleHalf * (1 - 2 * _stage.EdgeMarginPercent / 100);
        double lateral = Vector3D.DotProduct(movement, right);
        double follow = lateral - Math.Clamp(lateral, -limit, limit);
        target += right * follow + forward * followDepth;
    }
}
