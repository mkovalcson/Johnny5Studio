namespace Johnny5VR;
internal sealed partial class ViewerWindow
{
    private Form? followSettingsWindow;
    private CheckBox? followBackdropChoice,followStatusChoice,followWaistChoice;
    private bool refreshingFollow;
    private void SetFollowSettings(bool? backdrop=null,bool? overhead=null,bool? waist=null)
    {
        if(backdrop.HasValue)settings.UseEditorFollowBackdrop=backdrop.Value;if(overhead.HasValue)settings.ShowFollowStatus=overhead.Value;if(waist.HasValue)settings.TrackWaistSwivel=waist.Value;
        vr?.Configure(settings);if(editor!=null)editor.UseFollowBackdrop=settings.UseEditorFollowBackdrop;
        refreshingFollow=true;
        try{if(followBackdropChoice is {IsDisposed:false})followBackdropChoice.Checked=settings.UseEditorFollowBackdrop;if(followStatusChoice is {IsDisposed:false})followStatusChoice.Checked=settings.ShowFollowStatus;if(followWaistChoice is {IsDisposed:false})followWaistChoice.Checked=settings.TrackWaistSwivel;}
        finally{refreshingFollow=false;}
        try{var saved=ViewerSettings.Load();saved.UseEditorFollowBackdrop=settings.UseEditorFollowBackdrop;saved.ShowFollowStatus=settings.ShowFollowStatus;saved.TrackWaistSwivel=settings.TrackWaistSwivel;saved.Save();}catch(Exception ex){Fail(ex);}
        if(vr?.Menu is {} menu&&menu.Rows.Length==5&&menu.Rows[0].Label.StartsWith("Backdrop: ",StringComparison.Ordinal))UpdateFollowSettingsMenu(menu);
    }
    private void BuildFollowSettingsMenu()
    {
        if(vr?.Menu is not {} menu)return;menu.GoBack=BuildBodyMotionMenu;
        menu.Set("Follow settings",[
            new("",()=>SetFollowSettings(backdrop:true)),new("",()=>SetFollowSettings(backdrop:false)),
            new("",()=>SetFollowSettings(overhead:!settings.ShowFollowStatus)),
            new("",()=>SetFollowSettings(waist:!settings.TrackWaistSwivel)),
            new("Controller feedback · Sounds / Voice / Off",()=>{BuildControllerFeedbackMenu();menu.GoBack=BuildFollowSettingsMenu;})],"Changes apply and save immediately. Backdrop choices affect Follow/Record only. Calibration instructions, glove LCDs and the recording indicator remain available.");UpdateFollowSettingsMenu(menu);
    }
    private void UpdateFollowSettingsMenu(VrMenu menu)
    {
        menu.Rows[0]=menu.Rows[0] with{Label="Backdrop: "+(settings.UseEditorFollowBackdrop?"● ":"○ ")+"Use editor's loaded URDF backdrop"};
        menu.Rows[1]=menu.Rows[1] with{Label=(settings.UseEditorFollowBackdrop?"○ ":"● ")+"No backdrop"};
        menu.Rows[2]=menu.Rows[2] with{Label="Overhead Follow status: "+(settings.ShowFollowStatus?"Shown":"Hidden")};
        menu.Rows[3]=menu.Rows[3] with{Label="Waist swivel from body tracking: "+(settings.TrackWaistSwivel?"On":"Off (timeline keeps it)")};
    }
    private void OpenFollowSettings()
    {
        if(followSettingsWindow is {IsDisposed:false}){followSettingsWindow.Activate();return;}
        var window=new Form{Text="Follow settings",ClientSize=new(510,380),MinimumSize=new(500,420),StartPosition=FormStartPosition.CenterParent,BackColor=BackColor,ForeColor=ForeColor,Font=Font,AutoScaleMode=AutoScaleMode.Dpi,AutoScaleDimensions=new(96,96)};
        followSettingsWindow=window;var flow=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,Padding=new(18)};window.Controls.Add(flow);
        flow.Controls.Add(new Label{Text="Follow scene and status",AutoSize=true,Font=new(Font,FontStyle.Bold),Margin=new(3,3,3,15)});
        followBackdropChoice=new CheckBox{Text="Use the editor's loaded URDF backdrop",AutoSize=true,Checked=settings.UseEditorFollowBackdrop};flow.Controls.Add(followBackdropChoice);
        flow.Controls.Add(new Label{Text="Uncheck for no backdrop during Follow/Record.",AutoSize=true,Margin=new(24,0,3,15)});
        followStatusChoice=new CheckBox{Text="Show overhead Follow status",AutoSize=true,Checked=settings.ShowFollowStatus};flow.Controls.Add(followStatusChoice);
        followWaistChoice=new CheckBox{Text="Drive waist swivel from body tracking",AutoSize=true,Checked=settings.TrackWaistSwivel,Margin=new(3,15,3,3)};flow.Controls.Add(followWaistChoice);
        flow.Controls.Add(new Label{Text="Off: the waist swivel keeps its timeline animation during Follow and recording.",AutoSize=true,MaximumSize=new(455,0),Margin=new(24,0,3,5)});
        var feedback=new StudioDesktopButton{Text="Controller feedback…",Width=430,Height=38,Margin=new(3,15,3,10)};feedback.Click+=(_,_)=>OpenControllerFeedback();flow.Controls.Add(feedback);
        flow.Controls.Add(new Label{Text="Changes apply and save immediately. Calibration instructions, glove LCDs and the recording indicator remain available.",AutoSize=true,MaximumSize=new(455,0)});
        followBackdropChoice.CheckedChanged+=(_,_)=>{if(!refreshingFollow)SetFollowSettings(backdrop:followBackdropChoice.Checked);};followStatusChoice.CheckedChanged+=(_,_)=>{if(!refreshingFollow)SetFollowSettings(overhead:followStatusChoice.Checked);};followWaistChoice.CheckedChanged+=(_,_)=>{if(!refreshingFollow)SetFollowSettings(waist:followWaistChoice.Checked);};
        StudioDesktopTheme.Apply(window);
        window.FormClosed+=(_,_)=>{followSettingsWindow=null;followBackdropChoice=null;followStatusChoice=null;followWaistChoice=null;};window.Show(this);
    }
}
