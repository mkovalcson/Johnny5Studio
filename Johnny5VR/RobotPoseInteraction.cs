using System.Numerics;
using Johnny5.VrBridge;

namespace Johnny5VR;
internal sealed record PoseTarget(VrPoseHandle Control,Vector3 Label,float Width=.42f,float Height=.062f,bool Compact=false);
internal sealed record PoseOption(string Name,VrPoseAction Action,int Value,Vector3 Center,float Width,float Height);
internal sealed record PoseDial(VrPoseHandle Control,Vector3 Center,float Minimum,float Maximum);
internal sealed class RobotPoseInteraction
{
    internal VrPoseState? State;
    internal bool Enabled;
    internal float FloorY,RobotHeight=1.8f;
    internal Action<VrPoseRequest>? Send;
    internal Action? Feedback;
    internal Func<Vector3,Vector3,Vector3?>? HitRobot;
    internal PoseTarget? ExpandedLabel {get;private set;}
    internal PoseTarget[] Visible {get;private set;}=[];
    internal string? Hovered {get;private set;}
    internal bool Grabbing {get;private set;}
    internal bool DialOpen=>Dial!=null;
    internal string StatusMessage => !Enabled?"":State==null||State.Handles.Length==0?"Loading robot controls…":State.AppearanceBusy?"Preparing appearance…":!State.Editable?"Pause playback / close dialogs to pose":"";
    internal PoseDial? Dial {get;private set;}
    internal Matrix4x4 Orientation {get;private set;}=Matrix4x4.Identity;
    internal (Vector3 Start,Vector3 End)? Pointer {get;private set;}
    private PoseTarget[] layout=[];
    private string context="";
    private Vector3 right=Vector3.UnitX,up=Vector3.UnitY,center;
    private float lastValue,lastAngle;
    private Vector3 dialPosition;
    private string selectedKey="Servo:NeckTurn";
    private string? pendingPreview;
    internal Vector3 DialPosition=>dialPosition;
    internal PoseOption[] Options {get;private set;}=[];
    internal Vector3 OptionsCenter=>dialPosition+up*.455f;
    internal const float OptionsWidth=.64f,OptionsHeight=.27f;
    private bool wasDown;
    internal static (float Minimum,float Maximum) DialRange(VrPoseHandle h)
    {
        float span=h.Key switch{"Servo:URDFStageX" or "Servo:URDFStageY"=>200,"Servo:URDFStageHeading"=>360,_=>h.Maximum-h.Minimum};
        if(span>=h.Maximum-h.Minimum)return(h.Minimum,h.Maximum);
        float min=Math.Clamp(h.Value-span/2,h.Minimum,h.Maximum-span);return(min,min+span);
    }
    internal static float DefaultValue(VrPoseHandle h)=>Math.Clamp(0,h.Minimum,h.Maximum);
    internal static float AngleValue(float angle,float minimum,float maximum)=>minimum+(Math.Clamp(angle,-135,135)+135)/270*(maximum-minimum);
    internal static float ValueAngle(float value,float minimum,float maximum)=>Math.Clamp((value-minimum)/(maximum-minimum),0,1)*270-135;
    internal static Vector2? PlanePoint(Vector3 center,Vector3 from,Vector3 direction,Vector3 right,Vector3 up)
    {
        var normal=Vector3.Cross(right,up);float denom=Vector3.Dot(direction,normal);if(Math.Abs(denom)<.0001f)return null;
        float d=Vector3.Dot(center-from,normal)/denom;if(d<=0||d>8)return null;
        var p=from+direction*d-center;return new(Vector3.Dot(p,right),Vector3.Dot(p,up));
    }
    internal static PoseTarget? Pick(IEnumerable<PoseTarget> targets,Vector3 from,Vector3 direction,Vector3 right,Vector3 up)=>
        targets.Select(t=>(Target:t,Point:PlanePoint(t.Label,from,direction,right,up)))
        .Where(t=>t.Point is {} p&&Math.Abs(p.X)<=t.Target.Width/2&&Math.Abs(p.Y)<=t.Target.Height/2).Select(t=>t.Target).FirstOrDefault();
    // Side-specific individual controls follow their named side. Gangs have a
    // separate upper-left bank; central controls remain above the head.
    internal static int ControlBank(VrPoseHandle h)
    {
        if(h.Key=="Servo:B_OpenLeftHand")return -1;
        if(h.Key=="Servo:B_OpenRightHand")return 1;
        if(h.Ganged)return 0;
        string name=h.Key.Split(':')[^1];
        if(h.Key is "Servo:NeckTiltRight" or "Servo:EyesHorizontalRight" || h.Key.StartsWith("Gang:",StringComparison.Ordinal))return 0;
        if(name.Contains("Left",StringComparison.Ordinal))return -1;
        if(name.Contains("Right",StringComparison.Ordinal))return 1;
        return 0;
    }
    internal static VrPoseHandle[] SideOrder(IEnumerable<VrPoseHandle> source)
    {
        int Group(VrPoseHandle h)=>h.Key.Contains("OpenLeftHand")||h.Key.Contains("OpenRightHand")?1:h.Key.Contains("Thumb")?2:h.Key.Contains("Finger")?(h.Key.Contains("Upper")?3:4):0;
        return source.OrderBy(Group).ThenByDescending(h=>Group(h)==0?h.Anchor.Y:0)
            .ThenBy(h=>Group(h)==0?0:h.Key.Contains("Spread")?0:h.Key.Contains("End")?3:h.Key.Contains("Tip")?2:1)
            .ThenBy(h=>h.Name,StringComparer.Ordinal).ToArray();
    }
    private PoseTarget? PointedControl(Vector3 from,Vector3 direction,string? previous)
    {
        var hit=Pick(Visible,from,direction,right,up);
        if(hit==null&&previous!=null&&Visible.FirstOrDefault(t=>t.Control.Key==previous) is {} prior)
            hit=Pick([prior with{Width=prior.Width*1.35f,Height=prior.Height*1.35f}],from,direction,right,up) is not null?prior:null;
        if(hit==null&&HitRobot?.Invoke(from,direction) is {} point)
        {
            // Prefer an individual control near the hit surface over its parent gang.
            hit=Visible.Where(t=>!t.Control.Ganged&&!t.Control.Key.StartsWith("Servo:URDFStage"))
                .OrderBy(t=>Vector3.DistanceSquared(t.Control.Anchor,point)).FirstOrDefault();
            if(hit!=null&&Vector3.Distance(hit.Control.Anchor,point)>.25f*RobotHeight/1.8f)hit=null;
            if(hit!=null)Pointer=(from,point);
        }
        if(hit!=null)ExpandedLabel=hit with{Width=hit.Width*1.35f,Height=hit.Height*1.35f};
        return hit;
    }
    private void MakeLayout(VrPoseState state,Matrix4x4 head)
    {
        context=state.Token;Dial=null;Grabbing=false;
        if(state.Handles.Length==0){layout=[];return;}
        center=state.Handles.Aggregate(Vector3.Zero,(p,h)=>p+h.Anchor)/state.Handles.Length;
        center.Y=state.Handles.Max(h=>h.Anchor.Y);
        var toward=head.Translation-center;toward.Y=0;
        if(toward.LengthSquared()<.001f)toward=Vector3.UnitZ;
        toward=Vector3.Normalize(toward);right=Vector3.Normalize(Vector3.Cross(Vector3.UnitY,toward));up=Vector3.UnitY;
        Orientation=new(right.X,right.Y,right.Z,0,up.X,up.Y,up.Z,0,toward.X,toward.Y,toward.Z,0,0,0,0,1);
        var targets=new List<PoseTarget>();
        // Use actual joint height, captured once per editor context, so labels do not
        // shuffle while posing. Side-specific body joints belong beside that side too.
        float floor=FloorY;
        var shoulders=state.Handles.Where(h=>h.Key.Contains("Shoulder",StringComparison.Ordinal)&&!h.Ganged).ToArray();
        float robotScale=Math.Clamp(RobotHeight/1.8f,.35f,3);
        float leftEdge=shoulders.Select(h=>Vector3.Dot(h.Anchor-center,right)).DefaultIfEmpty(-.19f*robotScale).Min()-.16f*robotScale;
        float rightEdge=shoulders.Select(h=>Vector3.Dot(h.Anchor-center,right)).DefaultIfEmpty(.19f*robotScale).Max()+.16f*robotScale;
        float shoulderCenter=(leftEdge+rightEdge)/2;
        float shoulderTop=shoulders.Select(h=>h.Anchor.Y).DefaultIfEmpty(center.Y-.35f*robotScale).Max();
        float width=.62f*robotScale;
        int sideCount=Math.Max(1,new[]{-1,1}.Max(side=>state.Handles.Count(h=>ControlBank(h)==side)));
        float spacing=Math.Max(.025f*robotScale,(shoulderTop-floor)/Math.Max(1,sideCount-.1f));
        float labelHeight=Math.Min(.09f*robotScale,spacing*.90f);
        float shift=.05f*robotScale;
        float viewerLeft=leftEdge-width/2-.19f*robotScale+shift;
        float viewerRight=rightEdge+width/2+.27f*robotScale+shift;
        shoulderCenter+=shift;
        // Keep the two full-size center columns clear of the flanking banks.
        viewerLeft=Math.Min(viewerLeft,shoulderCenter-width*1.5f-.06f*robotScale);
        viewerRight=Math.Max(viewerRight,shoulderCenter+width*1.5f+.06f*robotScale);
        foreach(int side in new[]{-1,1})
        {
            var controls=SideOrder(state.Handles.Where(h=>ControlBank(h)==side));
            // Anatomical left appears on the viewer's right when facing the robot.
            int screenSide=-side;
            float x=screenSide>0?viewerRight:viewerLeft;
            for(int i=0;i<controls.Length;i++)
                targets.Add(new(controls[i],center+right*x+up*(shoulderTop-center.Y-spacing*.45f-i*spacing)+toward*.10f,width,labelHeight));
        }
        var middle=state.Handles.Where(h=>ControlBank(h)==0).ToArray();
        var gangs=middle.Where(h=>h.Ganged).OrderByDescending(h=>h.Anchor.Y).ThenBy(h=>h.Name,StringComparer.Ordinal).ToArray();
        int tilt=Array.FindIndex(gangs,h=>h.Key=="Servo:FlapTiltUp");
        if(tilt>=0&&tilt<gangs.Length-1)(gangs[tilt],gangs[tilt+1])=(gangs[tilt+1],gangs[tilt]);
        var body=middle.Where(h=>!h.Ganged && h.Key is not ("Servo:URDFStageX" or "Servo:URDFStageY" or "Servo:URDFStageHeading"))
            .OrderByDescending(h=>h.Anchor.Y).ThenBy(h=>h.Name,StringComparer.Ordinal).ToArray();
        var placement=middle.Where(h=>h.Key is "Servo:URDFStageX" or "Servo:URDFStageY" or "Servo:URDFStageHeading").ToArray();
        float pitch=labelHeight/.90f;
        void Column(VrPoseHandle[] controls,float x,float bottom)
        {
            for(int i=0;i<controls.Length;i++)
                targets.Add(new(controls[i],center+right*x+up*(bottom-center.Y+labelHeight/2+(controls.Length-1-i)*pitch)+toward*.10f,width,labelHeight));
        }
        Column(gangs,viewerLeft,shoulderTop+.025f*robotScale);
        string[] leftOrder=["Microphone_RaiseLower","NoseBasket","NoseBody","NeckTurn","B_NeckLift","B_ChestTilt","B_WaistSwivel","B_WaistLean","B_TipToe"];
        string[] rightOrder=["MFR_Rotate","MFR_UpDown","Whip_Antenna_RaiseLower","Whip_Antenna_Rotate","B_LaserLower","URDFStageX","URDFStageY","URDFStageHeading"];
        var central=body.Concat(placement).ToArray();
        VrPoseHandle[] Ordered(string[] order)=>order.Select(key=>central.FirstOrDefault(h=>h.Key=="Servo:"+key)).OfType<VrPoseHandle>().ToArray();
        var leftControls=Ordered(leftOrder);var rightControls=Ordered(rightOrder);
        var extras=central.Where(h=>!leftControls.Contains(h)&&!rightControls.Contains(h)).ToArray();
        float gap=.025f*robotScale;
        Column(leftControls.Concat(extras).ToArray(),shoulderCenter-(width+gap)/2,center.Y+.06f*robotScale);
        Column(rightControls,shoulderCenter+(width+gap)/2,center.Y+.06f*robotScale);
        dialPosition=center+right*viewerRight+up*(shoulderTop-center.Y+.025f*robotScale+.30f)+toward*.10f;
        var options=new List<PoseOption>();
        void Option(string name,VrPoseAction action,int value,float x,float y,float w)
            =>options.Add(new(name,action,value,OptionsCenter+right*x+up*y,w,.065f));
        Option("Normal",VrPoseAction.Appearance,0,-.213f,.085f,.213f);
        Option("Light wear",VrPoseAction.Appearance,1,0,.085f,.213f);
        Option("Medium wear",VrPoseAction.Appearance,2,.213f,.085f,.213f);
        Option("Outdoor weathered",VrPoseAction.Appearance,3,-.12f,.01f,.40f);
        Option("Gold",VrPoseAction.Appearance,4,.20f,.01f,.24f);
        Option("None",VrPoseAction.Accessory,0,-.213f,-.085f,.213f);
        Option("Laser",VrPoseAction.Accessory,1,0,-.085f,.213f);
        Option("Toolbox",VrPoseAction.Accessory,2,.213f,-.085f,.213f);
        Options=options.ToArray();
        layout=targets.ToArray();
    }
    private void Select(VrPoseHandle control)
    {
        selectedKey=control.Key;lastValue=control.Value;pendingPreview=null;
        var range=DialRange(control);Dial=new(control,dialPosition,range.Minimum,range.Maximum);
    }
    private void SetValue(float value)
    {
        if(Dial==null)return;
        value=Math.Clamp(value,Dial.Control.Minimum,Dial.Control.Maximum);
        if(Math.Abs(value-lastValue)<.001f)return;
        lastValue=value;Dial=Dial with{Control=Dial.Control with{Value=value}};
        pendingPreview=Guid.NewGuid().ToString();
        Send?.Invoke(new(pendingPreview,context,VrPoseAction.Set,Dial.Control.Key,value));
    }
    internal bool Step(Matrix4x4 head,Matrix4x4? hand,bool down,bool allowed,Vector2 fine=default,float seconds=0)
    {
        Pointer=null;ExpandedLabel=null;
        try
        {
            if(!Enabled||State is not {Editable:true} state||hand==null||!allowed||!EditorPanelLayout.Finite(head)||!EditorPanelLayout.Finite(hand.Value))
            {Dial=null;Grabbing=false;Visible=[];Hovered=null;context="";return false;}
            var from=hand.Value.Translation;var direction=Vector3.Normalize(Vector3.TransformNormal(-Vector3.UnitZ,hand.Value));
            if(context!=state.Token)MakeLayout(state,head);
            var current=state.Handles.ToDictionary(h=>h.Key);
            layout=layout.Where(t=>current.ContainsKey(t.Control.Key)).Select(t=>t with{Control=current[t.Control.Key]}).ToArray();Visible=layout;
            if(Dial==null&&current.Count>0)
                Select(current.GetValueOrDefault(selectedKey)??current.GetValueOrDefault("Servo:NeckTurn")??current.Values.First());
            var option=Options.FirstOrDefault(o=>PlanePoint(o.Center,from,direction,right,up) is {} p&&Math.Abs(p.X)<=o.Width/2&&Math.Abs(p.Y)<=o.Height/2);
            if(option!=null&&!Grabbing)
            {
                Pointer=(from,option.Center);
                if(down&&!wasDown){Send?.Invoke(new(Guid.NewGuid().ToString(),context,option.Action,Value:option.Value));Feedback?.Invoke();}
                return true;
            }
            if(Dial is {} dial)
            {
                if(!current.ContainsKey(dial.Control.Key)){Dial=null;Grabbing=false;return false;}
                if(pendingPreview==state.LastRequest)pendingPreview=null;
                if(!Grabbing&&pendingPreview==null)
                {
                    lastValue=current[dial.Control.Key].Value;
                    Dial=dial=dial with{Control=current[dial.Control.Key]};
                }
                var labelHit=PointedControl(from,direction,Hovered);
                Hovered=labelHit?.Control.Key;
                var point=PlanePoint(dial.Center,from,direction,right,up);
                bool inside=point is {} q&&Math.Abs(q.X)<=.30f&&Math.Abs(q.Y)<=.30f;
                if(inside){Pointer=(from,dial.Center+right*point!.Value.X+up*point.Value.Y);Hovered=dial.Control.Key;}
                else if(labelHit!=null&&Pointer==null)Pointer=(from,labelHit.Label);
                if(Grabbing&&!down){Grabbing=false;Visible=layout;return true;}
                if(inside&&down&&!wasDown)
                {
                    var p=point!.Value;
                    if(p.Y<-.225f)
                    {
                        if(p.X<-.10f||p.X>.10f)
                        {
                            float width=dial.Maximum-dial.Minimum,shift=p.X<0?-width/2:width/2;
                            float min=Math.Clamp(dial.Minimum+shift,dial.Control.Minimum,dial.Control.Maximum-width);
                            Dial=dial with{Minimum=min,Maximum=min+width};
                        }
                        else SetValue(DefaultValue(dial.Control));
                        return true;
                    }
                    Grabbing=true;lastAngle=ValueAngle(lastValue,dial.Minimum,dial.Maximum);Feedback?.Invoke();
                }
                else if(down&&!wasDown&&!inside&&labelHit is {} other)
                {
                    Select(other.Control);Feedback?.Invoke();
                }
                if(Grabbing)
                {
                    Visible=[];ExpandedLabel=null;
                    if(point is {} p)
                    {
                        p.Y-=.015f;
                        if(p.Length()>=.055f)
                        {
                            float angle=MathF.Atan2(p.X,p.Y)*180/MathF.PI;
                            // The bottom gap is an end stop, never a maximum-to-minimum jump.
                            if(angle-lastAngle>180)angle-=360;else if(angle-lastAngle< -180)angle+=360;
                            lastAngle=Math.Clamp(angle,-135,135);SetValue(AngleValue(lastAngle,dial.Minimum,dial.Maximum));
                        }
                    }
                }
                else if((inside||labelHit?.Control.Key==dial.Control.Key)&&Math.Abs(fine.Y)>.25f&&seconds>0)
                {SetValue(lastValue+fine.Y*(dial.Maximum-dial.Minimum)*.025f*Math.Min(seconds,.05f));Visible=[];ExpandedLabel=null;}
                return inside||Grabbing||labelHit!=null;
            }
            var hit=PointedControl(from,direction,Hovered);Hovered=hit?.Control.Key;
            if(hit==null)return false;
            Pointer=(from,hit.Label);
            if(down&&!wasDown)
            {
                Select(hit.Control);Feedback?.Invoke();
            }
            return down;
        }
        finally{wasDown=down;}
    }
    internal void Insert()=>Action(VrPoseAction.Insert);
    internal void Discard()=>Action(VrPoseAction.Discard);
    private void Action(VrPoseAction action)
    {if(State is {Editable:true} state)Send?.Invoke(new(Guid.NewGuid().ToString(),state.Token,action));}
}
