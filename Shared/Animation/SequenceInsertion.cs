namespace ServoAnimator;

public enum SequenceOverlapChoice { KeepOriginal, UseInserted, Merge }
public readonly record struct SequenceControlKey(ServoNames Servo,RobotControls? Control);
public sealed record SequenceOverlap(SequenceControlKey Key,int ExistingCount,int InsertedCount);
public sealed record SequenceInsertionResult(List<ServoCommand> Existing,List<ServoCommand> Inserted)
{ public List<ServoCommand> Commands=>Existing.Concat(Inserted).OrderBy(c=>c.OffsetSeconds).ToList(); }

/// <summary>A side-effect-free insertion plan. Only commands inside the inclusive insertion span can be removed.</summary>
public sealed class SequenceInsertion
{
    private sealed record Entry(ServoCommand Original,ServoCommand[] Parts);
    private readonly Entry[] original,incoming;
    public double Start {get;} public double End {get;}
    public string GroupId {get;}=Guid.NewGuid().ToString("N");
    public IReadOnlyList<SequenceOverlap> Overlaps {get;}
    private static SequenceControlKey Key(ServoCommand c)=>new(c.Servo,c.Control);
    public SequenceInsertion(IEnumerable<ServoCommand> existing,IEnumerable<ServoCommand> inserted,double start,double duration,Func<ServoCommand,IEnumerable<ServoCommand>> expand=null)
    {
        var source=inserted.Select(c=>c.Clone()).ToArray();
        if(!double.IsFinite(start)||start<0||!double.IsFinite(duration)||duration<0||source.Any(c=>!double.IsFinite(c.OffsetSeconds)||c.OffsetSeconds<0))throw new ArgumentException("Invalid sequence insertion time.");
        Start=ServoCommand.TimeKey(start);End=ServoCommand.TimeKey(Start+Math.Max(duration,source.Select(c=>c.OffsetSeconds).DefaultIfEmpty(0).Max()));
        Entry Make(ServoCommand c)=>new(c,(expand?.Invoke(c)??new[]{c}).Select(p=>p.Clone()).ToArray());
        original=existing.Select(c=>Make(c.Clone())).ToArray();
        foreach(var c in source){c.OffsetSeconds=ServoCommand.TimeKey(Start+c.OffsetSeconds);SetGroup(c);}
        incoming=source.Select(Make).ToArray();
        var old=original.Where(e=>Within(e.Original)).SelectMany(e=>e.Parts.Select(Key).Distinct().Select(k=>(Key:k,Entry:e))).GroupBy(p=>p.Key).ToDictionary(g=>g.Key,g=>g.Count());
        Overlaps=incoming.SelectMany(e=>e.Parts.Select(Key).Distinct()).GroupBy(k=>k).Where(g=>old.ContainsKey(g.Key))
            .Select(g=>new SequenceOverlap(g.Key,old[g.Key],g.Count())).OrderBy(g=>g.Key.Servo.ToString()).ThenBy(g=>g.Key.Control).ToArray();
    }
    private void SetGroup(ServoCommand command){command.GroupId=GroupId;if(command.AudioTrim!=null)foreach(var c in command.AudioTrim.HiddenCommands)SetGroup(c);}
    private bool Within(ServoCommand c)=>ServoCommand.TimeKey(c.OffsetSeconds)>=Start&&ServoCommand.TimeKey(c.OffsetSeconds)<=End;
    public SequenceInsertionResult Resolve(IReadOnlyDictionary<SequenceControlKey,SequenceOverlapChoice> choices)
    {
        if(Overlaps.Any(o=>!choices.TryGetValue(o.Key,out var choice)||!Enum.IsDefined(choice)))throw new ArgumentException("Choose how to handle every overlapping control.");
        var times=incoming.SelectMany(e=>e.Parts).GroupBy(Key).ToDictionary(g=>g.Key,g=>g.Select(c=>ServoCommand.TimeKey(c.OffsetSeconds)).ToHashSet());
        SequenceOverlapChoice Choice(SequenceControlKey k)=>choices.TryGetValue(k,out var c)?c:SequenceOverlapChoice.Merge;
        List<ServoCommand> Filter(Entry[] entries,bool isNew)
        {
            var output=new List<ServoCommand>();
            foreach(var e in entries)
            {
                if(!isNew&&!Within(e.Original)){output.Add(e.Original.Clone());continue;}
                var kept=e.Parts.Where(c=>isNew?Choice(Key(c))!=SequenceOverlapChoice.KeepOriginal:
                    !times.ContainsKey(Key(c))||Choice(Key(c))==SequenceOverlapChoice.KeepOriginal||
                    (Choice(Key(c))==SequenceOverlapChoice.Merge&&!times[Key(c)].Contains(ServoCommand.TimeKey(c.OffsetSeconds)))).ToArray();
                if(kept.Length==e.Parts.Length)output.Add(e.Original.Clone());
                else output.AddRange(kept.Select(c=>c.Clone()));
            }
            return output;
        }
        return new(Filter(original,false),Filter(incoming,true));
    }
}
