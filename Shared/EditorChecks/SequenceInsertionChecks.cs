namespace ServoAnimator;
internal static class SequenceInsertionChecks
{
    internal static void Run(Action<bool,string> check)
    {
        ServoCommand C(ServoNames servo,double t,int value=0)=>new(){Servo=servo,OffsetSeconds=t,NumericValue=value};
        var old=new[]{C(ServoNames.NeckTurn,9),C(ServoNames.NeckTurn,10),C(ServoNames.NeckTurn,11),C(ServoNames.NeckTurn,12),C(ServoNames.NeckTurn,13),C(ServoNames.B_ElbowLeft,11),C(ServoNames.Play,11)};
        var added=new[]{C(ServoNames.NeckTurn,0,20),C(ServoNames.NeckTurn,1,30),C(ServoNames.NeckTurn,2,40),C(ServoNames.B_ElbowLeft,.5,10)};
        added[0].GroupId="old-group";added[1].GroupId="another-group";
        var plan=new SequenceInsertion(old,added,10,2);var neck=new SequenceControlKey(ServoNames.NeckTurn,null);var arm=new SequenceControlKey(ServoNames.B_ElbowLeft,null);
        check(plan.Start==10&&plan.End==12&&plan.Overlaps.Single(o=>o.Key==neck) is {ExistingCount:3,InsertedCount:3},"Sequence review counts original and inserted commands across inclusive insertion bounds.");
        var choices=new Dictionary<SequenceControlKey,SequenceOverlapChoice>{{neck,SequenceOverlapChoice.KeepOriginal},{arm,SequenceOverlapChoice.UseInserted}};
        var kept=plan.Resolve(choices);
        check(!kept.Inserted.Any(c=>c.Servo==ServoNames.NeckTurn)&&kept.Existing.Count(c=>c.Servo==ServoNames.NeckTurn)==5,"Keep original removes only inserted commands for that control.");
        check(!kept.Existing.Any(c=>c.Servo==ServoNames.B_ElbowLeft)&&kept.Inserted.Any(c=>c.Servo==ServoNames.B_ElbowLeft),"Use inserted removes original control commands anywhere in the insertion span.");
        choices[neck]=SequenceOverlapChoice.UseInserted;var replaced=plan.Resolve(choices);
        check(replaced.Existing.Where(c=>c.Servo==ServoNames.NeckTurn).Select(c=>c.OffsetSeconds).SequenceEqual(new[]{9d,13d}),"Use inserted preserves original commands outside the insertion span.");
        var mergePlan=new SequenceInsertion(old,new[]{C(ServoNames.NeckTurn,1.0001,50),C(ServoNames.NeckTurn,1.5,60)},10,2);
        var merged=mergePlan.Resolve(new Dictionary<SequenceControlKey,SequenceOverlapChoice>{{neck,SequenceOverlapChoice.Merge}});
        check(merged.Existing.Where(c=>c.Servo==ServoNames.NeckTurn).Select(c=>c.OffsetSeconds).SequenceEqual(new[]{9d,10d,12d,13d})&&merged.Inserted.Count==2,"Merge removes only exact rounded-time matches and retains other original commands.");
        check(merged.Existing.Any(c=>c.Servo==ServoNames.Play),"Overlap choices leave unrelated audio/control commands intact.");
        var grouped=plan.Resolve(plan.Overlaps.ToDictionary(o=>o.Key,_=>SequenceOverlapChoice.Merge));
        check(grouped.Inserted.Select(c=>c.GroupId).Distinct().Count()==1&&grouped.Inserted[0].GroupId==plan.GroupId&&plan.GroupId!="old-group", "Each insertion receives one fresh group, replacing source group identities.");
        var again=new SequenceInsertion(old,added,10,2);check(again.GroupId!=plan.GroupId,"Repeated library insertions cannot share group identity.");
        check(added[0].OffsetSeconds==0&&added[0].GroupId=="old-group"&&old[2].NumericValue==0,"Planning and resolving never mutate source library or original timeline commands.");
        var ganged=C(ServoNames.FlapsOpen,10,25);var individual=C(ServoNames.FlapsOpen,1,50);individual.Control=RobotControls.BrowLeftTopOpen;
        IEnumerable<ServoCommand> Expand(ServoCommand c)
        {
            if(c.Control.HasValue)return new[]{c};
            return new[]{RobotControls.BrowLeftTopOpen,RobotControls.BrowRightTopOpen}.Select(child=>{var copy=c.Clone();copy.Control=child;return copy;});
        }
        var split=new SequenceInsertion(new[]{ganged},new[]{individual},10,2,Expand);
        var result=split.Resolve(split.Overlaps.ToDictionary(o=>o.Key,_=>SequenceOverlapChoice.UseInserted));
        check(split.Overlaps.Count==1&&result.Existing.Count==1&&result.Existing[0].Control==RobotControls.BrowRightTopOpen,"Individual control overlap splits a gang and preserves its unselected sibling.");
        var fresh=new SequenceInsertion(Array.Empty<ServoCommand>(),added,4,5).Resolve(new Dictionary<SequenceControlKey,SequenceOverlapChoice>());
        check(fresh.Inserted.Count==4&&fresh.Inserted.All(c=>!string.IsNullOrEmpty(c.GroupId)),"Non-overlapping library insertion also groups all commands by default.");
    }
}
