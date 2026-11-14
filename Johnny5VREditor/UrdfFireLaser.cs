using System.IO;
using System.Text.Json;
using System.Windows.Media.Media3D;

namespace ServoAnimator;

/// <summary>Fire Laser: a burst of glowing bolts from the shoulder laser's firing window, flying straight out
/// along the laser into the URDF scene. Display-only; nothing fires unless the laser itself is showing
/// (Hide Laser false and Toolbox Swap false).</summary>
public sealed class FireLaserCommand
{
    internal const string DefaultSound = "default", MediumSound = "medium", HeavySound = "heavy", NoSound = "none", DefaultColor = "#FF2A2A";
    /// <summary>The synthesized sounds in Configuration/Library/Audio, by their Sound setting.</summary>
    internal static readonly IReadOnlyDictionary<string, string> BuiltInSounds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [DefaultSound] = "Laser_Fire.wav", [MediumSound] = "Laser_Medium.wav", [HeavySound] = "Laser_Heavy.wav",
    };
    /// <summary>Bolts in the burst.</summary>
    public int Shots { get; set; } = 3;
    /// <summary>Seconds between bolts.</summary>
    public double Interval { get; set; } = .25;
    /// <summary>Bolt speed in metres per second.</summary>
    public double Speed { get; set; } = 15;
    /// <summary>How far a bolt flies, in metres, before it is gone.</summary>
    public double Range { get; set; } = 10;
    /// <summary>Bolt length in metres.</summary>
    public double Length { get; set; } = .4;
    /// <summary>Bolt colour as #RRGGBB.</summary>
    public string Color { get; set; } = DefaultColor;
    /// <summary>"default" (synthesized "pew"), "medium" (between the two), "heavy" (synthesized deep, guttural blast),
    /// "none", or an audio file relative to Configuration (or absolute); plays with each bolt.</summary>
    public string Sound { get; set; } = DefaultSound;
    public double Volume { get; set; } = 100;

    public static FireLaserCommand Read(string text) { var c = TvJson.Read<FireLaserCommand>(text); c.Validate(); return c; }
    public string ToJson() => JsonSerializer.Serialize(this, TvJson.Options);

    public void Validate()
    {
        if (Shots is < 1 or > 20) throw new InvalidDataException("Fire Laser shots must be between 1 and 20.");
        if (!double.IsFinite(Interval) || Interval < .05 || Interval > 2) throw new InvalidDataException("Fire Laser interval must be between 0.05 and 2 seconds.");
        if (!double.IsFinite(Speed) || Speed < 1 || Speed > 60) throw new InvalidDataException("Fire Laser speed must be between 1 and 60 metres per second.");
        if (!double.IsFinite(Range) || Range < .5 || Range > 30) throw new InvalidDataException("Fire Laser range must be between 0.5 and 30 metres.");
        if (!double.IsFinite(Length) || Length < .05 || Length > 2) throw new InvalidDataException("Fire Laser bolt length must be between 0.05 and 2 metres.");
        if (!LcdShowCommand.TryParseColor(Color, out _)) throw new InvalidDataException("Fire Laser colour must look like #FF2A2A.");
        if (!double.IsFinite(Volume) || Volume < 0 || Volume > 100) throw new InvalidDataException("Fire Laser volume must be between 0 and 100.");
        if (string.IsNullOrWhiteSpace(Sound)) throw new InvalidDataException("Fire Laser sound must be default, none or an audio file.");
    }

    internal string Label() => Shots == 1 ? "Fire Laser" : $"Fire Laser x{Shots}";
    /// <summary>Seconds from the command until the last bolt is gone.</summary>
    internal double Duration => (Shots - 1) * Interval + Range / Speed;

    /// <summary>The audio file to play with each bolt, or null for none.</summary>
    internal string SoundFile(string configRoot)
    {
        if (string.Equals(Sound, NoSound, StringComparison.OrdinalIgnoreCase)) return null;
        string relative = BuiltInSounds.TryGetValue(Sound, out var file) ? Path.Combine("Library", "Audio", file) : Sound;
        return Path.IsPathRooted(relative) ? relative : Path.GetFullPath(Path.Combine(configRoot ?? "", relative));
    }
}

