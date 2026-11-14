using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;

namespace ServoAnimator;

public enum BeamDirection { In, Out }

/// <summary>URDF Beam: a TNG-style transporter effect for the URDF robot. "In" materializes the robot from a
/// sparkling column; "Out" dematerializes it. Optional particles fall from the top of the view (in) or rise to it
/// (out). The sound plays from the command time; RGB sends a matching BEAM sparkle to the robot's LED rings.</summary>
public sealed class BeamCommand
{
    internal const string DefaultSound = "default", NoSound = "none";
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() }
    };
    public BeamDirection Direction { get; set; } = BeamDirection.In;
    public double Seconds { get; set; } = 3.5;
    /// <summary>Particles fall from the top of the view when beaming in and rise to it when beaming out.</summary>
    public bool Particles { get; set; } = true;
    /// <summary>"default" (synthesized), "none", or an audio file relative to Configuration (or absolute).</summary>
    public string Sound { get; set; } = DefaultSound;
    public double Volume { get; set; } = 100;
    /// <summary>Also sparkle the physical robot's RGB eye/vent rings (BEAM effect in ArduinoOpenSaintRGB).</summary>
    public bool Rgb { get; set; } = true;

    public static BeamCommand Read(string text)
    {
        var c = string.IsNullOrWhiteSpace(text) ? new BeamCommand()
            : JsonSerializer.Deserialize<BeamCommand>(text, Options) ?? throw new InvalidDataException("URDF Beam needs a settings object.");
        c.Validate(); return c;
    }
    public void Validate()
    {
        if (!double.IsFinite(Seconds) || Seconds < .5 || Seconds > 20) throw new InvalidDataException("URDF Beam seconds must be between 0.5 and 20.");
        if (!double.IsFinite(Volume) || Volume < 0 || Volume > 100) throw new InvalidDataException("URDF Beam volume must be between 0 and 100.");
        if (string.IsNullOrWhiteSpace(Sound)) throw new InvalidDataException("URDF Beam sound must be default, none or an audio file.");
    }
    public string ToJson() => JsonSerializer.Serialize(this, Options);
    internal string Label() => Direction == BeamDirection.In ? "Beam in" : "Beam out";

    /// <summary>The audio file to play, or null for none.</summary>
    internal string SoundFile(string configRoot)
    {
        if (string.Equals(Sound, NoSound, StringComparison.OrdinalIgnoreCase)) return null;
        string relative = string.Equals(Sound, DefaultSound, StringComparison.OrdinalIgnoreCase)
            ? Path.Combine("Library", "Audio", Direction == BeamDirection.In ? "Beam_In.wav" : "Beam_Out.wav") : Sound;
        return Path.IsPathRooted(relative) ? relative : Path.GetFullPath(Path.Combine(configRoot ?? "", relative));
    }

    /// <summary>The matching RGB ring command (TNG blue-white sparkle on eyes and vents), or null.</summary>
    internal string RgbText() => Rgb
        ? $"Beam,140,200,255,200,Both,LR,{(int)Math.Round(Seconds * 1000)},{(Direction == BeamDirection.In ? "In" : "Out")}" : null;
}

/// <summary>What the beam effect shows at one timeline instant.</summary>
internal sealed record BeamState(bool Active, bool Hidden, BeamDirection Direction, double Progress, BeamCommand Settings, double StartSeconds)
{
    internal static readonly BeamState Visible = new(false, false, BeamDirection.In, 1, null, 0);

    /// <summary>Progress expressed as "how far materialized": 0 = gone, 1 = solid, for either direction.</summary>
    internal double Materialized => Direction == BeamDirection.In ? Progress : 1 - Progress;
    internal double RobotOpacity => Active ? BeamTimeline.Ramp(Materialized, .25, .85) : Hidden ? 0 : 1;
    internal double Glow => Active ? BeamTimeline.Ramp(Materialized, .15, .45) * (1 - BeamTimeline.Ramp(Materialized, .7, 1)) : 0;
    internal double Sparkle => Active ? BeamTimeline.Ramp(Materialized, .05, .3) * (1 - BeamTimeline.Ramp(Materialized, .75, 1)) : 0;
    /// <summary>0..1 position of the falling (in) or rising (out) particles, or -1 when none are on screen.</summary>
    internal double ParticleTravel => !Active || Settings?.Particles != true ? -1
        : Direction == BeamDirection.In ? (Progress <= .45 ? Progress / .45 : -1)
        : (Progress >= .55 ? (Progress - .55) / .45 : -1);
}

