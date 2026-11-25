using System.IO;

namespace ServoAnimator;

internal sealed record HeadroomEditResult(List<ServoCommand> Commands, int Generated, List<string> Warnings);

internal static class HeadroomTimelineEditor
{
    // Editable style poses: tilt, nod, turn (degrees), horizontal viewport fraction.
    internal static readonly (double Tilt, double Nod, double Turn, double Slide)[] Poses =
        { (0,0,0,0), (-8,4,0,-.10), (8,-3,0,.10), (-5,6,-6,-.15), (5,0,6,.15) };
    private static bool Head(ServoNames s) => s is ServoNames.NeckTurn or ServoNames.NeckNodUp or ServoNames.NeckTiltRight
        or ServoNames.FlapsOpen or ServoNames.FlapTiltUp or ServoNames.IrisClose or ServoNames.EyesVerticalUp
        or ServoNames.EyesHorizontalRight or ServoNames.LeftEyePop or ServoNames.RightEyePop or ServoNames.BothEyePop;
    private static bool Neck(ServoNames s) => s is ServoNames.NeckNodUp or ServoNames.NeckTiltRight;
    private sealed class Track
    {
        public ServoCommand[] C;
        public double[] T,V,M,CT;
        public bool[] BreakAfter;
        public bool Spline;
        public Track(IEnumerable<ServoCommand> commands, bool spline)
        {
            C=commands.OrderBy(c=>c.OffsetSeconds).ToArray(); CT=C.Select(c=>c.OffsetSeconds).ToArray();
            var numeric=C.Where(c=>!c.Disable).ToArray();
            T=numeric.Select(c=>c.OffsetSeconds).ToArray(); V=numeric.Select(c=>(double)c.NumericValue).ToArray();
            BreakAfter=numeric.Select(c=>c.BreakSpline).ToArray();
            M=SplineUtil.Tangents(T,V,BreakAfter); Spline=spline;
        }
        public ServoCommand At(double time)
        {
            int i=Array.BinarySearch(CT,time); if(i<0) i=~i-1;
            if(i<0) return null;
            var c=C[i].Clone();
            if(Spline && !c.Disable && !c.IsTextServo) c.NumericValue=(int)Math.Round(SplineUtil.Eval(T,V,M,time,BreakAfter));
            c.ClampToRange(); return c;
        }
    }
    public static HeadroomEditResult Apply(AnimationDocument doc, HeadroomTimeline map, double start, ServoCommand audio,
        bool retime, bool repeatFace, bool motion, double strength, UrdfConfiguration config, ServoConfiguration servoConfig, UrdfPresentationSettings background, UrdfFraming framing, ISet<ServoNames> splineServos, int sampleHz)
    {
        map.Validate();
        var output=doc.Commands.Select(c=>c.Clone()).ToList(); var warnings=new List<string>();
        if(!retime) return new(output,0,warnings);
        if(doc.Commands.Any(c=>c.AudioTrim is { } trim &&
            (trim.SkipCommitted || trim.EndCommitted || trim.HiddenCommands.Count>0) &&
            (c.OffsetSeconds>=start || trim.HiddenCommands.Any(h=>h.OffsetSeconds>=start) ||
                !string.IsNullOrEmpty(c.AudioTakeId) && doc.Commands.Any(k=>k.AudioTakeId==c.AudioTakeId && k.OffsetSeconds>=start))))
            throw new InvalidDataException("Re-timing currently requires untrimmed audio handles from the replacement onward. Use Reset trims in their Audio timing window first, or replace audio without re-timing.");
        double Time(double t) => t < start ? t : ServoCommand.TimeKey(start+map.Map(t-start));
        for(int i=0;i<output.Count;i++)
        {
            if(ReferenceEquals(doc.Commands[i],audio)) continue; // replacement starts where its handle already was
            output[i].OffsetSeconds=Time(output[i].OffsetSeconds);
        }
        // Source-relative recording metadata must agree with the newly positioned take handles.
        var takes=output.Where(c=>c.Servo==ServoNames.Play && !string.IsNullOrEmpty(c.AudioTakeId))
            .GroupBy(c=>c.AudioTakeId).ToDictionary(g=>g.Key,g=>g.First().OffsetSeconds);
        foreach(var c in output)
            if(c.AudioSourceSeconds.HasValue && takes.TryGetValue(c.AudioTakeId,out double origin)) c.AudioSourceSeconds=c.OffsetSeconds-origin;
        var active=doc.Commands.ToArray();
        var tracks=active.Where(c=>c.Servo!=ServoNames.Play && c.Servo!=ServoNames.LoadBackdrop && c.Servo!=ServoNames.FallApart && c.Servo is not (ServoNames.TvFront or ServoNames.TvShow or ServoNames.TvReturn or ServoNames.UrdfBeam or ServoNames.LcdShow or ServoNames.HideLaser or ServoNames.ToolboxSwap or ServoNames.FireLaser or ServoNames.VideoClip or ServoNames.CameraShot))
            .GroupBy(c=>(Servo: Neck(c.Servo) ? ServoNames.NeckNodUp : c.Servo,c.Control))
            .ToDictionary(g=>g.Key,g=>new Track(g,!g.Key.Control.HasValue &&
                (splineServos.Contains(g.Key.Servo) || Neck(g.Key.Servo) && splineServos.Contains(ServoNames.NeckTiltRight))));
        int hz=Math.Clamp(sampleHz,1,120); double frame=1d/hz;
        var samples=new SortedSet<double>();
        void Span(double lo,double hi)
        {
            samples.Add(ServoCommand.TimeKey(lo)); samples.Add(ServoCommand.TimeKey(hi));
            for(double t=lo+frame;t<hi;t+=frame) { samples.Add(ServoCommand.TimeKey(t)); if(samples.Count>250000) throw new InvalidDataException("Effect spans are too long to bake in one operation."); }
        }
        foreach(var seg in map.TimeMap.Where(s=>s.Kind!="original")) Span(seg.OutputStart,seg.OutputEnd);
        if(motion && strength>0) foreach(var e in map.Events)
        {
            Span(e.OutputStart,e.OutputEnd+(e.Type == "tape_stop" ? .15 : 0));
            samples.Add(ServoCommand.TimeKey(e.OutputEnd));
            if(e.Type=="tape_stop") samples.Add(ServoCommand.TimeKey(e.OutputEnd+Math.Min(frame,.15)));
        }
        // Retain exact mapped keys in the sampled spans, including irregular recording timestamps.
        foreach(var c in output.Where(c=>c.OffsetSeconds>=start))
        {
            double t=c.OffsetSeconds-start;
            if(map.TimeMap.Any(s=>s.Kind!="original" && t>=s.OutputStart && t<=s.OutputEnd) ||
                motion && map.Events.Any(e=>t>=e.OutputStart && t<=e.OutputEnd+(e.Type == "tape_stop" ? .15 : 0))) samples.Add(t);
        }
        foreach(var e in map.Events.Where(e=>e.Type=="stutter"))
            foreach(var r in e.RepeatList)
            {
                samples.Add(ServoCommand.TimeKey(Math.Max(e.OutputStart,r.OutputStart-frame))); samples.Add(ServoCommand.TimeKey(r.OutputStart));
                if (repeatFace) foreach (var c in active.Where(c => Head(c.Servo) || c.Servo == ServoNames.RGBCommand))
                    if(c.OffsetSeconds >= start+e.OriginalStart && c.OffsetSeconds < start+e.OriginalEnd)
                        samples.Add(ServoCommand.TimeKey(r.OutputStart+(c.OffsetSeconds-start-e.OriginalStart)/(e.OriginalEnd-e.OriginalStart)*r.Duration));
            }
        var poses=map.Events.ToDictionary(e=>e.Id,e=>
        {
            var random=new Random(e.Id); var list=new List<int>(); int previous=0;
            for(int k=0;k<(e.Type=="stutter"?e.Repeats:1);k++) { int p; do p=random.Next(1,Poses.Length); while(p==previous || previous!=0 && Math.Sign(Poses[p].Tilt)==Math.Sign(Poses[previous].Tilt)); list.Add(p); previous=p; }
            return list;
        });
        var generated=new Dictionary<(double,ServoNames,RobotControls?),ServoCommand>(); int clamped=0;
        void Put(ServoCommand c,double t)
        {
            c.OffsetSeconds=ServoCommand.TimeKey(start+t); c.AudioSourceSeconds=null;
            c.Reason="Headroom: "+c.Reason;
            var range=ServoCommand.RangeFor(c.Servo); if(!c.IsTextServo && (c.NumericValue<range.Min || c.NumericValue>range.Max)) clamped++;
            c.ClampToRange(); generated[(c.OffsetSeconds,c.Servo,c.Control)]=c;
            if(generated.Count>500000) throw new InvalidDataException("This update would generate more than 500,000 commands. Split the recording into shorter sequences.");
        }
        int DegreesToValue(Func<double,double> mapping, double angle)
        {
            double lo=mapping(-100), hi=mapping(100);
            if(angle<Math.Min(lo,hi) || angle>Math.Max(lo,hi)) clamped++;
            return Inverse(mapping,angle,-100,100);
        }
        var motionServos=new[] {ServoNames.NeckTurn,ServoNames.URDFSlide,ServoNames.URDFSpin,ServoNames.B_ChestTilt,
            ServoNames.LeftEyePop,ServoNames.RightEyePop,ServoNames.IrisClose};
        foreach(double t in samples)
        {
            var inserted=map.TimeMap.FirstOrDefault(s=>s.Kind=="inserted" && t>=s.OutputStart && t<s.OutputEnd);
            var affecting=motion && strength>0 ? map.Events.Where(e=>t>=e.OutputStart && t<=e.OutputEnd+(e.Type == "tape_stop" ? .15 : 0)).ToArray() : Array.Empty<HeadroomEvent>();
            bool retimedSpan=map.TimeMap.Any(s=>s.Kind!="original" && t>=s.OutputStart && t<=s.OutputEnd);
            if(!retimedSpan && affecting.Length==0) continue;
            double source=map.SourceAt(t);
            var stutter=repeatFace ? map.Events.FirstOrDefault(e=>e.Type=="stutter" && t>=e.OutputStart && t<e.OutputEnd) : null;
            double faceSource=source;
            if(stutter!=null)
            {
                var r=stutter.RepeatList.FirstOrDefault(r=>t>=r.OutputStart && t<r.OutputEnd);
                if(r!=null) faceSource=stutter.OriginalStart+(t-r.OutputStart)/r.Duration*(stutter.OriginalEnd-stutter.OriginalStart);
            }
            var freeze=affecting.FirstOrDefault(e=>e.Type=="stretch" && t<e.OutputEnd);
            if(freeze!=null) faceSource=map.SourceAt(freeze.OutputStart);
            var values=new Dictionary<(ServoNames,RobotControls?),ServoCommand>();
            foreach(var (key,track) in tracks)
            {
                bool head=Head(key.Servo) || key.Servo==ServoNames.RGBCommand;
                if(!retimedSpan && (!head || key.Servo==ServoNames.RGBCommand) && !motionServos.Contains(key.Servo)) continue;
                double at=start+(head?faceSource:source);
                var c=track.At(at); if(c!=null) values[key]=c;
            }
            if(affecting.Length>0)
            {
                foreach(var servo in motionServos)
                    if(!values.Keys.Any(k=>k.Item1==servo) && !(servo is ServoNames.LeftEyePop or ServoNames.RightEyePop && values.Keys.Any(k=>k.Item1==ServoNames.BothEyePop)))
                        values[(servo,null)]=new ServoCommand {Servo=servo,NumericValue=(int)Math.Round(servo switch
                        { ServoNames.URDFSlide => framing?.HorizontalPercent ?? 0, ServoNames.URDFSpin => framing?.SpinDegrees ?? 0, _ => 0 })};
                double tilt=0,nod=0;
                var offsets=new Dictionary<ServoNames,double>();
                void Add(ServoNames s,double v) => offsets[s]=offsets.GetValueOrDefault(s)+v;
                foreach(var e in affecting)
                {
                    double duration=e.OutputEnd-e.OutputStart, u=Math.Clamp((t-e.OutputStart)/duration,0,1);
                    double sign=(e.Id%2==0?1:-1), intensity=e.Type switch {"stutter"=>Math.Clamp(e.Repeats/4d,.35,1),"stretch"=>Math.Clamp(e.Factor/4,.5,1),"pitch_jump"=>Math.Clamp(Math.Abs(e.Semitones)/8,.4,1),_=>1};
                    double scale=intensity*strength;
                    // Exact event boundaries remain the original recording; reboot is an explicit post-event tail.
                    if(Math.Abs(t-e.OutputStart)<.0005 || Math.Abs(t-e.OutputEnd)<.0005) continue;
                    if(e.Type=="stutter" && t<e.OutputEnd)
                    {
                        int k=0; for(int j=1;j<e.RepeatList.Count;j++) if(t>=Math.Max(e.OutputStart,e.RepeatList[j].OutputStart-frame)) k=j;
                        var p=Poses[poses[e.Id][k]]; int n=k+1;
                        tilt+=p.Tilt*scale; nod+=(p.Nod+Math.Sign(e.PitchStepSemitones)*2*n)*scale;
                        Add(ServoNames.NeckTurn,p.Turn*scale); Add(ServoNames.URDFSlide,p.Slide*ServoCommand.RangeFor(ServoNames.URDFSlide).Max*(e.PitchStepSemitones==0?1:1+.15*n)*scale);
                        Add(ServoNames.URDFSpin,Math.Sign(p.Slide)*(3+1.5*n)*scale);
                    }
                    else if(e.Type=="stretch" && t<e.OutputEnd)
                    {
                        Add(ServoNames.B_ChestTilt,sign*Math.Min(12,3+2*e.Factor)*Math.Sin(Math.PI*u)*scale);
                        Add(ServoNames.URDFSpin,sign*6*Math.Sin(Math.PI*u)*scale);
                        Add(ServoNames.LeftEyePop,1200*u*scale); Add(ServoNames.RightEyePop,1200*u*scale);
                    }
                    else if(e.Type=="tape_stop")
                    {
                        if(t<e.OutputEnd)
                        {
                            double progress=(2*u-(1-e.SlowestSpeed)*u*u)/(1+e.SlowestSpeed);
                            nod-=10*progress*scale; Add(ServoNames.B_ChestTilt,8*progress*scale); Add(ServoNames.URDFSpin,20*progress*scale);
                            Add(ServoNames.IrisClose,50*progress*scale);
                            Add(ServoNames.LeftEyePop,-1600*progress*scale); Add(ServoNames.RightEyePop,-1600*progress*scale);
                        }
                        else
                        {
                            double elapsed=t-e.OutputEnd;
                            Add(ServoNames.LeftEyePop,2000*Math.Clamp(1-(elapsed-frame)/Math.Max(.001,.15-frame),0,1)*scale); Add(ServoNames.RightEyePop,2000*Math.Clamp(1-(elapsed-frame)/Math.Max(.001,.15-frame),0,1)*scale);
                            if(elapsed<.12) Add(ServoNames.NeckTurn,5*Math.Sin(4*Math.PI*elapsed/.12)*scale);
                        }
                    }
                    else if(e.Type=="pitch_jump")
                    {
                        double frames=(t-e.OutputStart)/frame;
                        double shape=frames<3?frames/3:frames<4?1:Math.Max(0,1-(frames-4)/5);
                        nod+=Math.Sign(e.Semitones)*6*shape*scale; tilt+=Math.Sign(e.Semitones)*3*shape*scale;
                        Add(ServoNames.URDFSlide,Math.Sign(e.Semitones)*.05*ServoCommand.RangeFor(ServoNames.URDFSlide).Max*shape*scale);
                    }
                }
                foreach(var (key,c) in values.Where(p=>!Neck(p.Key.Item1)).ToArray())
                {
                    double offset=offsets.GetValueOrDefault(c.Servo);
                    if(c.Servo==ServoNames.BothEyePop) offset=offsets.GetValueOrDefault(ServoNames.LeftEyePop);
                    if(c.Disable || c.IsTextServo) continue;
                    if(c.Servo==ServoNames.B_ChestTilt)
                    {
                        var definition=BodyControlCatalog.Find(c.Servo); var setting=config.BodySetting(definition);
                        double degrees=setting.Map(c.NumericValue,definition)*180/Math.PI+offset;
                        c.NumericValue=DegreesToValue(v=>setting.Map(v,definition)*180/Math.PI,degrees);
                    }
                    else if(c.Servo==ServoNames.NeckTurn)
                    {
                        var child=c.Control??RobotControls.NeckTurn;
                        double zero=config.Map(c.Servo,child,c.NumericValue,servoConfig);
                        c.NumericValue=DegreesToValue(v=>config.Map(c.Servo,child,v,servoConfig),zero+offset);
                    }
                    else c.NumericValue+=(int)Math.Round(offset);
                }
                // Nod and tilt share actuators: author independent children rather than competing parent commands.
                var neckValues=values.Values.Where(c=>Neck(c.Servo)).OrderBy(c=>c.OffsetSeconds).ToArray();
                foreach(var key in values.Keys.Where(k=>Neck(k.Item1)).ToArray()) values.Remove(key);
                foreach(var child in new[] {RobotControls.NeckTiltLeft,RobotControls.NeckTiltRight})
                {
                    // Nod and tilt are complementary: the base piston mixes both (a direct piston command wins).
                    var direct=neckValues.LastOrDefault(c=>c.Control==child);
                    double nodValue=neckValues.LastOrDefault(c=>c.Control==null && c.Servo==ServoNames.NeckNodUp)?.NumericValue??0;
                    double tiltValue=neckValues.LastOrDefault(c=>c.Control==null && c.Servo==ServoNames.NeckTiltRight)?.NumericValue??0;
                    var mixed=NeckMix.Pistons(servoConfig,nodValue,tiltValue);
                    double native=direct!=null ? NeckMix.ToNodUnits(servoConfig,direct.Servo,child,direct.NumericValue)
                        : child==RobotControls.NeckTiltLeft ? mixed.Left : mixed.Right;
                    double angle=config.Map(ServoNames.NeckNodUp,child,native,servoConfig);
                    var previous=direct??neckValues.LastOrDefault();
                    double target=angle+tilt+(child==RobotControls.NeckTiltLeft?nod:-nod);
                    var c=previous?.Clone()??new ServoCommand(); c.Servo=ServoNames.NeckNodUp; c.Control=child;
                    c.NumericValue=DegreesToValue(v=>config.Map(c.Servo,child,v,servoConfig),target);
                    values[(c.Servo,child)]=c;
                }
            }
            foreach(var c in values.Values)
            {
                if(c.IsTextServo)
                {
                    bool boundary=map.TimeMap.Any(s=>s.Kind=="inserted" && (Math.Abs(t-s.OutputStart)<.0005 || Math.Abs(t-s.OutputEnd)<.0005));
                    bool repeatStart=stutter?.RepeatList.Any(r=>Math.Abs(t-r.OutputStart)<.0005)==true;
                    bool key=tracks[(c.Servo,c.Control)].C.Any(k=>Math.Abs(k.OffsetSeconds-(start+faceSource))<.0005);
                    if(!boundary && !repeatStart && !key) continue;
                }
                Put(c,t);
            }
        }
        // Replace only generated target/time keys; preserve all other authored commands and identities.
        output.RemoveAll(c=>generated.ContainsKey((c.OffsetSeconds,c.Servo,c.Control)) ||
            motion && Neck(c.Servo) && samples.Contains(ServoCommand.TimeKey(c.OffsetSeconds-start)) &&
            generated.ContainsKey((c.OffsetSeconds,ServoNames.NeckNodUp,RobotControls.NeckTiltLeft)));
        output.AddRange(generated.Values);
        if(motion && strength>0) AddBackgroundCues(output,map,start,frame,background);
        foreach(var c in generated.Values)
            if(!string.IsNullOrEmpty(c.AudioTakeId) && takes.TryGetValue(c.AudioTakeId,out double origin)) c.AudioSourceSeconds=c.OffsetSeconds-origin;
        if(output.Any(c=>!double.IsFinite(c.OffsetSeconds)||c.OffsetSeconds<0||c.OffsetSeconds>86400)) throw new InvalidDataException("Updated commands exceed the 24-hour timeline limit.");
        int low=map.Words.Count(w=>w.AlignConfidence<.25);
        if(low>0) warnings.Add($"{low} low-confidence words (<0.25).");
        if(clamped>0) warnings.Add($"{clamped} generated values clamped to control limits.");
        if(motion) warnings.Add("Motion is sampled at the sequence spline rate. Preview the result before hardware playback.");
        return new(output,generated.Count,warnings);
    }
    private static void AddBackgroundCues(List<ServoCommand> commands, HeadroomTimeline timeline, double start, double frame, UrdfPresentationSettings initial)
    {
        // Only existing moving-image backgrounds get glitch cues. Stage and static presets remain untouched.
        var original = commands.Where(c=>c.Servo==ServoNames.LoadBackdrop && !c.Disable).OrderBy(c=>c.OffsetSeconds).ToArray();
        var cues = new SortedDictionary<double, BackdropCommand>();
        BackdropCommand At(double time)
        {
            var prior=original.LastOrDefault(c=>c.OffsetSeconds<=time);
            return prior==null ? new BackdropCommand { Background=initial?.Clone()??new() } : BackdropCommand.Read(prior.TextValue);
        }
        foreach(var e in timeline.Events)
        {
            var preset=At(start+e.OutputStart);
            if(preset.Background.BackgroundMode!=UrdfBackgroundMode.MovingImage) continue;
            if(e.Type=="stretch")
            {
                var frozen=BackdropCommand.Read(preset.ToJson()); frozen.Background.BackgroundMode=UrdfBackgroundMode.Image;
                cues[ServoCommand.TimeKey(start+e.OutputStart)]=frozen;
                cues[ServoCommand.TimeKey(start+e.OutputEnd)]=At(start+e.OutputEnd);
            }
            var cuts=e.Type=="stutter" ? e.RepeatList.Select(r=>Math.Max(e.OutputStart,r.OutputStart-frame))
                : e.Type=="tape_stop" ? new[]{Math.Max(e.OutputStart,e.OutputEnd-frame)} : Array.Empty<double>();
            int index=0;
            foreach(double t in cuts)
            {
                var cut=At(start+t); cut.Background.BackgroundSeed=unchecked(e.Id*397+index++);
                cut.Background.BackgroundHalfTurns=true; cues[ServoCommand.TimeKey(start+t)]=cut;
            }
        }
        foreach(var (time,preset) in cues)
        {
            // An authored backdrop command always wins at an occupied millisecond.
            if(commands.Any(c=>c.Servo==ServoNames.LoadBackdrop && ServoCommand.TimeKey(c.OffsetSeconds)==time)) continue;
            commands.Add(new ServoCommand { Servo=ServoNames.LoadBackdrop, OffsetSeconds=time, TextValue=preset.ToJson(), Reason="Headroom background cue" });
        }
    }
    private static int Inverse(Func<double,double> map,double target,int min,int max)
    {
        double a=min,b=max; bool ascending=map(max)>=map(min);
        for(int i=0;i<24;i++) {double m=(a+b)/2; if((map(m)<target)==ascending)a=m;else b=m;}
        return (int)Math.Round((a+b)/2);
    }
}
