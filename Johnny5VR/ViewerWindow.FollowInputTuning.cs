using ServoAnimator;
namespace Johnny5VR;

internal sealed partial class ViewerWindow
{
    private bool inputTuningMenu;
    private Func<string>[] inputTuningLabels=[];
    private Action<VrMenu>? inputTuningUpdate;
    private void SetInputTuningPage(VrMenu menu,string title,MenuRow[] rows,string detail,Action back)
    {
        menu.Set(title,rows,detail);
        inputTuningMenu=true;menu.FollowSelections=true;menu.LiveFollowing=true;
        menu.GoBack=back;menu.Navigation.Open();UpdateInputTuningLabels(menu);
    }
    private void ShowInputTuning()
    {
        if(vr?.Menu is not {} menu)return;
        inputTuningUpdate=null;inputTuningLabels=[];
        SetInputTuningPage(menu,"Follow · Input tuning",
            [new("Smoothing…",()=>ShowInputSections(true)),new("Input Scaling…",()=>ShowInputSections(false)),new("Travel Extents…",ShowTravelExtents)],
            "Smooth rapid changes or adjust movement relative to the neutral pose. Speed limits remain active.",
            ()=>{menu.Navigation.Close();ToggleFollowSelections();});
    }
    private void ShowInputSections(bool smoothing)
    {
        if(vr?.Menu is not {} menu)return;
        inputTuningUpdate=null;inputTuningLabels=[];
        var rows=FollowNeutralCatalog.Controls.Select(c=>c.Section).Distinct()
            .Select(section=>new MenuRow(section,()=>ShowInputControls(smoothing,section,0))).ToArray();
        SetInputTuningPage(menu,smoothing?"Follow · Smoothing":"Follow · Input Scaling",rows,
            smoothing?"0 ms leaves the input unfiltered. Higher values reduce jitter and add some delay."
                     :"100% uses normal movement. 50% halves it; 200% doubles it within joint limits.",ShowInputTuning);
    }
    private void ShowInputControls(bool smoothing,string section,int page)
    {
        if(vr?.Menu is not {} menu)return;
        var settings=vr.BodyMotion.InputTuning;
        var all=FollowNeutralCatalog.Controls.Where(c=>c.Section==section).ToArray();
        var controls=all.Skip(page*6).Take(6).ToArray();
        float Get(FollowNeutralControl c)
        {
            var value=settings.Filter.Get(c.Key);
            return smoothing?value.SmoothingMs/300:value.Scale/2;
        }
        void Set(FollowNeutralControl c,float value)
        {
            var old=settings.Filter.Get(c.Key);
            settings.Set(c.Key,smoothing?old with{SmoothingMs=MathF.Round(value*60)*5}:old with{Scale=MathF.Round(value*40)/20});
        }
        string Label(FollowNeutralControl c)
        {
            var name=c.Key=="B_NeckLift"?"Crouch / forward bend":c.Label;
            var value=settings.Filter.Get(c.Key);
            return smoothing?$"{name} · {value.SmoothingMs:0} ms":$"{name} · {value.Scale:P0}";
        }
        inputTuningLabels=controls.Select(c=>(Func<string>)(()=>Label(c))).ToArray();
        var rows=controls.Select(c=>new MenuRow(Label(c),()=>{})
            {SliderValue=()=>Get(c),SetSlider=v=>Set(c,v),SliderStep=smoothing?1f/60:1f/40}).ToList();
        if(page>0)rows.Add(new("Previous controls",()=>ShowInputControls(smoothing,section,page-1)));
        if((page+1)*6<all.Length)rows.Add(new("More controls…",()=>ShowInputControls(smoothing,section,page+1)));
        rows.Add(new("Reset this section",()=>{foreach(var c in all)Set(c,smoothing?0:.5f);UpdateInputTuningLabels(menu);}));
        SetInputTuningPage(menu,"Follow · "+(smoothing?"Smoothing":"Input Scaling")+" · "+section,rows.ToArray(),
            "Hold trigger on a slider or step with the right stick. Follow stays live and changes save automatically. "
            +(section=="Torso"?"Crouch / forward bend controls the shared neck-first motion. ":"")
            +"Left B closes; Right B stops Follow.",()=>ShowInputSections(smoothing));
    }
    private void UpdateInputTuningLabels(VrMenu menu)
    {
        inputTuningUpdate?.Invoke(menu);
        for(int i=0;i<inputTuningLabels.Length&&i<menu.Rows.Length;i++)
        {
            string label=inputTuningLabels[i]();
            if(menu.Rows[i].Label!=label)menu.Rows[i]=menu.Rows[i] with{Label=label};
        }
    }
}