internal static class BeamTimeline
{
    internal static double Ramp(double x, double from, double to)
    {
        double t = Math.Clamp((x - from) / (to - from), 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>Stateless evaluation. No beam commands: the robot is visible. Before the first beam command the robot
    /// is hidden if that command beams in. Otherwise the latest enabled beam at or before the cursor decides: while it
    /// runs the effect is active; afterwards the robot is visible (in) or hidden (out). Invalid settings are skipped.</summary>
    internal static BeamState StateAt(IEnumerable<ServoCommand> commands, double cursor)
    {
        var beams = new List<(double Time, BeamCommand Settings)>();
        foreach (var c in commands.Where(c => c.Servo == ServoNames.UrdfBeam && !c.Disable).OrderBy(c => c.OffsetSeconds))
        {
            try { beams.Add((c.OffsetSeconds, BeamCommand.Read(c.TextValue))); } catch (Exception) { }
        }
        if (beams.Count == 0) return BeamState.Visible;
        var latest = beams.LastOrDefault(b => b.Time <= cursor + 1e-9);
        if (latest.Settings == null)
            return beams[0].Settings.Direction == BeamDirection.In ? new BeamState(false, true, BeamDirection.In, 0, beams[0].Settings, beams[0].Time) : BeamState.Visible;
        double progress = (cursor - latest.Time) / latest.Settings.Seconds;
        if (progress < 1) return new BeamState(true, false, latest.Settings.Direction, Math.Max(0, progress), latest.Settings, latest.Time);
        return latest.Settings.Direction == BeamDirection.In ? BeamState.Visible with { Settings = latest.Settings, StartSeconds = latest.Time }
            : new BeamState(false, true, BeamDirection.Out, 1, latest.Settings, latest.Time);
    }

    /// <summary>Deterministic sparkle field for a column: positions as fractions of the column (0..1) with a twinkle
    /// brightness 0..1, for a given time. Same inputs always give the same sparkles.</summary>
    internal static IEnumerable<(double X, double Y, double Size, double Brightness)> Sparkles(int count, double seconds)
    {
        for (int i = 0; i < count; i++)
        {
            double a = Hash(i * 3 + 1), b = Hash(i * 3 + 2), c = Hash(i * 3 + 3);
            double speed = .3 + .5 * b;                                        // slow upward drift, wraps
            double y = (c + seconds * speed * .15) % 1.0;
            double twinkle = .5 + .5 * Math.Sin(seconds * (6 + 10 * a) + i);   // per-sparkle flicker
            yield return (a, y, .4 + .8 * c, Math.Max(0, twinkle));
        }
    }

    /// <summary>Particles travelling between the top of the view and the column: start and target as fractions of
    /// the view, staggered so they arrive over the travel window. Travel 0 = at the top (in) / in the column (out).</summary>
    internal static IEnumerable<(Point Position, double Brightness)> Stream(int count, double travel, BeamDirection direction, Rect column, Size view)
    {
        if (travel < 0 || view.Width <= 0 || view.Height <= 0) yield break;
        for (int i = 0; i < count; i++)
        {
            double delay = Hash(i * 5 + 11) * .45;                            // staggered departures
            double t = Math.Clamp((travel - delay) / (1 - .45), 0, 1);
            double ease = t * t * (3 - 2 * t);
            var top = new Point(column.X + column.Width * Hash(i * 5 + 12) + (Hash(i * 5 + 13) - .5) * view.Width * .25, -12);
            var inColumn = new Point(column.X + column.Width * Hash(i * 5 + 14), column.Y + column.Height * Hash(i * 5 + 15));
            var (from, to) = direction == BeamDirection.In ? (top, inColumn) : (inColumn, top);
            if (direction == BeamDirection.In ? t <= 0 : t >= 1) continue;     // not yet departed / already gone
            var p = new Point(from.X + (to.X - from.X) * ease, from.Y + (to.Y - from.Y) * ease);
            yield return (p, direction == BeamDirection.In ? .4 + .6 * ease : 1 - .6 * ease);
        }
    }

    internal static double Hash(int n)
    {
        unchecked
        {
            uint x = (uint)n * 0x9E3779B1u; x ^= x >> 15; x *= 0x85EBCA77u; x ^= x >> 13; x *= 0xC2B2AE3Du; x ^= x >> 16;
            return (x & 0xFFFFFF) / (double)0x1000000;
        }
    }
}
