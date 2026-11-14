using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Valve.VR;

namespace Johnny5VR;

internal sealed class ActionControllerInput
{
    private readonly CVRInput input;
    private ulong move,turn,menu,select,back,grab,leftSource,rightSource;
    private readonly VRActiveActionSet_t[] sets=new VRActiveActionSet_t[1];
    internal CVRInput Input=>input;
    internal ActionControllerInput(bool registerApplication=true)
    {
        input=OpenVR.Input??throw new InvalidOperationException("SteamVR Input is unavailable.");
        var apps=OpenVR.Applications;
        if(registerApplication&&apps!=null)
        {
            var error=apps.AddApplicationManifest(Path.Combine(AppContext.BaseDirectory,"Input/viewer.vrmanifest"),true);
            if(error==EVRApplicationError.None)error=apps.IdentifyApplication((uint)Environment.ProcessId,"local.johnny5vr.viewer");
            // Registration is useful for the binding UI, but a temporary app
            // identity problem must not permanently disable the input service.
            if(error!=EVRApplicationError.None)Diagnostics.Log(new InvalidOperationException("Controller application identity: "+error));
        }
        Check(input.SetActionManifestPath(Path.Combine(AppContext.BaseDirectory,"Input/actions.json")));
        ulong set=0;Check(input.GetActionSetHandle("/actions/navigation",ref set));sets[0].ulActionSet=set;
        Check(input.GetActionHandle("/actions/navigation/in/move",ref move));
        Check(input.GetActionHandle("/actions/navigation/in/turn",ref turn));
        Check(input.GetActionHandle("/actions/navigation/in/menu",ref menu));
        Check(input.GetActionHandle("/actions/navigation/in/select",ref select));
        Check(input.GetActionHandle("/actions/navigation/in/back",ref back));
        Check(input.GetActionHandle("/actions/navigation/in/grab",ref grab));
        Check(input.GetInputSourceHandle("/user/hand/left",ref leftSource));
        Check(input.GetInputSourceHandle("/user/hand/right",ref rightSource));
    }
    private static void Check(EVRInputError error){if(error!=EVRInputError.None)throw new InvalidOperationException("SteamVR controller input: "+error);}
    internal ControllerReading Read()
    {
        Check(input.UpdateActionState(sets,(uint)Marshal.SizeOf<VRActiveActionSet_t>()));
        var m=new InputAnalogActionData_t();var t=new InputAnalogActionData_t();
        Check(input.GetAnalogActionData(move,ref m,(uint)Marshal.SizeOf<InputAnalogActionData_t>(),OpenVR.k_ulInvalidInputValueHandle));
        Check(input.GetAnalogActionData(turn,ref t,(uint)Marshal.SizeOf<InputAnalogActionData_t>(),OpenVR.k_ulInvalidInputValueHandle));
        var la=Digital(menu,leftSource);var lb=Digital(back,leftSource);var ra=Digital(menu,rightSource);var rb=Digital(back,rightSource);
        var lc=Digital(grab,leftSource);var lt=Digital(select,leftSource);
        var a=Digital(menu);var trigger=Digital(select);var b=Digital(back);var g=Digital(grab);
        string State((bool Active,bool Down) value)=>!value.Active?"unbound":value.Down?"down":"up";
        string buttons=$"A:{State(a)} B:{State(b)} Trigger:{State(trigger)}";
        return new(m.bActive?new(m.x,m.y):Vector2.Zero,t.bActive?new(t.x,t.y):Vector2.Zero,
            (m.bActive?"action bound":"action not bound")+" · "+buttons,t.bActive?"action bound":"action not bound",m.bActive||t.bActive||a.Active||trigger.Active||b.Active,"",a.Down,trigger.Down,b.Down,g.Down){LeftTrigger=lt.Down,BoundLeftTrigger=lt.Active,LeftStickClick=lc.Down,BoundLeftStickClick=lc.Active,LeftA=la.Down,LeftB=lb.Down,RightA=ra.Down,RightB=rb.Down,BoundLeftA=la.Active,BoundLeftB=lb.Active,BoundRightA=ra.Active,BoundRightB=rb.Active,ButtonStatus=buttons,BoundMenu=a.Active,BoundSelect=trigger.Active,BoundBack=b.Active,BoundGrab=g.Active};
    }
    private (bool Active,bool Down) Digital(ulong action,ulong source=0){var state=new InputDigitalActionData_t();Check(input.GetDigitalActionData(action,ref state,(uint)Marshal.SizeOf<InputDigitalActionData_t>(),source));return(state.bActive,state.bActive&&state.bState);}

}

