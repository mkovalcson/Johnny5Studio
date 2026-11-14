using System.IO;
using System.Text.Json;
using ServoAnimator;

internal static class SharedBehaviorChecks
{
    internal static void Run()
    {
        int count=0; void Check(bool ok,string message){if(!ok)throw new Exception(message);count++;}
        Check(typeof(ServoCommand).Assembly==typeof(AnimationDocument).Assembly && typeof(ServoCommand).Assembly.GetName().Name=="J5Studio.Animation","Commands and sequence data share one implementation.");
        var audio=new ServoCommand{Servo=ServoNames.Play,OffsetSeconds=1,TextValue="Projects/voice.wav",AudioTakeId="take"};
        ServoCommand Point(double time,int value)=>new(){Servo=ServoNames.NeckTurn,OffsetSeconds=time,NumericValue=value,AudioTakeId="take"};
        var before=Point(1.1,10);var middle=Point(1.5,20);var after=Point(2,30);
        var document=new AnimationDocument{Commands=new(){audio,before,middle,after},SplineServos=new(){"NeckTurn"},CommandGroupNumbers=new(){{"head",1}}};
        AudioTrimSettings.Apply(document,audio,new(){SkipCommitted=true,SkipSeconds=.2,EndCommitted=true,EndSeconds=.8,ScrubBefore=true,ScrubAfter=true});
        Check(document.Commands.Count==2&&audio.AudioTrim.HiddenCommands.Count==2,"Trimming retains excluded commands as hidden source data.");
        Check(Math.Abs(middle.OffsetSeconds-1.3)<1e-9&&Math.Abs(middle.AudioSourceSeconds!.Value-.5)<1e-9,"Linked commands retain source timing while the playable span shifts.");
        var clone=document.Clone();var copiedAudio=clone.Commands.Single(c=>c.Servo==ServoNames.Play);
        copiedAudio.AudioTrim.HiddenCommands[0].NumericValue=99;clone.SplineServos.Clear();clone.CommandGroupNumbers["head"]=2;
        Check(audio.AudioTrim.HiddenCommands[0].NumericValue==10&&document.SplineServos.Count==1&&document.CommandGroupNumbers["head"]==1,"Cloning isolates hidden commands, spline selections and group metadata.");
        AudioTrimSettings.Apply(document,audio,new(){CommandsLinked=true});
        Check(document.Commands.Count==4&&audio.AudioTrim.HiddenCommands.Count==0,"Removing trims restores the hidden commands.");
        Check(Math.Abs(before.OffsetSeconds-1.1)<1e-9&&Math.Abs(middle.OffsetSeconds-1.5)<1e-9&&after.OffsetSeconds==2,"Restoring trims recovers authored source times.");
        var copied=document.Commands.Select(c=>c.Clone()).ToList();AudioTrimSettings.RemapCopiedTakes(copied);
        Check(copied.All(c=>c.AudioTakeId==copied[0].AudioTakeId)&&copied[0].AudioTakeId!="take"&&document.Commands.All(c=>c.AudioTakeId=="take"),"Copied takes receive an independent identity without changing the original.");
        var parent=new ServoCommand{Servo=ServoNames.NeckTurn,OffsetSeconds=.1001,NumericValue=10};
        var child=new ServoCommand{Servo=ServoNames.NeckTurn,Control=RobotControls.NeckTurn,OffsetSeconds=.1,NumericValue=15};
        var later=new ServoCommand{Servo=ServoNames.NeckTurn,OffsetSeconds=.1002,NumericValue=20};
        var commands=new List<ServoCommand>{parent,child,later};var groups=CommandConflicts.Find(commands);
        Check(groups.Count==1&&groups[0].Commands.Count==2,"Conflict detection groups millisecond-equal parent commands while keeping child targets separate.");
        CommandConflicts.KeepSelected(commands,groups,new[]{later});
        Check(commands.SequenceEqual(new[]{child,later}),"Conflict resolution preserves survivor insertion order.");
        var live=Point(.5,10);var session=new CommandEditSession(new[]{live},.5);session.Commands[0].NumericValue=20;live.NumericValue=30;
        var merged=session.Merge(new[]{live});
        Check(CommandConflicts.Find(merged).Single().Commands.Select(c=>c.NumericValue).SequenceEqual(new[]{30,20}),"Concurrent edits retain both versions for conflict selection.");
        var points=new[]{new TimelineKeyframe(0,0,false),new TimelineKeyframe(1,20,false),new TimelineKeyframe(3,-10,false)};
        var track=new KeyframeTrack(points,true);double[] times={0,1,3},values={0,20,-10};var tangents=SplineUtil.Tangents(times,values);
        for(int i=0;i<=300;i++){double t=i/100d;Check(Math.Abs(track.At(t)!.Value.Value-SplineUtil.Eval(times,values,tangents,t))<1e-12,"Player and editor curves agree at "+t);}
        Check(track.At(-1)==null&&SplineUtil.Eval(times,values,tangents,-1)==0,"Hosts retain their explicit pre-first-command behavior.");
        var broken=new KeyframeTrack(new[]{new TimelineKeyframe(0,10,true),new TimelineKeyframe(1,90,false)},true);
        Check(broken.At(.999)!.Value.Value==10&&broken.At(1)!.Value.Value==90,"BreakSpline holds the outgoing value until the next command.");
        var duplicates=new KeyframeTrack(new[]{new TimelineKeyframe(0,1,false),new TimelineKeyframe(0,7,false),new TimelineKeyframe(1,20,false)},false);
        Check(duplicates.At(0)!.Value.Value==7&&duplicates.At(.5)!.Value.Stamp==0,"Duplicate-time playback retains the final authored point and its stamp.");
        double now=0;var transport=new TransportClock(()=>now);transport.Start();now=.2;transport.Stop();now=5;
        Check(Math.Abs(transport.Elapsed-.2)<1e-9&&!transport.IsRunning,"Pause holds the transport time.");
        transport.Start();now=5.3;Check(Math.Abs(transport.Elapsed-.5)<1e-9,"Resume preserves elapsed time.");
        transport.Restart();Check(transport.Elapsed==0&&transport.IsRunning,"Restart resets and runs the transport.");
        transport.Reset();Check(transport.Elapsed==0&&!transport.IsRunning,"Stop/reset clears transport time.");
        var device=new PlaybackClock();device.Start(0);
        double first=device.AdvanceTo(.1,0),second=device.AdvanceTo(.2,0),third=device.AdvanceTo(.3,null);
        Check(first>0&&second>first&&third>second,"Repeated or absent audio-device samples do not freeze the desktop timeline.");
        device.Rate=2;device.Start(0);Check(device.AdvanceTo(.5,null)==1,"Desktop playback retains its configured rate.");
        string temporary=Directory.CreateTempSubdirectory("J5-SharedAnimation-").FullName;
        string root=Path.Combine(temporary,"Config");Directory.CreateDirectory(root);
        try
        {
            string file=Path.Combine(root,"sequence.json");
            File.WriteAllText(file,"""{/* read-only playback view */ "audioFilePath":"Projects/voice.wav","audioStartOffsetSeconds":0.5,"urdfBackgroundFile":"LibraryScenes/scene.json","scaleValues":true,"splineServos":["NeckTurn"],"commands":[{"servo":"NeckTurn","offsetSeconds":0,"value":0.625},{"servo":"FutureEffect","offsetSeconds":1,"value":"payload"}],}""");
            string original=File.ReadAllText(file);
            using(var source=SequenceSource.Load(file))
            {
                Check(AnimationJson.Number(source.Commands.First(),"value")==.625&&source.ScaleValues,"The playback reader preserves fractional values and export metadata.");
                Check(AnimationJson.Text(source.Commands.Last(),"servo")=="FutureEffect","Unknown commands remain available for host warnings.");
                Check(source.PrimaryAudio=="Projects/voice.wav"&&source.AudioStartOffsetSeconds==.5&&source.SceneReference=="LibraryScenes/scene.json","Audio timing and saved scene references share one metadata reader.");
            }
            Check(File.ReadAllText(file)==original,"Reading playback data leaves the sequence unchanged.");
            document.AudioFilePath=Path.Combine(root,"Projects/voice.wav");document.UrdfBackgroundFile=Path.Combine(root,"LibraryScenes/scene.json");
            document.Commands.Insert(0,new(){Servo=ServoNames.NoseBody,OffsetSeconds=3,NumericValue=12});
            var order=document.Commands.ToArray();document.Save(file);
            Check(document.Commands.SequenceEqual(order),"Saving sorts a copy without reordering the live document.");
            var loaded=AnimationDocument.Load(file);
            Check(loaded.Commands.Select(c=>c.OffsetSeconds).SequenceEqual(loaded.Commands.Select(c=>c.OffsetSeconds).Order()),"Saved commands are ordered by timeline position.");
            Check(loaded.AudioFilePath==Path.Combine("Projects","voice.wav")&&loaded.UrdfBackgroundFile==Path.Combine("LibraryScenes","scene.json"),"Movie/sequence asset references remain Config-relative.");
            string movie=Path.Combine(root,"movie.json");
            File.WriteAllText(movie,"""{/* legacy Player casing */ "DESCRIPTION":"Film","CREATEDDATE":"2000-01-01","SEQUENCES":["Projects/a.json","Projects/b.json"],"SEQUENCELOOPS":[true],"SEQUENCETRIGGERS":["","button"],}""");
            var film=MovieDocument.Load(movie);
            Check(film.Sequences.Count==2&&film.SequenceLoops.Single()&&film.SequenceTriggers[1]=="button","The shared movie reader retains Player casing, loop and trigger compatibility.");
            film.Save(movie);Check(MovieDocument.Load(movie).CreatedDate=="2000-01-01","Movie creation dates survive round trips.");
        }
        finally {if(Path.GetFileName(temporary).StartsWith("J5-SharedAnimation-")&&temporary.StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase))Directory.Delete(temporary,true);}
        Console.WriteLine($"PASS: {count} shared animation trim, clone, conflict, file, curve and playback-clock assertions.");
    }
}
