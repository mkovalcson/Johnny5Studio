using System.Numerics;
using Johnny5.VrBridge;

namespace Johnny5VR;
internal static class RobotPoseChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var control=new VrPoseHandle("Servo:Test","Neck turn",new(0,1,-2),-100,100,0,false);
        check(RobotPoseInteraction.AngleValue(-135,-100,100)==-100&&RobotPoseInteraction.AngleValue(135,-100,100)==100,"Dial arc reaches both native range limits.");
        check(RobotPoseInteraction.AngleValue(0,-100,100)==0,"Middle dial position is exactly neutral.");
        check(RobotPoseInteraction.AngleValue(200,-100,100)==100,"Dial end stop clamps outside the arc.");
        check(RobotPoseInteraction.DialRange(control)==(-100,100),"Joint dial uses its full configured range.");
        check(RobotPoseInteraction.DialRange(control with{Key="Servo:URDFStageX",Minimum=-100000,Maximum=100000})==(-100,100),"Scene translation uses a precise 200 cm pageable range.");
        check(RobotPoseInteraction.DialRange(control with{Key="Servo:URDFStageHeading",Minimum=-36000,Maximum=36000})==(-180,180),"Heading uses a pageable single-turn range.");
        var target=new PoseTarget(control,new(0,1,-2));
        check(RobotPoseInteraction.Pick([target],new(.15f,1,0),-Vector3.UnitZ,Vector3.UnitX,Vector3.UnitY)==target,"Enlarged label is selectable near its edge.");
        check(RobotPoseInteraction.Pick([target],new(.5f,1,0),-Vector3.UnitZ,Vector3.UnitX,Vector3.UnitY)==null,"Ray outside a label does not select it.");
        check(RobotPoseInteraction.Pick([target],new(0,1,0),Vector3.UnitZ,Vector3.UnitX,Vector3.UnitY)==null,"Pose selection ignores handles behind the controller.");
        var state=new VrPoseState("context",1.234,true,2,"draft","id",[control]);
        using var stream=new MemoryStream();using(var w=new BinaryWriter(stream,System.Text.Encoding.UTF8,true))PoseWire.Write(w,state);stream.Position=0;
        using var r=new BinaryReader(stream);var copy=PoseWire.Read(r)!;
        check(copy.Token==state.Token&&copy.Cursor==1.234&&copy.Handles[0]==control,"Pose wire preserves cursor, ranges, labels and coordinates.");
        var requests=new List<VrPoseRequest>();var interaction=new RobotPoseInteraction{Enabled=true,State=state,Send=requests.Add};
        var head=Matrix4x4.CreateTranslation(0,1.6f,0);
        Matrix4x4 Hand(Vector3 point)=>Matrix4x4.CreateTranslation(point+Vector3.UnitZ);
        interaction.Step(head,Hand(Vector3.Zero),false,true);
        var label=interaction.Visible[0].Label;
        check(interaction.Visible.Length==1,"Controls are visible without pointing at robot geometry.");
        check(interaction.DialOpen,"Dial appears automatically alongside pose labels.");
        int awayRequests=requests.Count;
        check(!interaction.Step(head,Hand(new Vector3(4,0,-2)),false,true,new(0,1),.02f)&&requests.Count==awayRequests,"An idle dial does not capture locomotion or change values while pointing away.");
        interaction.Enabled=false;interaction.Step(head,Hand(Vector3.Zero),false,true);
        interaction.Enabled=true;interaction.Step(head,Hand(Vector3.Zero),false,true);
        check(interaction.DialOpen&&interaction.Dial!.Control.Key==control.Key,"Hiding and reopening pose mode remembers its selected control.");
        interaction.Step(Matrix4x4.CreateRotationY(.1f)*head,Hand(Vector3.Zero),false,true);
        check(interaction.Visible[0].Label==label,"Label layout remains stable while the viewer turns their head.");
        var hand=Hand(label);interaction.Step(head,hand,true,true);
        check(interaction.DialOpen&&!interaction.Grabbing&&requests.Count==0,"Selecting a label opens a dial without changing the control.");
        var dial=interaction.Dial!;
        interaction.Step(head,hand,false,true);
        hand=Hand(dial.Center+new Vector3(.18f,.015f,0));interaction.Step(head,hand,true,true);
        check(interaction.Grabbing&&interaction.Visible.Length==0,"Dragging the dial hides all other control labels.");
        check(requests.Count==1&&Math.Abs(requests[0].Value-66.6667f)<.01&&requests[0].Action==VrPoseAction.Set,"Pointing around the dial previews the expected native value.");
        hand=Hand(dial.Center+new Vector3(.01f,-.20f,0));interaction.Step(head,hand,true,true);
        hand=Hand(dial.Center+new Vector3(-.01f,-.20f,0));interaction.Step(head,hand,true,true);
        check(requests[^1].Value==100,"Crossing the bottom gap holds the maximum instead of jumping to minimum.");
        interaction.Step(head,hand,false,true);check(!interaction.Grabbing&&interaction.DialOpen&&interaction.Visible.Length==1&&interaction.Dial!.Control.Key==control.Key,"Trigger release restores labels and keeps the selected dial active.");
        interaction.Step(head,Hand(label),true,true);interaction.Step(head,Hand(label),false,true);
        int count=requests.Count;interaction.Step(head,Hand(label),false,true,new(0,.5f),.02f);
        check(requests.Count==count+1&&requests[^1].Value<1,"Right stick provides a small, precise native-unit adjustment.");
        dial=interaction.Dial!;
        using(var image=PoseHandleOverlay.DrawDial(dial,true))image.Save(Path.Combine(Diagnostics.Folder,"pose-dial-preview.png"));
        hand=Hand(dial.Center+new Vector3(0,-.26f,0));interaction.Step(head,hand,true,true);
        check(interaction.DialOpen&&requests[^1].Value==0,"Reset default previews neutral while keeping the dial active.");
        interaction.Step(head,hand,false,true);
        var fake=new Labels();
        using(var labels=new PoseHandleOverlay(fake,background:false))
        {
            labels.Draw(interaction,head,Matrix4x4.Identity);int uploads=fake.Uploads;
            labels.Draw(interaction,head,Matrix4x4.Identity);
            int white=Enumerable.Range(0,fake.Pixels.Length/4).Count(i=>fake.Pixels[i*4]>220&&fake.Pixels[i*4+1]>220&&fake.Pixels[i*4+2]>220);
            check(uploads==3&&fake.Uploads==3&&white>100,"Pose labels and persistent dial render immediately and reuse unchanged images.");
            interaction.Step(head,Hand(label),true,true);labels.Draw(interaction,head,Matrix4x4.Identity);int withDial=fake.Uploads;
            labels.Draw(interaction,head,Matrix4x4.Identity);
            check(withDial==fake.Uploads,"Dial renders once and reuses its unchanged GPU image.");
        }
        check(fake.Destroyed==4,"Label and dial overlay resources are released.");
        interaction.Insert();check(requests[^1].Action==VrPoseAction.Insert&&requests[^1].Token==state.Token,"Explicit insert carries current editor context.");
        interaction.State=state with{Token="new context"};interaction.Step(head,hand,false,true);
        check(interaction.DialOpen&&!interaction.Grabbing&&interaction.Dial!.Control.Key==control.Key,"Changed context restores the remembered selection without applying values.");
        interaction.State=state with{Handles=Enumerable.Range(0,85).Select(i=>control with{Key="Servo:"+i,Name="Control "+i}).ToArray()};interaction.Step(head,hand,false,true);
        check(interaction.Visible.Length==85,"All supported controls remain visible, without the old twelve-handle limit.");
        check(interaction.Visible.Select(t=>t.Label).Distinct().Count()==85,"Every label has a separate selection target.");
        var many=new Labels();using(var labels=new PoseHandleOverlay(many,background:false))labels.Draw(interaction,head,Matrix4x4.Identity);
        check(many.Created<=8&&many.Destroyed==many.Created,"Central controls share compositor columns rather than creating one overlay per label.");
        var left=control with{Key="Child:FlapsOpen:BrowLeftTopOpen",Name="Left top flap"};
        var right=control with{Key="Child:FlapsOpen:BrowRightTopOpen",Name="Right top flap"};
        var body=control with{Key="Servo:B_ShoulderRotateLeft",Name="Shoulder rotate left"};
        var gang=control with{Key="Servo:NeckTiltRight",Name="Neck tilt right",Ganged=true};
        check(RobotPoseInteraction.ControlBank(left)==-1&&RobotPoseInteraction.ControlBank(right)==1,"Head child controls are classified by their own side, not the parent gang name.");
        check(RobotPoseInteraction.ControlBank(body)==-1&&RobotPoseInteraction.ControlBank(gang)==0,"Side-specific body joints join their side while central directional gangs remain above the robot.");
        interaction.State=state with{Token="grouped",Handles=[left,right,body,gang]};interaction.Step(head,hand,false,true);
        var layout=interaction.Visible.ToDictionary(t=>t.Control.Key);
        check(layout[left.Key].Label.X>0&&layout[right.Key].Label.X<0,"Left and right labels occupy their requested sides.");
        check(layout[body.Key].Label.X>0&&layout[gang.Key].Label.Y>layout[right.Key].Label.Y,"Side body controls stay beside the robot and central gangs remain above the head.");
        static byte[] Pixels(System.Drawing.Bitmap image)
        {
            var data=image.LockBits(new(0,0,image.Width,image.Height),System.Drawing.Imaging.ImageLockMode.ReadOnly,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var bytes=new byte[image.Width*image.Height*4];
            try{System.Runtime.InteropServices.Marshal.Copy(data.Scan0,bytes,0,bytes.Length);}finally{image.UnlockBits(data);}
            return bytes;
        }
        using(var normal=PoseHandleOverlay.DrawDial(dial,true,96))using(var high=PoseHandleOverlay.DrawDial(dial,true,192))
            check(Pixels(normal).SequenceEqual(Pixels(high)),"Dial typography is pixel-identical at 100% and 200% texture DPI.");
        using(var normal=new System.Drawing.Bitmap(600,100))using(var high=new System.Drawing.Bitmap(600,100))
        {
            high.SetResolution(192,192);
            using(var g=System.Drawing.Graphics.FromImage(normal))PoseHandleOverlay.DrawLabel(g,target,false);
            using(var g=System.Drawing.Graphics.FromImage(high))PoseHandleOverlay.DrawLabel(g,target,false);
            check(Pixels(normal).SequenceEqual(Pixels(high)),"Label typography is independent of monitor scaling.");
            check(normal.GetPixel(2,2).A==0&&normal.GetPixel(595,95).A==0,"Control labels no longer draw boxes or crossing borders.");
            normal.Save(Path.Combine(Diagnostics.Folder,"pose-label-borderless.png"));
        }
        var ordered=new[]{
            control with{Key="Servo:B_WristPitchLeft",Name="A wrist",Anchor=new(0,.7f,-2)},
            control with{Key="Servo:B_ShoulderRotateLeft",Name="Z shoulder",Anchor=new(0,1.3f,-2)},
            control with{Key="Servo:B_ElbowLeft",Name="M elbow",Anchor=new(0,1f,-2)},
            control with{Key="Servo:URDFStageX",Name="Robot X position",Anchor=new(0,.08f,-2)},control with{Key="Servo:URDFStageY",Name="Robot Y position",Anchor=new(0,.08f,-2)},
            control with{Key="Servo:URDFStageHeading",Name="Robot heading",Anchor=new(0,.08f,-2)},
            control with{Key="Servo:B_WaistSwivel",Name="Waist swivel"}};
        var paired=ServoAnimator.PairedBodyControls.All.Select(p=>control with{Key=p.Key,Name=p.Name,Ganged=true});
        interaction.State=state with{Token="ordered",Handles=ordered.Concat(paired).ToArray()};interaction.Step(head,hand,false,true);
        var shown=interaction.Visible.ToDictionary(t=>t.Control.Key);
        check(shown[ordered[1].Key].Label.Y>shown[ordered[2].Key].Label.Y&&shown[ordered[2].Key].Label.Y>shown[ordered[0].Key].Label.Y,"Side controls follow physical joint height, not alphabetical labels.");
        check(shown.Values.Where(t=>t.Control.Ganged).Select(t=>t.Label.Y).Distinct().Count()==5,"Ganged controls extend upward in their own readable column.");
        check(ordered.Skip(3).Take(3).All(h=>shown.ContainsKey(h.Key)),"X/Y/heading remain visible in the placement row.");
        check(shown[ordered[6].Key].Label.X>shown.Values.First(t=>t.Control.Ganged).Label.X,"Body controls are centered separately from the viewer-left ganged bank.");
        check(shown[ordered[0].Key].Width>.32f,"Side labels have larger physical text space.");
        check(RobotPoseInteraction.ControlBank(control with{Key="Servo:B_OpenLeftHand",Ganged=true})==-1,"Left hand gang stays beside individual left-hand controls.");
        string catalog=Path.Combine(Diagnostics.Folder,"pose-control-catalog.bin");
        if(File.Exists(catalog))
        {
            using var reader=new BinaryReader(File.OpenRead(catalog));var full=PoseWire.Read(reader)!;
            using var bounds=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(Diagnostics.Folder,"pose-control-bounds.json")));
            float modelFloor=bounds.RootElement.GetProperty("RobotFloor").GetSingle();
            float modelScale=1.8f/bounds.RootElement.GetProperty("RobotHeight").GetSingle();
            interaction.FloorY=0;interaction.RobotHeight=1.8f;
            full=full with{Handles=full.Handles.Select(h=>h with{Anchor=new(h.Anchor.Y*modelScale,(h.Anchor.Z-modelFloor)*modelScale,h.Anchor.X*modelScale)}).ToArray()};
            interaction.State=full with{Token="actual-catalog"};interaction.Step(head,hand,false,true);
            check(interaction.Visible.Length==full.Handles.Length,"Every actual model control is accounted for in the layout.");
            var all=interaction.Visible;
            foreach(int side in new[]{-1,1})
            {
                var bank=all.Where(t=>RobotPoseInteraction.ControlBank(t.Control)==side).OrderByDescending(t=>t.Label.Y).ToArray();
                check(bank.Select(t=>t.Control.Key).SequenceEqual(RobotPoseInteraction.SideOrder(bank.Select(t=>t.Control)).Select(h=>h.Key)),"Side labels preserve joint order and thumb/upper/lower hand grouping.");
                for(int i=1;i<bank.Length;i++)check(bank[i-1].Label.Y-bank[i].Label.Y>=bank[i].Height,"Actual side label selection areas do not overlap.");
            }
            check(all.Where(t=>t.Control.Ganged&&RobotPoseInteraction.ControlBank(t.Control)==0).Select(t=>t.Label.Y).Distinct().Count()==all.Count(t=>t.Control.Ganged&&RobotPoseInteraction.ControlBank(t.Control)==0),"All ganged controls have separate upward-extending rows.");
            for(int i=0;i<all.Length;i++)for(int j=i+1;j<all.Length;j++)
                check(Math.Abs(Vector3.Dot(all[i].Label-all[j].Label,new(interaction.Orientation.M11,interaction.Orientation.M12,interaction.Orientation.M13)))>=(all[i].Width+all[j].Width)/2-.00001f || Math.Abs(all[i].Label.Y-all[j].Label.Y)>=(all[i].Height+all[j].Height)/2-.00001f,$"Complete catalog overlap: {all[i].Control.Key} {all[i].Label} {all[i].Width}/{all[i].Height} and {all[j].Control.Key} {all[j].Label} {all[j].Width}/{all[j].Height}");
            check(all.Where(t=>RobotPoseInteraction.ControlBank(t.Control)!=0).Min(t=>t.Label.Y)>=-.01f,"Full robot side labels remain between head and floor.");
            var screenRight=new Vector3(interaction.Orientation.M11,interaction.Orientation.M12,interaction.Orientation.M13);
            var sideBanks=all.Where(t=>RobotPoseInteraction.ControlBank(t.Control)!=0).ToArray();
            check(sideBanks.All(t=>RobotPoseInteraction.ControlBank(t.Control)*Vector3.Dot(t.Label-full.Handles.Aggregate(Vector3.Zero,(sum,h)=>sum+h.Anchor)/full.Handles.Length,screenRight)<0),"Anatomical left/right controls appear on opposite viewer sides.");
            var overhead=all.Where(t=>RobotPoseInteraction.ControlBank(t.Control)==0).ToArray();
            check(overhead.All(t=>!t.Compact),"Upper controls use the side-bank label renderer.");
            check(sideBanks.All(t=>t.Width>.5f),"Side banks have larger physical label widths.");
            float shoulderTop=full.Handles.Where(h=>h.Key.Contains("Shoulder")&&!h.Ganged).Max(h=>h.Anchor.Y);
            check(sideBanks.All(t=>t.Label.Y+t.Height/2<=shoulderTop+.001f&&t.Label.Y-t.Height/2>=-.001f),"Complete side labels fit between shoulder top and true track floor.");
            check(overhead.Where(t=>!t.Control.Ganged).Min(t=>t.Label.Y-t.Height/2)>full.Handles.Max(h=>h.Anchor.Y),"Lowest overhead row starts above the head.");
            check(all.All(t=>Math.Abs(t.Width-sideBanks[0].Width)<.0001f&&Math.Abs(t.Height-sideBanks[0].Height)<.0001f),"Side, body and ganged labels share identical physical size and typography.");
            float X(Vector3 p)=>Vector3.Dot(p,screenRight);
            var viewerLeft=sideBanks.OrderBy(t=>X(t.Label)).First();
            var viewerRight=sideBanks.OrderByDescending(t=>X(t.Label)).First();
            check(overhead.Where(t=>t.Control.Ganged).All(t=>Math.Abs(X(t.Label)-X(viewerLeft.Label))<.001f),"Ganged label left edges align with viewer-left side labels.");
            var shoulders=full.Handles.Where(h=>h.Key.Contains("Shoulder")&&!h.Ganged).ToArray();
            float bodyX=(shoulders.Min(h=>X(h.Anchor))+shoulders.Max(h=>X(h.Anchor)))/2;
            var centralColumns=overhead.Where(t=>!t.Control.Ganged).GroupBy(t=>MathF.Round(X(t.Label),3)).OrderBy(g=>g.Key).ToArray();
            check(centralColumns.Length==2,"Center controls occupy two columns.");
            string[] expectedLeft=["Servo:Microphone_RaiseLower","Servo:NoseBasket","Servo:NoseBody","Servo:NeckTurn","Servo:B_NeckLift","Servo:B_ChestTilt","Servo:B_WaistSwivel","Servo:B_WaistLean","Servo:B_TipToe"];
            string[] expectedRight=["Servo:MFR_Rotate","Servo:MFR_UpDown","Servo:Whip_Antenna_RaiseLower","Servo:Whip_Antenna_Rotate","Servo:B_LaserLower","Servo:URDFStageX","Servo:URDFStageY","Servo:URDFStageHeading"];
            check(centralColumns[0].OrderByDescending(t=>t.Label.Y).Select(t=>t.Control.Key).SequenceEqual(expectedLeft),"Left center column follows the requested order (eight rows plus TipToe under Waist Lean).");
            check(centralColumns[1].OrderByDescending(t=>t.Label.Y).Select(t=>t.Control.Key).SequenceEqual(expectedRight),"Right center column follows the requested eight-row order.");
            var upperGangs=overhead.Where(t=>t.Control.Ganged).OrderByDescending(t=>t.Label.Y).ToArray();
            var expectedGangs=upperGangs.Select(t=>t.Control).OrderByDescending(h=>h.Anchor.Y).ThenBy(h=>h.Name,StringComparer.Ordinal).ToArray();
            int tiltIndex=Array.FindIndex(expectedGangs,h=>h.Key=="Servo:FlapTiltUp");
            if(tiltIndex>=0&&tiltIndex<expectedGangs.Length-1)(expectedGangs[tiltIndex],expectedGangs[tiltIndex+1])=(expectedGangs[tiltIndex+1],expectedGangs[tiltIndex]);
            check(upperGangs.Select(t=>t.Control.Key).SequenceEqual(expectedGangs.Select(h=>h.Key)),"Flap Tilt Up moves down one row while the remaining gangs retain height order.");
            check(upperGangs.Min(t=>t.Label.Y-t.Height/2)>sideBanks.Max(t=>t.Label.Y+t.Height/2)&&upperGangs.Min(t=>t.Label.Y-t.Height/2)-sideBanks.Max(t=>t.Label.Y+t.Height/2)<.06f,"Ganged labels have a small gap above the side labels.");
            check(interaction.DialPosition.Y-.30f-sideBanks.Max(t=>t.Label.Y+t.Height/2)<.06f,"Dial is lowered to a small gap above the side labels.");
            check(interaction.Options.Length==8&&interaction.Options.Count(o=>o.Action==VrPoseAction.Appearance)==5,"All five appearances and three accessories have radio targets.");
            check(Math.Abs(X(interaction.DialPosition)-X(viewerRight.Label))<.001f,"Dial is centered above the viewer-right side bank.");
            check(interaction.DialPosition.Y-.30f>sideBanks.Max(t=>t.Label.Y+t.Height/2),"Dial clears the viewer-right side labels.");
            var normal=new Vector3(interaction.Orientation.M31,interaction.Orientation.M32,interaction.Orientation.M33);
            check(all.All(t=>Math.Abs(Vector3.Dot(interaction.DialPosition-t.Label,normal))<.001f),"Dial and every label share exactly the same depth plane.");
            check(full.Handles.All(h=>PoseControlIcons.For(h.Key)!=null),"Every actual model control has an icon.");
            var fresh=new RobotPoseInteraction{Enabled=true,State=full,RobotHeight=1.8f};fresh.Step(head,hand,false,true);
            check(fresh.Dial!.Control.Key=="Servo:NeckTurn","Neck Turn is selected by default in a fresh pose session.");
            fresh.State=full with{Handles=full.Handles.Select(h=>h.Key=="Servo:NeckTurn"?h with{Value=24}:h).ToArray()};fresh.Step(head,hand,false,true);
            check(fresh.Dial!.Control.Value==24,"Idle persistent dial follows edits made through the desktop editor.");
            var chosen=all.First(t=>t.Control.Key=="Servo:B_OpenLeftHand");
            check(RobotPoseInteraction.ControlBank(chosen.Control)==-1&&chosen.Control.Ganged,"Purple left hand gang is in its anatomical hand bank.");
            check(RobotPoseInteraction.ControlBank(all.First(t=>t.Control.Key=="Servo:B_OpenRightHand").Control)==1,"Right hand gang returns to its own hand bank.");
            Matrix4x4 Aim(Vector3 point)=>Matrix4x4.CreateWorld(point+normal,-normal,Vector3.UnitY);
            interaction.Step(head,Aim(chosen.Label),true,true);
            check(interaction.Dial!.Control.Key==chosen.Control.Key,"Selecting another label replaces the always-active dial selection.");
            interaction.Enabled=false;interaction.Step(head,null,false,true);
            interaction.Enabled=true;interaction.Step(head,Aim(chosen.Label),false,true);
            check(interaction.Dial!.Control.Key==chosen.Control.Key,"Actual hand control remains selected when pose labels reappear.");
            foreach(var option in interaction.Options)
            {
                interaction.Step(head,Aim(option.Center),false,true);
                int beforeOption=requests.Count;interaction.Step(head,Aim(option.Center),true,true);
                check(requests.Count==beforeOption+1&&requests[^1].Action==option.Action&&requests[^1].Value==option.Value,"Radio target sends its exact appearance/accessory value: "+option.Name);
            }
            foreach(var pair in ServoAnimator.PairedBodyControls.All)
            {
                var icon=PoseControlIcons.For(pair.Key);check(icon!=null&&icon.Width==96,"New paired controls have linked bilateral icons.");
                icon!.Save(Path.Combine(Diagnostics.Folder,pair.Key.Replace(':','-')+"-icon.png"));
            }
            var rows=all.OrderBy(t=>RobotPoseInteraction.ControlBank(t.Control)).ThenByDescending(t=>t.Label.Y).ThenBy(t=>t.Label.X)
                .Select(t=>$"{t.Control.Key}\t{t.Control.Name}\t{(RobotPoseInteraction.ControlBank(t.Control)<0?"Left":RobotPoseInteraction.ControlBank(t.Control)>0?"Right":t.Control.Key.StartsWith("Servo:URDFStage")?"Placement":t.Control.Ganged?"Central ganged":"Central")}\t{t.Control.Anchor.Y:F4}\t{t.Label.X:F4}\t{t.Label.Y:F4}");
            File.WriteAllLines(Path.Combine(Diagnostics.Folder,"pose-control-layout.tsv"),new[]{"Key\tName\tSection\tJoint height\tLabel X\tLabel height"}.Concat(rows));
        }
        var waiting=new RobotPoseInteraction{Enabled=true};var waitingOverlay=new Labels();
        using(var labels=new PoseHandleOverlay(waitingOverlay,background:false))
        {
            labels.Draw(waiting,head,Matrix4x4.Identity);
            check(waitingOverlay.Created==1&&waitingOverlay.Uploads==1,"Enabling pose mode before metadata arrives immediately shows a loading indicator.");
            labels.Draw(waiting,head,Matrix4x4.Identity);check(waitingOverlay.Uploads==1,"Unchanged loading status reuses its texture.");
        }
        interaction.State=state with{Token="async-preparation"};interaction.Step(head,Hand(Vector3.Zero),false,true);
        var asynchronous=new Labels();
        using(var labels=new PoseHandleOverlay(asynchronous))
        {
            labels.Draw(interaction,head,Matrix4x4.Identity);
            check(labels.PendingLabels?.Wait(TimeSpan.FromSeconds(5))??true,"Pose graphics finish on the preparation worker.");
            labels.Draw(interaction,head,Matrix4x4.Identity);int uploads=asynchronous.Uploads;
            labels.Draw(interaction,head,Matrix4x4.Identity);
            check(asynchronous.Uploads==uploads,"Prepared labels are reused after asynchronous upload.");
            var at=interaction.Visible[0].Label;interaction.Step(head,Hand(at),true,true);
            check(interaction.DialOpen,"The first trigger press opens the dial immediately.");
            check(interaction.Dial!.Center==interaction.DialPosition,"First press opens the dial at its fixed upper-right label-plane position.");
            labels.Draw(interaction,head,Matrix4x4.Identity);
            check(labels.PendingDial?.Wait(TimeSpan.FromSeconds(5))??true,"First dial artwork finishes on the preparation worker.");
            interaction.Step(head,Hand(at),false,true,new(0,.5f),.02f);
            int beforeDialUpload=asynchronous.Uploads;
            labels.Draw(interaction,head,Matrix4x4.Identity);
            if(asynchronous.Uploads==beforeDialUpload)
            {
                check(labels.PendingDial?.Wait(TimeSpan.FromSeconds(5))??true,"Updated dial artwork completes even if the first image was already uploaded.");
                labels.Draw(interaction,head,Matrix4x4.Identity);
            }
            check(asynchronous.Uploads>beforeDialUpload,"Dial artwork remains responsive while the control value changes during preparation.");
        }
        check(ServoAnimator.AnimationNames.Words("BrowLeftTopOpen")=="Flap Left Top Open","Historical child names display as Flap through the shared formatter.");
        check(ServoAnimator.ControlLabels.Text("Browse…")=="Browse…","Control naming does not change Browse buttons.");
        var surface=new RobotSurfacePicker();
        var mesh=new VrMesh(1,false,[-.5f,-.5f,0,.5f,-.5f,0,0,.5f,0],new float[9],new float[6],[0,1,2]);
        var frame=new VrFrame(0,"","",0,1,0,[new VrNode(1,1,1,Matrix4x4.CreateTranslation(0,0,-2),true)], [mesh], [new VrMaterial(1,Vector4.One,Vector4.Zero,Vector4.Zero,1,0)],[],[]);
        surface.Update(frame,Matrix4x4.Identity);
        check(surface.Hit(Vector3.Zero,-Vector3.UnitZ) is {} hitPoint&&Math.Abs(hitPoint.Z+2)<.0001f,"Robot surface ray hits transformed mesh triangles.");
        check(surface.Hit(new(2,0,0),-Vector3.UnitZ)==null,"Robot surface picking rejects empty space.");
        check(surface.Hit(new(.45f,.45f,0),-Vector3.UnitZ)==null,"Picking tests triangles rather than selecting empty corners of their bounds.");
        surface.Update(frame,Matrix4x4.CreateScale(2));
        check(surface.Hit(Vector3.Zero,-Vector3.UnitZ) is {} scaledPoint&&Math.Abs(scaledPoint.Z+4)<.0001f,"Picking preserves world distance through scaled model transforms.");
        surface.Update(frame with{Nodes=[frame.Nodes[0] with{Robot=false}]},Matrix4x4.Identity);
        check(surface.Hit(Vector3.Zero,-Vector3.UnitZ)==null,"Scenery is excluded from robot control picking.");
        surface.Update(frame,Matrix4x4.Identity);
        check(surface.Hit(Vector3.Zero,Vector3.UnitZ)==null,"Robot surface picking rejects geometry behind the pointer.");
        var pointing=new RobotPoseInteraction{Enabled=true,State=state,HitRobot=(_,_)=>control.Anchor};
        pointing.Step(head,Hand(new Vector3(4,0,-2)),false,true);
        check(pointing.Hovered==control.Key&&pointing.ExpandedLabel is {} big&&big.Width>pointing.Visible[0].Width,"Pointing at robot geometry highlights and enlarges its nearby control.");
        pointing.Step(head,Hand(new Vector3(4,0,-2)),true,true);
        check(pointing.Dial!.Control.Key==control.Key,"Trigger on a pointed robot part selects its dial control.");
        pointing.HitRobot=null;pointing.Step(head,Hand(pointing.Visible[0].Label),false,true);
        var normalLabel=pointing.Visible[0];
        var wider=normalLabel.Label+new Vector3(normalLabel.Width*.6f,0,0);
        pointing.Step(head,Hand(wider),false,true);
        check(pointing.ExpandedLabel!=null,"Expanded label keeps its larger pointer selection area.");
        pointing.Step(head,Hand(new Vector3(4,0,-2)),false,true);
        check(pointing.ExpandedLabel==null,"Leaving a label returns it to normal size.");
        var viewerConfig=ViewerSettings.Load().ConfigFolder;string fullModel=Path.Combine(viewerConfig,"Models/Robots/Johnny5FullBody/johnny5_head.urdf");
        if(File.Exists(fullModel))
        {
            var rig=new AnimationRig(fullModel,viewerConfig);var calibration=ServoAnimator.ConfigurationStore.LoadUrdf(viewerConfig,fullModel);
            foreach(var pair in ServoAnimator.PairedBodyControls.All)
            {
                var first=new PlaybackSequence("","paired",2,[new MotionTrack(pair.Left.ToString(),"",[new(0,-20,false)],false),new MotionTrack(pair.Servo.ToString(),"",[new(1,40,false)],false)],[],false,"",[]);
                using var transport=new PlaybackPlayer(()=>0);transport.Load(new PlaybackShow("paired movie",[first,first with{Tracks=[]}],[]));transport.Next();
                var carried=transport.Pose();
                check(carried["servo:"+pair.Servo].Stamp>carried["servo:"+pair.Left].Stamp,"Movie carry preserves a later paired command over an earlier member command.");
                var pairedPose=new Dictionary<string,(double Value,double Stamp,string Servo)>{{"servo:"+pair.Servo,(40,0,pair.Servo.ToString())}};
                rig.Apply(pairedPose);var expectedPalette=rig.Palette.ToArray();
                var direct=ServoAnimator.PairedBodyControls.Resolve(pair,calibration,40).ToDictionary(m=>"servo:"+m.Servo,m=>(m.Value,0d,m.Servo.ToString()));
                rig.Apply(direct);check(rig.Palette.SequenceEqual(expectedPalette),"Saved paired command renders like calibrated left/right commands in VR playback.");
                pairedPose["servo:"+pair.Left]=(-25,1,pair.Left.ToString());rig.Apply(pairedPose);expectedPalette=rig.Palette.ToArray();
                direct["servo:"+pair.Left]=(-25,1,pair.Left.ToString());rig.Apply(direct);
                check(rig.Palette.SequenceEqual(expectedPalette),"VR playback preserves later individual refinements of paired commands.");
            }
        }
        interaction.State=state with{Editable=false};interaction.Step(head,hand,true,true);
        check(interaction.Visible.Length==0&&!interaction.Grabbing&&!interaction.DialOpen,"Playback/modal state hides controls and prevents adjustment.");
    }

    private sealed class Labels:IPanelOverlay
    {
        internal int Uploads,Destroyed,Created;internal byte[] Pixels=[];
        public Valve.VR.EVROverlayError CreateOverlay(string key,string name,ref ulong h){h=(ulong)++Created;return 0;}
        public Valve.VR.EVROverlayError SetOverlayWidthInMeters(ulong h,float width)=>0;
        public Valve.VR.EVROverlayError SetOverlayInputMethod(ulong h,Valve.VR.VROverlayInputMethod method)=>0;
        public Valve.VR.EVROverlayError SetOverlaySortOrder(ulong h,uint order)=>0;
        public Valve.VR.EVROverlayError SetOverlayRaw(ulong h,IntPtr data,uint width,uint height,uint depth){Uploads++;Pixels=new byte[width*height*depth];System.Runtime.InteropServices.Marshal.Copy(data,Pixels,0,Pixels.Length);return 0;}
        public Valve.VR.EVROverlayError SetOverlayTransformAbsolute(ulong h,Valve.VR.ETrackingUniverseOrigin origin,ref Valve.VR.HmdMatrix34_t pose)=>0;
        public Valve.VR.EVROverlayError ShowOverlay(ulong h)=>0;
        public Valve.VR.EVROverlayError HideOverlay(ulong h)=>0;
        public Valve.VR.EVROverlayError DestroyOverlay(ulong h){Destroyed++;return 0;}
        public void Dispose(){}
    }
}
