using ServoAnimator;

namespace Johnny5VR;

internal static class ExpressiveIdleChecks
{
    internal static void Run()
    {
        int count=0;
        void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);count++;}
        foreach(var mood in Enum.GetValues<IdleMood>().Where(m=>m!=IdleMood.None))
        {
            Check(ExpressiveIdle.Offsets(mood,0).Values.All(v=>v==0),"Idle begins at the final authored pose.");
            foreach(double time in new[]{.1,1.0,2,10,1000})
                foreach(var (servo,offset) in ExpressiveIdle.Offsets(mood,time))
                {
                    var range=ControlDefinitions.RangeFor(servo);
                    double value=ExpressiveIdle.Value(servo,range.Max,offset);
                    Check(double.IsFinite(value)&&value>=range.Min&&value<=range.Max,"Idle remains within the supported control limits.");
                }
        }
        Check(!ExpressiveIdle.Offsets(IdleMood.Attentive,4).SequenceEqual(ExpressiveIdle.Offsets(IdleMood.Impatient,4)),"Moods have distinct motion.");
        var directory=Path.Combine(Path.GetTempPath(),"J5IdleChecks-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path=Path.Combine(directory,"movie.json");
            var movie=new MovieDocument{Sequences=["first.json","second.json"],SequenceLoops=[true,false],SequenceIdles=[IdleMood.Curious]};
            movie.Save(path);
            var copy=MovieDocument.Load(path);
            Check(ExpressiveIdle.For(copy,0)==IdleMood.Curious&&!copy.SequenceLoops[0],"Mood survives save and replaces looping.");
            Check(ExpressiveIdle.For(copy,1)==IdleMood.None,"Short mood lists default to off.");
            File.WriteAllText(path, """{"sequences":["old.json"],"sequenceLoops":[true]}""");
            copy=MovieDocument.Load(path);
            Check(copy.SequenceLoops[0]&&ExpressiveIdle.For(copy,0)==IdleMood.None,"Old movies retain their loop behavior.");
            double now=0;
            using var player=new PlaybackPlayer(()=>now);
            PlaybackSequence Sequence(string name,IdleMood mood,bool loop=false)=>new(name,name,1,[],[],loop,"",[]){IdleMood=mood};
            player.Load(new("Movie",[Sequence("first",IdleMood.Curious),Sequence("second",IdleMood.None)],[]));
            player.Toggle();now=1.1;player.Tick();
            Check(player.IsIdling&&player.Index==0&&player.Running,"Idle waits at the current sequence instead of advancing.");
            now=2;var first=player.Pose();now=4;var second=player.Pose();
            Check(first["servo:NeckTurn"].Value!=second["servo:NeckTurn"].Value,"Idle motion keeps running during the wait.");
            Check(player.TransportLabel=="Pause idle","Idle transport describes Pause.");
            player.Toggle();
            double paused=player.Position;var held=player.Pose();
            now+=2;player.Tick();
            Check(player.IsIdling&&!player.Running&&player.Index==0&&player.Position==paused,"Pause freezes idle without advancing.");
            Check(player.Pose()["servo:NeckTurn"].Value==held["servo:NeckTurn"].Value&&player.TransportLabel=="Resume idle","Paused idle holds its pose and advertises Resume.");
            player.Toggle();now+=1;player.Tick();
            Check(player.Running&&player.IsIdling&&player.Index==0&&player.Position>paused,"Resume continues idle in the same sequence.");
            player.Next();
            Check(!player.IsIdling&&player.Index==1,"Next exits idle immediately.");
            player.Stop();
            Check(!player.IsIdling&&!player.Running,"Stop clears idle.");
            player.Load(new("Movie",[Sequence("last",IdleMood.Tired)],[]));
            player.Toggle();now+=2;player.Tick();
            Check(player.IsIdling,"The final sequence can idle too.");
            player.Next();now+=1;player.Tick();
            Check(player.IsIdling&&player.Running,"Next on the final idle leaves the animation running.");
            player.Toggle();double endPause=player.Position;now+=2;player.Toggle();now+=1;player.Tick();
            Check(player.IsIdling&&player.Index==0&&player.Position>endPause,"Final idle can pause and resume without restarting the movie.");
            player.Stop();
        }
        finally{Directory.Delete(directory,true);}
        Console.WriteLine($"PASS: {count} focused expressive idle checks.");
    }
}