internal sealed record ControllerReading(Vector2 Move,Vector2 Right,string LeftStatus,string RightStatus,bool Active,string Error,bool Menu=false,bool Select=false,bool Back=false,bool Grab=false)
{
    internal bool LeftTrigger {get;init;} internal bool BoundLeftTrigger {get;init;}
    internal bool LeftStickClick {get;init;} internal bool BoundLeftStickClick {get;init;}
    internal bool LeftA {get;init;} internal bool LeftB {get;init;} internal bool RightA {get;init;} internal bool RightB {get;init;}
    internal bool BoundLeftA {get;init;} internal bool BoundLeftB {get;init;} internal bool BoundRightA {get;init;} internal bool BoundRightB {get;init;}
    internal ControllerReading ForFollowing()=>this with{Menu=RightA,Back=RightB};
    internal string ButtonStatus {get;init;}="";
    internal bool BoundMenu {get;init;}
    internal bool BoundSelect {get;init;}
    internal bool BoundBack {get;init;}
    internal bool BoundGrab {get;init;}
    internal static ControllerReading Failed(string message)=>new(Vector2.Zero,Vector2.Zero,"waiting","waiting",false,message);
}

internal sealed class ControllerInput
{
    private readonly CVRSystem system;
    private readonly bool useActions;
    private ActionControllerInput? actions;
    private long retryAfter;
    private string lastError="";
    private readonly Dictionary<uint,bool> indexDevices=new();
    private readonly Dictionary<uint,bool> triggerStates=new();
    private readonly ControllerInputRoute route=new();
    internal bool ActionStateReady {get;private set;}
    internal CVRInput? ActionInput=>ActionStateReady?actions?.Input:null;
    internal string Mode=>useActions?"SteamVR bindings":route.BoundForTracking?"Direct sticks + SteamVR buttons/fingers":"Direct Index";
    internal ControllerInput(CVRSystem system,bool useActions)
    {
        this.system=system;this.useActions=useActions;
        if(!useActions&&OpenVR.Applications is {} apps)
        {
            var error=apps.AddApplicationManifest(Path.Combine(AppContext.BaseDirectory,"Input/viewer-direct.vrmanifest"),true);
            if(error==EVRApplicationError.None)error=apps.IdentifyApplication((uint)Environment.ProcessId,"local.johnny5vr.viewer.direct");
            if(error!=EVRApplicationError.None)Diagnostics.Log(new InvalidOperationException("Direct input application identity: "+error));
        }
    }
    internal ControllerReading Read(bool tracking=false)
    {
        route.EnableTracking(tracking);ActionStateReady=false;
        ControllerReading Direct()
        {
            var left=ReadHand(ETrackedControllerRole.LeftHand);var right=ReadHand(ETrackedControllerRole.RightHand);
            return new(left.Stick,right.Stick,left.Status,right.Status,left.Active||right.Active,"",left.Menu||right.Menu,left.Select||right.Select,left.Back||right.Back,left.Grab||right.Grab){LeftTrigger=left.Select,LeftStickClick=left.StickClick,LeftA=left.Menu,LeftB=left.Back,RightA=right.Menu,RightB=right.Back};
        }
        ControllerReading Bound()
        {
            if(Environment.TickCount64<retryAfter)return ControllerReading.Failed(lastError+" (retrying)");
            try
            {
                // Keep the process's existing app identity in direct mode. All actions
                // then belong to the same app and are updated exactly once per frame.
                actions??=new ActionControllerInput(registerApplication:useActions);
                var value=actions.Read();ActionStateReady=true;lastError="";return value;
            }
            catch(Exception ex)
            {
                if(lastError!=ex.Message)Diagnostics.Log(ex);
                lastError=ex.Message;retryAfter=Environment.TickCount64+2000;actions=null;return ControllerReading.Failed(lastError);
            }
        }
        try{return route.Read(useActions,Direct,Bound);}
        catch(Exception ex){Diagnostics.Log(ex);return ControllerReading.Failed(ex.Message);}
    }
    private (Vector2 Stick,string Status,bool Active,bool Menu,bool Select,bool Back,bool Grab,bool StickClick) ReadHand(ETrackedControllerRole role)
    {
        uint device=system.GetTrackedDeviceIndexForControllerRole(role);
        if(device==OpenVR.k_unTrackedDeviceIndexInvalid||!system.IsTrackedDeviceConnected(device))return (Vector2.Zero,"not connected",false,false,false,false,false,false);
        var state=new VRControllerState_t();
        if(!system.GetControllerState(device,ref state,(uint)Marshal.SizeOf<VRControllerState_t>()))return (Vector2.Zero,$"device {device}: no state",false,false,false,false,false,false);
        if(!indexDevices.TryGetValue(device,out bool index))
        {
            var error=ETrackedPropertyError.TrackedProp_Success;var name=new StringBuilder(256);
            system.GetStringTrackedDeviceProperty(device,ETrackedDeviceProperty.Prop_ControllerType_String,name,256,ref error);
            index=name.ToString().Contains("knuckles",StringComparison.OrdinalIgnoreCase)||name.ToString().Contains("index",StringComparison.OrdinalIgnoreCase);
            indexDevices[device]=index;
        }
        var buttons=DecodeButtons(state.ulButtonPressed,index);
        var types=new int[5];
        for(int i=0;i<5;i++)
        {
            var error=ETrackedPropertyError.TrackedProp_Success;
            types[i]=system.GetInt32TrackedDeviceProperty(device,(ETrackedDeviceProperty)((int)ETrackedDeviceProperty.Prop_Axis0Type_Int32+i),ref error);
            if(error!=ETrackedPropertyError.TrackedProp_Success)types[i]=0;
        }
        bool trigger=DecodeTrigger(state,types,index,triggerStates.GetValueOrDefault(device));triggerStates[device]=trigger;
        int axis=FindNavigationAxis(types);
        string buttonStatus=$" · buttons 0x{state.ulButtonPressed:X} · A {(buttons.Menu?"down":"up")}";
        if(axis<0)return (Vector2.Zero,$"device {device}: no 2D axis [{string.Join(',',types)}]"+buttonStatus,false,buttons.Menu,trigger,buttons.Back,buttons.Grab,false);
        string mapping=types[axis]==(int)EVRControllerAxisType.k_eControllerAxis_TrackPad?"legacy 2D":"joystick";
        return (Axis(state,axis),$"device {device} · axis {axis} ({mapping})"+buttonStatus,true,buttons.Menu,trigger,buttons.Back,buttons.Grab,(state.ulButtonPressed&(1UL<<(32+axis)))!=0);
    }
    internal static (bool Menu,bool Select,bool Back,bool Grab) DecodeButtons(ulong pressed,bool index)
    {
        bool Press(int id)=>(pressed&(1UL<<id))!=0;
        // OpenVR aliases Index A to Grip (2), not generic A (7).
        // Index squeeze uses Axis2; treating bit 2 as grab steals the menu button.
        return (Press(7)||(index&&Press(2)),Press(33),Press(1),
            (index?Press(34):Press(2))||Press(32)||Press(35));
    }
    internal static bool DecodeTrigger(VRControllerState_t state,int[] types,bool index,bool wasDown)
    {
        if((state.ulButtonPressed&(1UL<<33))!=0)return true;
        // Index's second trigger-typed axis is grip force, not the index-finger trigger.
        int axis=index?(types.Length>1&&types[1]==(int)EVRControllerAxisType.k_eControllerAxis_Trigger?1:-1):Array.FindIndex(types,t=>t==(int)EVRControllerAxisType.k_eControllerAxis_Trigger);
        return axis>=0&&Axis(state,axis).X>=(wasDown?.35f:.65f);
    }
    internal static int FindNavigationAxis(int[] types)
    {
        int joystick=Array.FindIndex(types,t=>t==(int)EVRControllerAxisType.k_eControllerAxis_Joystick);
        if(joystick>=0)return joystick;
        // SteamVR's legacy Index mapping may present the thumbstick through
        // axis 0 as TrackPad: [1,3,3,0,0]. Both are 2D navigation inputs;
        // trigger axes (3) must never be selected as a movement fallback.
        return Array.FindIndex(types,t=>t==(int)EVRControllerAxisType.k_eControllerAxis_TrackPad);
    }
    internal static Vector2 Axis(VRControllerState_t s,int axis)
    {
        var value=axis switch{0=>s.rAxis0,1=>s.rAxis1,2=>s.rAxis2,3=>s.rAxis3,4=>s.rAxis4,_=>default};
        return float.IsFinite(value.x)&&float.IsFinite(value.y)?Vector2.Clamp(new(value.x,value.y),-Vector2.One,Vector2.One):Vector2.Zero;
    }
}

