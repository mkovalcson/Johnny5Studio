using ServoAnimator;
namespace Johnny5VR;
internal sealed partial class ViewerWindow
{
    private bool neutralPoseMenu;
    private FollowNeutralControl[] visibleNeutralControls=[];
    private void ShowNeutralPose()
    {
        if(vr?.Menu is not {} menu)return;
        var rows=new[]{"Torso","Head","Left arm","Right arm","Left hand","Right hand"}.Select(section=>new MenuRow(section+" defaults…",()=>ShowNeutralPosePage(section,0))).ToList();
        rows.Add(new("Reset all neutral defaults",()=>{vr.BodyMotion.Neutrals.Reset(FollowNeutralCatalog.Controls);ShowNeutralPose();}));
        menu.Set("Follow · Neutral pose defaults",rows.ToArray(),"Stand in your measured neutral stance and adjust the robot's resting pose. Left/right means Johnny's left/right. Follow stays live and speed-limited; changes save automatically. Neck lowers first for crouching or bending, then chest and waist bend. Left B closes; Right B stops Follow.");
        neutralPoseMenu=true;visibleNeutralControls=[];menu.FollowSelections=true;menu.LiveFollowing=true;
        menu.GoBack=()=>{menu.Navigation.Close();ToggleFollowSelections();};menu.Navigation.Open();
    }
    private void ShowNeutralPosePage(string section,int page)
    {
        if(vr?.Menu is not {} menu)return;
        var all=FollowNeutralCatalog.Controls.Where(c=>c.Section==section).ToArray();
        var controls=all.Skip(page*6).Take(6).ToArray();
        var motion=vr.BodyMotion;
        var rows=controls.Select(c=>new MenuRow(c.Label,()=>{}) {
            SliderValue=()=>c.ToSlider(motion.Neutrals.Get(c,motion.NeutralMapper,motion.MirrorFacing)),
            SetSlider=value=>motion.Neutrals.Set(c,value),
            SliderStep=c.Key.StartsWith("B_",StringComparison.Ordinal)?MathF.PI/180/(c.Maximum-c.Minimum):.005f }).ToList();
        if(page>0)rows.Add(new("Previous controls",()=>ShowNeutralPosePage(section,page-1)));
        if((page+1)*6<all.Length)rows.Add(new("More controls…",()=>ShowNeutralPosePage(section,page+1)));
        rows.Add(new("Reset this section to measured defaults",()=>{motion.Neutrals.Reset(all);UpdateNeutralPoseLabels(menu);}));
        menu.Set("Follow · "+section+" neutral"+(all.Length>6?$" · {page+1}/{(all.Length+5)/6}":""),rows.ToArray(),
            "These are Johnny's positions in your measured standing stance. Motion is relative to that stance, clamped to joint limits. Body values are degrees; head values are %. Hold trigger on a slider or use right stick left/right. Follow stays live; travel is held while tuning. Saved automatically. Point gesture keeps its straight finger/wrist pose. Left B closes; Right B stops.");
        neutralPoseMenu=true;visibleNeutralControls=controls;menu.FollowSelections=true;menu.LiveFollowing=true;menu.GoBack=ShowNeutralPose;
        menu.Navigation.Open();UpdateNeutralPoseLabels(menu);
    }
    private void UpdateNeutralPoseLabels(VrMenu menu)
    {
        if(vr==null)return;var motion=vr.BodyMotion;
        for(int i=0;i<visibleNeutralControls.Length&&i<menu.Rows.Length;i++)
        {
            var c=visibleNeutralControls[i];float value=motion.Neutrals.Get(c,motion.NeutralMapper,motion.MirrorFacing);
            menu.Rows[i]=menu.Rows[i] with{Label=$"{c.Label} · {c.Display(value)}"+(motion.Neutrals.Values.ContainsKey(c.Key)?" *":"")};
        }
    }
}
