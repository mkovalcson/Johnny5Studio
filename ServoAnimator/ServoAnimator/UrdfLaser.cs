using System.IO;
using System.Text.Json;
using System.Windows.Media.Media3D;

namespace ServoAnimator;

/// <summary>Hide Laser: hides (true) or shows (false) the optional shoulder laser assembly, every URDF
/// link named body_laser_* (support, laser and toolbox). Without any Hide Laser command it is shown.</summary>
public sealed class HideLaserCommand
{
    /// <summary>True hides the laser; false shows it.</summary>
    public bool Hide { get; set; } = true;
    public static HideLaserCommand Read(string text) => TvJson.Read<HideLaserCommand>(text);
    public string ToJson() => JsonSerializer.Serialize(this, TvJson.Options);
    internal string Label() => "Hide Laser = " + (Hide ? "true" : "false");
}

/// <summary>Toolbox Swap: on the laser's hinge, show the red toolbox (true) or the laser (false).
/// He normally carries the toolbox, so without any Toolbox Swap command the toolbox shows.</summary>
public sealed class ToolboxSwapCommand
{
    /// <summary>True shows the toolbox in place of the laser; false shows the laser.</summary>
    public bool Swap { get; set; } = true;
    public static ToolboxSwapCommand Read(string text) => TvJson.Read<ToolboxSwapCommand>(text);
    public string ToJson() => JsonSerializer.Serialize(this, TvJson.Options);
    internal string Label() => "Toolbox Swap = " + (Swap ? "true" : "false");
}

internal static class LaserTimeline
{
    /// <summary>Whether the laser is hidden at the cursor: the latest enabled Hide Laser at or
    /// before it decides; with none, the laser shows.</summary>
    internal static bool HiddenAt(IEnumerable<ServoCommand> commands, double cursor, UrdfAccessory fallback = UrdfAccessory.Toolbox)
    {
        var command = Latest(commands, ServoNames.HideLaser, cursor);
        if (command == null) return Latest(commands, ServoNames.ToolboxSwap, cursor) == null && fallback == UrdfAccessory.None;
        try { return HideLaserCommand.Read(command.TextValue).Hide; } catch { return false; }
    }

    /// <summary>Whether the toolbox replaces the laser at the cursor: the latest enabled Toolbox Swap
    /// at or before it decides; with none, the toolbox shows.</summary>
    internal static bool ToolboxAt(IEnumerable<ServoCommand> commands, double cursor, UrdfAccessory fallback = UrdfAccessory.Toolbox)
    {
        var command = Latest(commands, ServoNames.ToolboxSwap, cursor);
        if (command == null) return fallback == UrdfAccessory.Toolbox;
        try { return ToolboxSwapCommand.Read(command.TextValue).Swap; } catch { return true; }
    }

    private static ServoCommand Latest(IEnumerable<ServoCommand> commands, ServoNames servo, double cursor) =>
        commands.Where(c => c.Servo == servo && !c.Disable && c.OffsetSeconds <= cursor + 1e-9).OrderBy(c => c.OffsetSeconds).LastOrDefault();
}

internal sealed partial class UrdfScene
{
    /// <summary>Every link of the optional shoulder laser assembly starts with this.</summary>
    internal const string LaserPrefix = "body_laser_";
    internal const string LaserHeadLink = "body_laser_head_link";
    internal const string ToolboxLink = "body_laser_toolbox_link";
    internal bool HasLaser => _links.Keys.Any(IsLaserLink);
    internal bool HasToolbox => _links.ContainsKey(ToolboxLink);
    internal bool LaserHidden { get; private set; }
    /// <summary>The toolbox shows in place of the laser (the default).</summary>
    internal bool ToolboxShown { get; private set; } = true;
    private static bool IsLaserLink(string link) => link.StartsWith(LaserPrefix, StringComparison.Ordinal);

    /// <summary>Hidden laser parts are detached from the scene, so they cost nothing to draw; their
    /// joints keep moving, so showing them again puts them back in place.</summary>
    internal void SetLaserHidden(bool hidden) { LaserHidden = hidden; ApplyLaserVisibility(); }

    /// <summary>Show the toolbox (true) or the laser (false) on the laser's hinge.</summary>
    internal void SetToolboxShown(bool shown) { ToolboxShown = shown; ApplyLaserVisibility(); }

    private bool LaserLinkVisible(string link) => !LaserHidden
        && !(link == LaserHeadLink && ToolboxShown && HasToolbox) && !(link == ToolboxLink && !ToolboxShown);

    private void ApplyLaserVisibility()
    {
        foreach (var (name, group) in _links.Where(p => IsLaserLink(p.Key)))
        {
            if (!_linkVisuals.TryGetValue(name, out var visuals)) continue;
            bool visible = LaserLinkVisible(name);
            foreach (var visual in visuals)
            {
                if (!visible) group.Children.Remove(visual);
                else if (!group.Children.Contains(visual)) group.Children.Add(visual);
            }
        }
    }

    /// <summary>Called once the model is built, so only one of laser and toolbox shows from the start.</summary>
    private void InitializeLaser() { if (HasLaser) ApplyLaserVisibility(); }

    internal bool LaserVisualsAttached(string link = null) => _links.Where(p => link == null ? IsLaserLink(p.Key) : p.Key == link)
        .Any(p => _linkVisuals.TryGetValue(p.Key, out var visuals) && visuals.Any(v => p.Value.Children.Contains(v)));
}
