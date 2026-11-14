using System.IO;
using System.Text.Json;

namespace ServoAnimator;

internal sealed class HeadroomSegment
{
    public string Kind { get; set; }
    public required double OriginalStart { get; set; }
    public required double OriginalEnd { get; set; }
    public required double OutputStart { get; set; }
    public required double OutputEnd { get; set; }
}
internal sealed class HeadroomRepeat
{
    public int Index { get; set; }
    public required double OutputStart { get; set; }
    public required double OutputEnd { get; set; }
    public required double Duration { get; set; }
    public double PitchSemitones { get; set; }
}
internal sealed class HeadroomEvent
{
    public int Id { get; set; }
    public string Type { get; set; }
    public string Word { get; set; }
    public required double OriginalStart { get; set; }
    public required double OriginalEnd { get; set; }
    public required double OutputStart { get; set; }
    public required double OutputEnd { get; set; }
    public double AddedSeconds { get; set; }
    public int Repeats { get; set; }
    public double SliceSeconds { get; set; }
    public double PitchStepSemitones { get; set; }
    public List<HeadroomRepeat> RepeatList { get; set; } = new();
    public double Factor { get; set; } = 1;
    public double SlowestSpeed { get; set; } = 1;
    public double Semitones { get; set; }
}
internal sealed class HeadroomWord
{
    public double AlignConfidence { get; set; } = 1;
}
internal sealed class HeadroomTimeline
{
    public string Format { get; set; }
    public int Version { get; set; }
    public int SampleRate { get; set; }
    public required double OriginalDuration { get; set; }
    public required double OutputDuration { get; set; }
    public required double TotalAddedSeconds { get; set; }
    public List<HeadroomSegment> TimeMap { get; set; } = new();
    public List<HeadroomEvent> Events { get; set; } = new();
    public List<HeadroomWord> Words { get; set; } = new();
    public string Summary => $"{OriginalDuration:F3}s → {OutputDuration:F3}s; {TotalAddedSeconds:+0.###;-0.###;0}s added. " +
        string.Join(", ", Events.GroupBy(e => e.Type).Select(g => $"{g.Count()} {g.Key}"));
    public static HeadroomTimeline Load(string path)
    {
        if (new FileInfo(path).Length > 16 * 1024 * 1024) throw new InvalidDataException("Command Update exceeds 16 MiB.");
        var value = JsonSerializer.Deserialize<HeadroomTimeline>(File.ReadAllText(path), new JsonSerializerOptions
            { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Empty Command Update JSON.");
        value.Validate(); return value;
    }
    private static bool Near(double a, double b) => Math.Abs(a-b) <= .00002;
    public void Validate()
    {
        void Require(bool ok, string message) { if (!ok) throw new InvalidDataException(message); }
        Require(Format == "johnny5-headroom-timeline" && Version == 1, "Expected johnny5-headroom-timeline version 1. Other versions are not supported.");
        Require(SampleRate > 0 && double.IsFinite(OriginalDuration) && double.IsFinite(OutputDuration) && double.IsFinite(TotalAddedSeconds) &&
            OriginalDuration > 0 && OutputDuration > 0 && OutputDuration <= 86400 && Near(OutputDuration-OriginalDuration, TotalAddedSeconds), "Invalid audio durations, sample rate, or added time.");
        Require(TimeMap != null && TimeMap.Count > 0 && TimeMap.Count <= 100000 && Events != null && Events.Count <= 10000 && Words != null, "Missing time_map, events or words.");
        double original = 0, output = 0;
        foreach (var s in TimeMap)
        {
            Require(s != null && new[] { s.OriginalStart,s.OriginalEnd,s.OutputStart,s.OutputEnd }.All(double.IsFinite), "Non-finite time_map value.");
            Require(s.Kind is "original" or "inserted" or "stretch" or "tape_stop", "Unknown time_map kind.");
            Require(Near(s.OriginalStart, original) && Near(s.OutputStart, output) && s.OutputEnd > s.OutputStart, "time_map must be ordered, contiguous, and cover both audio files.");
            Require(s.Kind == "inserted" ? Near(s.OriginalStart,s.OriginalEnd) : s.OriginalEnd > s.OriginalStart, "Invalid original span in time_map.");
            Require(s.Kind != "original" || Near(s.OriginalEnd-s.OriginalStart,s.OutputEnd-s.OutputStart), "An original segment must preserve duration.");
            // Normalize sub-sample rounding seams so Map never falls through a tiny source gap.
            s.OriginalStart=original; s.OutputStart=output;
            if(s.Kind=="inserted") s.OriginalEnd=original;
            original=s.OriginalEnd; output=s.OutputEnd;
        }
        Require(Near(original,OriginalDuration) && Near(output,OutputDuration), "time_map does not cover the declared durations.");
        TimeMap[^1].OutputEnd=OutputDuration;
        if(TimeMap[^1].Kind!="inserted") TimeMap[^1].OriginalEnd=OriginalDuration;
        double previous = -1; var ids = new HashSet<int>();
        foreach (var e in Events)
        {
            Require(e != null && new[] {e.OriginalStart,e.OriginalEnd,e.OutputStart,e.OutputEnd,e.AddedSeconds,e.Factor,e.SlowestSpeed,e.Semitones,e.PitchStepSemitones,e.SliceSeconds}.All(double.IsFinite), "Invalid event number.");
            Require(e.Id > 0 && ids.Add(e.Id) && (e.Type is "stutter" or "stretch" or "tape_stop" or "pitch_jump"), "Invalid event ID or effect type.");
            Require(e.OutputStart >= previous && e.OutputStart >= 0 && e.OutputEnd > e.OutputStart && e.OutputEnd <= OutputDuration + .00002 &&
                e.OriginalStart >= 0 && e.OriginalEnd > e.OriginalStart && e.OriginalEnd <= OriginalDuration + .00002, "Event spans must be ordered and inside the audio.");
            previous = e.OutputStart;
            if (e.Type == "stretch") Require(e.Factor > 0, "Stretch factor must be positive.");
            if (e.Type == "tape_stop") Require(e.SlowestSpeed >= 0 && e.SlowestSpeed <= 1, "Tape-stop speed must be 0–1.");
            if (e.Type != "stutter") continue;
            Require(e.RepeatList != null && e.Repeats > 0 && e.RepeatList.Count == e.Repeats && e.Repeats <= 10000, "Invalid stutter repeat count.");
            output=e.OutputStart;
            foreach (var r in e.RepeatList)
            {
                Require(r != null && new[] {r.OutputStart,r.OutputEnd,r.Duration,r.PitchSemitones}.All(double.IsFinite) && Near(r.OutputStart,output) && r.Duration > 0 && Near(r.OutputEnd-r.OutputStart,r.Duration), "Stutter repeat windows must be contiguous with matching positive durations.");
                output=r.OutputEnd;
            }
            Require(Near(output,e.OutputEnd), "Stutter repeats must cover the event.");
            Require(TimeMap.Any(s=>s.Kind=="inserted" && Near(s.OriginalStart,e.OriginalStart) &&
                Near(s.OutputStart,e.OutputStart) && Near(s.OutputEnd,e.OutputEnd)), "Stutter event does not match an inserted time_map segment.");
        }
        Require(Words.All(w => w != null && double.IsFinite(w.AlignConfidence) && w.AlignConfidence >= 0 && w.AlignConfidence <= 1), "Invalid word alignment confidence.");
    }
    // Half-open source spans deliberately skip inserted time, including at its boundary.
    public double Map(double time)
    {
        if (time < 0) return time;
        if (time >= OriginalDuration) return OutputDuration + time - OriginalDuration;
        foreach (var s in TimeMap)
            if (s.OriginalEnd > s.OriginalStart && time >= s.OriginalStart && time < s.OriginalEnd)
                return s.OutputStart + (time-s.OriginalStart)/(s.OriginalEnd-s.OriginalStart)*(s.OutputEnd-s.OutputStart);
        var last=TimeMap[^1]; return last.OutputEnd + time-last.OriginalEnd;
    }
    public double SourceAt(double output)
    {
        foreach (var s in TimeMap)
            if (output >= s.OutputStart && output < s.OutputEnd)
                return s.OriginalStart + (output-s.OutputStart)/(s.OutputEnd-s.OutputStart)*(s.OriginalEnd-s.OriginalStart);
        return OriginalDuration + output-OutputDuration;
    }
}
