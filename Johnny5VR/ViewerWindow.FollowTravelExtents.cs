using Johnny5.VrBridge;
using ServoAnimator;
using System.Text.Json;

namespace Johnny5VR;

internal sealed partial class ViewerWindow
{
    private string SendTravelRequest(VrPoseAction action,string key="")
    {
        var pose=lastEditorFrame?.Pose;
        if(editor==null||pose is not {Editable:true,Recording:false})
        {
            vr?.Menu?.Detail("Pause recording or playback before changing URDF extents.");
            return "";
        }
        string id=Guid.NewGuid().ToString();editor.Pose(new(id,pose.Token,action,key));return id;
    }
    private void ShowTravelExtents()
    {
        if(vr?.Menu is not {} menu)return;
        inputTuningLabels=[];inputTuningUpdate=null;
        SetInputTuningPage(menu,"Follow · Travel Extents",[],"Reading URDF Configuration…",ShowInputTuning);
        string id=SendTravelRequest(VrPoseAction.ReadTravelExtents);
        if(id=="")return;
        inputTuningUpdate=_=>
        {
            if(lastEditorFrame?.Pose is not {} pose||pose.TravelExtentReceipt!=id)return;
            inputTuningUpdate=null;
            if(pose.TravelExtentMessage.StartsWith("Set both",StringComparison.Ordinal))ShowTravelSections(pose.TravelExtents);
            else menu.Detail(pose.TravelExtentMessage);
        };
    }
    private void ShowTravelSections(VrTravelExtent[] extents)
    {
        if(vr?.Menu is not {} menu)return;
        var sections=FollowNeutralCatalog.Controls.Where(c=>extents.Any(e=>e.Key==c.Key)).Select(c=>c.Section).Distinct();
        SetInputTuningPage(menu,"Follow · Travel Extents",sections.Select(section=>new MenuRow(section,()=>ShowTravelControls(extents,section))).ToArray(),
            "These are the same ranges used by URDF Configuration. Choose a control to edit its minimum and maximum.",ShowInputTuning);
    }
    private void ShowTravelControls(VrTravelExtent[] extents,string section)
    {
        if(vr?.Menu is not {} menu)return;
        inputTuningUpdate=null;inputTuningLabels=[];
        var rows=FollowNeutralCatalog.Controls.Where(c=>c.Section==section).Select(c=>(Control:c,Range:extents.FirstOrDefault(e=>e.Key==c.Key)))
            .Where(row=>row.Range!=null).Select(row=>new MenuRow(row.Control.Label,()=>EditTravelRange(extents,row.Control,row.Range!))).ToArray();
        SetInputTuningPage(menu,"Follow · Travel Extents · "+section,rows,"Choose a control. Changes only take effect when saved.",()=>ShowTravelSections(extents));
    }
    private void EditTravelRange(VrTravelExtent[] extents,FollowNeutralControl control,VrTravelExtent range)
    {
        if(vr?.Menu is not {} menu)return;
        double min=range.Minimum,max=range.Maximum,span=range.SliderMaximum-range.SliderMinimum;
        string pending="";
        inputTuningUpdate=null;
        inputTuningLabels=[()=>$"Minimum · {min:0.###} {range.Unit}",()=>$"Maximum · {max:0.###} {range.Unit}"];
        double Value(float v)=>Math.Clamp(Math.Round(range.SliderMinimum+span*v,1),range.SliderMinimum,range.SliderMaximum);
        SetInputTuningPage(menu,"Travel Extents · "+control.Label,[
            new("Minimum",()=>{}){SliderValue=()=>(float)((min-range.SliderMinimum)/span),SetSlider=v=>{if(pending=="")min=Value(v);},SliderStep=(float)(.1/span)},
            new("Maximum",()=>{}){SliderValue=()=>(float)((max-range.SliderMinimum)/span),SetSlider=v=>{if(pending=="")max=Value(v);},SliderStep=(float)(.1/span)},
            new("Save to URDF Configuration",()=>
            {
                if(pending!="")return;
                if(min>=max){menu.Detail("Minimum must be below maximum.");return;}
                if(min==range.Minimum&&max==range.Maximum){menu.Detail("No changes to save.");return;}
                pending=SendTravelRequest(VrPoseAction.SaveTravelExtent,JsonSerializer.Serialize(new VrTravelExtentEdit(range.Key,range.Minimum,range.Maximum,min,max)));
                if(pending!="")menu.Detail("Saving URDF Configuration…");
            })],
            "Set both endpoints, then Save. Back or Left B discards unsaved slider changes. Right stick steps by 0.1.",
            ()=>ShowTravelControls(extents,control.Section));
        inputTuningUpdate=_=>
        {
            if(pending==""||lastEditorFrame?.Pose is not {} pose||pose.TravelExtentReceipt!=pending)return;
            pending="";
            if(pose.TravelExtentMessage=="Saved to URDF Configuration."&&pose.TravelExtents.FirstOrDefault(e=>e.Key==range.Key) is {} saved)
            {EditTravelRange(pose.TravelExtents,control,saved);}
            menu.Detail(pose.TravelExtentMessage);
        };
    }
}
