using Johnny5.VrBridge;
namespace Johnny5VR;
internal sealed partial class ViewerWindow
{
    private void ToggleFollowSelections()
    {
        if(vr?.Menu is not {} menu||!vr.BodyMotion.Following)return;
        if(menu.FollowSelections&&menu.Navigation.Visible){menu.Navigation.Close();return;}
        neutralPoseMenu=false;inputTuningMenu=false;
        void Choose(VrPoseAction action,int value)
        {
            var pose=lastEditorFrame?.Pose;if(pose==null||editor==null)return;
            editor.Pose(new(Guid.NewGuid().ToString(),pose.Token,action,"",value));
        }
        menu.GoBack=()=>menu.Navigation.Close();
        menu.Set("Follow · Selections",[
            new("Laser",()=>Choose(VrPoseAction.Accessory,1)),
            new("Toolbox",()=>Choose(VrPoseAction.Accessory,2)),
            new("None",()=>Choose(VrPoseAction.Accessory,0)),
            new("Normal",()=>Choose(VrPoseAction.Appearance,0)),
            new("Gold",()=>Choose(VrPoseAction.Appearance,4)),
            new("Follow speed sliders…",ShowFollowSpeeds),
            new("Neutral pose defaults…",ShowNeutralPose),
            new("Smoothing / Input Scaling…",ShowInputTuning)],
            "Choose accessory and finish. Left B closes this menu and resumes following. Right B stops Follow. Movement is held while selecting.");
        menu.FollowSelections=true;menu.Navigation.Open();
    }
    private void UpdateFollowSelections(VrPoseState pose)
    {
        if(vr?.Menu is not {FollowSelections:true,Navigation.Visible:true} menu)return;
        if(menu.LiveFollowing){if(inputTuningMenu)UpdateInputTuningLabels(menu);else if(neutralPoseMenu)UpdateNeutralPoseLabels(menu);else UpdateSpeedLabels(menu);return;}
        string[] names=["Laser","Toolbox","None","Normal","Gold"];
        for(int i=0;i<names.Length;i++)
        {
            bool selected=i<3?pose.Accessory==(i==0?1:i==1?2:0):i==3?pose.Appearance!=4:pose.Appearance==4;
            menu.Rows[i]=menu.Rows[i] with{Label=(selected?"● ":"○ ")+names[i]};
        }
        menu.Detail(pose.AppearanceBusy?"Preparing robot appearance… Follow is held. Left B closes the menu.":"Left B closes and resumes Follow. Left stick: robot; right stick: you. Left stick click: laser up/down. Left trigger: fire when lowered. Right A: regular menu.");
    }
    private ServoAnimator.FollowSpeedControl[] visibleFollowSpeeds=[];
    internal static ServoAnimator.FollowSpeedControl[] MainFollowSpeeds=>new[]{new ServoAnimator.FollowSpeedControl(FollowSpeedSettings.AllFingers,"All fingers + thumbs · L/R",2)}
        .Concat(new[]{"B_WristRoll","B_WristPitch","B_ArmTwist","B_Elbow","B_ShoulderRotate"}.Select(key=>ServoAnimator.FollowSpeedCatalog.Controls.Single(c=>c.Key==key))).ToArray();
    private void ShowFollowSpeeds()=>ShowFollowSpeedPage("Follow · Arm and hand speeds",MainFollowSpeeds,true);
    private void ShowOtherFollowSpeeds()=>ShowFollowSpeedPage("Follow · Head, body and travel speeds",ServoAnimator.FollowSpeedCatalog.Controls.Where(c=>!FollowSpeedSettings.IsFinger(c.Key)&&!MainFollowSpeeds.Any(m=>m.Key==c.Key)).ToArray(),false);
    private void ShowAdvancedFingerSpeeds()=>ShowFollowSpeedPage("Follow · Advanced finger speeds",ServoAnimator.FollowSpeedCatalog.Controls.Where(c=>FollowSpeedSettings.IsFinger(c.Key)).ToArray(),false);
    private void ShowFollowSpeedPage(string title,ServoAnimator.FollowSpeedControl[] controls,bool main)
    {
        if(vr?.Menu is not {} menu)return;
        neutralPoseMenu=false;inputTuningMenu=false;
        var speeds=vr.BodyMotion.Speeds;visibleFollowSpeeds=controls;
        var rows=controls.Select(c=>new MenuRow(c.Label,()=>{})
        {SliderValue=()=>ServoAnimator.FollowSpeedCatalog.ToSlider(speeds.Get(c.Key)),SetSlider=v=>speeds.Set(c.Key,v),SliderStep=.1f/4.9f}).ToList();
        if(main){rows.Add(new("Head, body and travel speeds…",ShowOtherFollowSpeeds));rows.Add(new("Advanced finger speeds…",ShowAdvancedFingerSpeeds));}
        menu.Set(title,rows.ToArray(),"Follow stays active. Range: 0.1×–5× of original speed. Each L/R slider applies to both sides. All fingers + thumbs sets every finger joint, including spread, to the same speed; wrists stay separate. Mixed means individual finger speeds differ. Point and hold trigger, or use right stick left/right for 0.1× steps. Changes save automatically. Left B closes; Right B stops Follow.");
        menu.GoBack=main?()=>{menu.Navigation.Close();ToggleFollowSelections();}:ShowFollowSpeeds;
        menu.FollowSelections=true;menu.LiveFollowing=true;menu.Navigation.Open();UpdateSpeedLabels(menu);
    }
    private void UpdateSpeedLabels(VrMenu menu)
    {
        if(vr==null)return;
        var speeds=vr.BodyMotion.Speeds;
        for(int i=0;i<visibleFollowSpeeds.Length&&i<menu.Rows.Length;i++)
        {var c=visibleFollowSpeeds[i];menu.Rows[i]=menu.Rows[i] with{Label=c.Key==FollowSpeedSettings.AllFingers&&speeds.MixedFingers?$"{c.Label} · Mixed":$"{c.Label}  {speeds.Get(c.Key):F1}×"};}
    }
}
