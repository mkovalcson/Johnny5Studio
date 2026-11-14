namespace ServoAnimator;

public sealed partial class RobotHeadView
{
    private UrdfScene _accessoryDefaultScene;
    private UrdfAccessory? _accessoryDefault;
    private void ApplyAccessoryDefault()
    {
        var selected = _urdfConfiguration.Rendering.DefaultAccessory;
        if (_scene == null || (ReferenceEquals(_scene,_accessoryDefaultScene) && _accessoryDefault == selected)) return;
        _accessoryDefaultScene = _scene; _accessoryDefault = selected;
        SetToolboxShown(selected == UrdfAccessory.Toolbox);
        SetLaserHidden(selected == UrdfAccessory.None);
    }
    internal void ApplyAccessoryTimeline(IEnumerable<ServoCommand> commands, double cursor)
    {
        var selected = _urdfConfiguration.Rendering.DefaultAccessory;
        SetToolboxShown(LaserTimeline.ToolboxAt(commands,cursor,selected));
        SetLaserHidden(LaserTimeline.HiddenAt(commands,cursor,selected));
    }
    /// <summary>Hide or show the optional shoulder laser (no-op for models without one).</summary>
    internal void SetLaserHidden(bool hidden)
    {
        if (_scene == null || !_scene.HasLaser || _scene.LaserHidden == hidden) return;
        _scene.SetLaserHidden(hidden);
    }

    /// <summary>Show the toolbox (true) or the laser (false) on the laser's hinge (no-op without a toolbox).</summary>
    internal void SetToolboxShown(bool shown)
    {
        if (_scene == null || !_scene.HasToolbox || _scene.ToolboxShown == shown) return;
        _scene.SetToolboxShown(shown);
    }

    internal bool HasLaser => _scene?.HasLaser == true;
    internal bool LaserHidden => _scene?.LaserHidden == true;
    internal bool ToolboxShown => _scene?.ToolboxShown == true;
    internal bool LaserReadyToFire => _scene?.LaserReadyToFire == true;
}