// An active bound action is authoritative, including its released state. A stale
// legacy pressed bit must not override a released SteamVR trigger.
// Once skeletal actions are enabled, continue reading their buttons after calibration
// closes: enabling an action manifest can change SteamVR's legacy button emulation.
internal sealed class ControllerInputRoute
{
    internal bool BoundForTracking {get;private set;}
    internal void EnableTracking(bool enabled){BoundForTracking|=enabled;}
    internal ControllerReading Read(bool useActions,Func<ControllerReading> direct,Func<ControllerReading> bound)
    {
        if(useActions)return bound();
        var raw=direct();if(!BoundForTracking)return raw;
        var action=bound();
        return new(raw.Active&&raw.Move.LengthSquared()>.0025f?raw.Move:action.Move,raw.Active&&raw.Right.LengthSquared()>.0025f?raw.Right:action.Right,
            raw.LeftStatus+" · "+action.LeftStatus,raw.RightStatus+" · "+action.RightStatus,raw.Active||action.Active,action.Error,
            action.BoundMenu?action.Menu:raw.Menu,action.BoundSelect?action.Select:raw.Select,action.BoundBack?action.Back:raw.Back,action.BoundGrab?action.Grab:raw.Grab){LeftTrigger=action.BoundLeftTrigger?action.LeftTrigger:raw.LeftTrigger,LeftStickClick=action.BoundLeftStickClick?action.LeftStickClick:raw.LeftStickClick,ButtonStatus=action.ButtonStatus,LeftA=action.BoundLeftA?action.LeftA:raw.LeftA,LeftB=action.BoundLeftB?action.LeftB:raw.LeftB,RightA=action.BoundRightA?action.RightA:raw.RightA,RightB=action.BoundRightB?action.RightB:raw.RightB};
    }
}