/// <summary>One bolt in flight: from command <see cref="CommandSeconds"/>, bolt <see cref="Index"/>, fired
/// <see cref="Age"/> seconds ago.</summary>
internal readonly record struct LaserShot(double CommandSeconds, int Index, double Age, FireLaserCommand Settings)
{
    internal double Start => CommandSeconds + Index * Settings.Interval;
    /// <summary>Distance of the bolt's front and tail from the firing window, in metres.</summary>
    internal double Front => Math.Min(Age * Settings.Speed, Settings.Range);
    internal double Tail => Math.Max(0, Front - Settings.Length);
}

internal static class FireLaserTimeline
{
    /// <summary>Stateless: every bolt in flight at the cursor, from all enabled Fire Laser commands (bursts may
    /// overlap). Invalid settings are skipped.</summary>
    internal static List<LaserShot> ShotsAt(IEnumerable<ServoCommand> commands, double cursor)
    {
        var shots = new List<LaserShot>();
        foreach (var c in commands.Where(c => c.Servo == ServoNames.FireLaser && !c.Disable && c.OffsetSeconds <= cursor + 1e-9))
        {
            FireLaserCommand settings;
            try { settings = FireLaserCommand.Read(c.TextValue); } catch (Exception) { continue; }
            if (cursor - c.OffsetSeconds >= settings.Duration) continue;
            for (int i = 0; i < settings.Shots; i++)
            {
                double age = cursor - (c.OffsetSeconds + i * settings.Interval);
                if (age >= -1e-9 && age < settings.Range / settings.Speed) shots.Add(new LaserShot(c.OffsetSeconds, i, Math.Max(0, age), settings));
            }
        }
        return shots;
    }

    /// <summary>The most recently fired bolt at the cursor (its sound plays), or null.</summary>
    internal static LaserShot? LatestShot(IEnumerable<ServoCommand> commands, double cursor) =>
        ShotsAt(commands, cursor).OrderBy(s => s.Start).Cast<LaserShot?>().LastOrDefault();
}

internal sealed partial class UrdfScene
{
    internal const string FiringWindowVisual = "body_laser_firing_window";
    /// <summary>The laser itself is showing (not hidden, not swapped for the toolbox), so it can fire.</summary>
    internal bool LaserReadyToFire => HasLaser && _links.ContainsKey(LaserHeadLink) && LaserLinkVisible(LaserHeadLink);

    /// <summary>The firing window's centre and the laser's pointing direction, in the robot model's coordinates
    /// (the frame of <see cref="RootModel"/>'s parent).</summary>
    internal bool TryLaserMuzzle(out Point3D muzzle, out Vector3D aim)
    {
        aim = new Vector3D(1, 0, 0);
        var local = new Point3D(.3355, 0, .0545);                       // laser_details.py: FRONT, tip centre
        if (_visuals.TryGetValue(FiringWindowVisual, out var window) && window.Geometry != null && !window.Geometry.Bounds.IsEmpty)
        {
            var b = window.Geometry.Bounds;
            local = new Point3D(b.X + b.SizeX / 2, b.Y + b.SizeY / 2, b.Z + b.SizeZ / 2);
            if (window.Transform != null) local = window.Transform.Transform(local);
        }
        if (!TryTransformLinkPoint(LaserHeadLink, local, out muzzle) || !TryTransformLinkPoint(LaserHeadLink, local + new Vector3D(1, 0, 0), out var ahead)) return false;
        aim = ahead - muzzle;
        if (aim.Length < 1e-9) return false;
        aim.Normalize();
        return true;
    }
}
