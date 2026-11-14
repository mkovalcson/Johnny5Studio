namespace ServoAnimator;

/// <summary>Source-file trim bounds. Hidden commands are retained here, outside the playable list.</summary>
public sealed class AudioTrimSettings
{
    public double SkipSeconds { get; set; }
    public double? EndSeconds { get; set; }
    public bool SkipCommitted { get; set; }
    public bool EndCommitted { get; set; }
    public bool ScrubBefore { get; set; }
    public bool ScrubAfter { get; set; }
    public bool CommandsLinked { get; set; }
    public List<ServoCommand> HiddenCommands { get; set; } = new();
    public AudioTrimSettings Clone() => new()
    {
        SkipSeconds = SkipSeconds,
        EndSeconds = EndSeconds,
        SkipCommitted = SkipCommitted,
        EndCommitted = EndCommitted,
        ScrubBefore = ScrubBefore,
        ScrubAfter = ScrubAfter,
        CommandsLinked = CommandsLinked,
        HiddenCommands = HiddenCommands.Select(c => c.Clone()).ToList()
    };
    [System.Text.Json.Serialization.JsonIgnore]
    public double Start => SkipCommitted && double.IsFinite(SkipSeconds) ? Math.Max(0, SkipSeconds) : 0;
    public bool SameAs(AudioTrimSettings other) => other != null &&
        SkipSeconds == other.SkipSeconds && EndSeconds == other.EndSeconds &&
        SkipCommitted == other.SkipCommitted && EndCommitted == other.EndCommitted &&
        ScrubBefore == other.ScrubBefore && ScrubAfter == other.ScrubAfter && CommandsLinked == other.CommandsLinked &&
        HiddenCommands.Count == other.HiddenCommands.Count && HiddenCommands.Zip(other.HiddenCommands).All(p => CommandConflicts.SameContent(p.First, p.Second));
    public double End(double duration) => EndCommitted && EndSeconds is double end && double.IsFinite(end)
        ? Math.Clamp(end, Start, Math.Max(Start, duration)) : Math.Max(Start, duration);

    public static void Apply(AnimationDocument document, ServoCommand audio, AudioTrimSettings next)
    {
        var prior = audio.AudioTrim ?? new AudioTrimSettings();
        if (string.IsNullOrEmpty(audio.AudioTakeId)) audio.AudioTakeId = Guid.NewGuid().ToString("N");
        // Older recordings did not carry take identity. Adopt only unassigned non-audio commands
        // between this handle and the next; other recordings keep their own identity.
        if (!prior.CommandsLinked && (next.ScrubBefore || next.ScrubAfter))
        {
            double end = document.Commands.Where(c => c.Servo == ServoNames.Play && c.OffsetSeconds > audio.OffsetSeconds)
                .Select(c => c.OffsetSeconds).DefaultIfEmpty(double.PositiveInfinity).Min();
            foreach (var c in document.Commands.Where(c => c.Servo != ServoNames.Play && string.IsNullOrEmpty(c.AudioTakeId) &&
                         c.OffsetSeconds >= audio.OffsetSeconds && c.OffsetSeconds < end)) c.AudioTakeId = audio.AudioTakeId;
            next.CommandsLinked = true;
        }
        var visible = document.Commands.Where(c => c != audio && c.AudioTakeId == audio.AudioTakeId).ToList();
        double previousShift = prior.ScrubBefore ? prior.Start : 0;
        // Capture edits to visible commands in source time before reapplying a changed trim.
        foreach (var c in visible) c.AudioSourceSeconds = c.OffsetSeconds - audio.OffsetSeconds + previousShift;
        document.Commands.RemoveAll(visible.Contains);
        var candidates = visible.Concat(prior.HiddenCommands).ToList();
        next.HiddenCommands = new();
        foreach (var c in candidates)
        {
            double source = c.AudioSourceSeconds ?? c.OffsetSeconds - audio.OffsetSeconds;
            c.AudioSourceSeconds = source;
            bool hidden = (next.ScrubBefore && source < next.Start - 1e-9) ||
                (next.ScrubAfter && next.EndCommitted && next.EndSeconds.HasValue && source > next.EndSeconds.Value + 1e-9);
            if (hidden) next.HiddenCommands.Add(c);
            else
            {
                c.OffsetSeconds = ServoCommand.TimeKey(Math.Max(0, audio.OffsetSeconds + source - (next.ScrubBefore ? next.Start : 0)));
                document.Commands.Add(c);
            }
        }
        audio.AudioTrim = next;
    }

    public static void RemapCopiedTakes(IEnumerable<ServoCommand> commands)
    {
        var copies = commands.ToList();
        var ids = copies.Where(c => c.Servo == ServoNames.Play && !string.IsNullOrEmpty(c.AudioTakeId))
            .Select(c => c.AudioTakeId).Distinct().ToDictionary(id => id, _ => Guid.NewGuid().ToString("N"));
        foreach (var command in copies.Concat(copies.Where(c => c.AudioTrim != null).SelectMany(c => c.AudioTrim.HiddenCommands)))
        {
            if (string.IsNullOrEmpty(command.AudioTakeId)) continue;
            command.AudioTakeId = ids.GetValueOrDefault(command.AudioTakeId) ?? "";
            if (command.AudioTakeId.Length == 0) command.AudioSourceSeconds = null;
        }
    }
}
