namespace Johnny5VR;
internal sealed partial class ViewerWindow
{
    private readonly ControllerFeedback controllerFeedback=new();
    private Form? feedbackWindow;
    private ComboBox? feedbackChoice;
    private TrackBar? feedbackLevel;
    private Label? feedbackPercent;
    private bool refreshingFeedback;
    private void PlayControllerFeedback(FollowToggle toggle)=>controllerFeedback.Play(toggle,
        (toggle is FollowToggle.Hand or FollowToggle.Movement)&&settings.ControllerConfirmation!=FeedbackConfirmation.Off
            ?FeedbackConfirmation.Voice:settings.ControllerConfirmation,settings.ControllerFeedbackVolume);
    private void SetControllerFeedback(FeedbackConfirmation? mode=null,float? volume=null)
    {
        if(mode is {} m)settings.ControllerConfirmation=m;
        if(volume is {} v)settings.ControllerFeedbackVolume=Math.Clamp(v,0,1);
        if(settings.ControllerConfirmation==FeedbackConfirmation.Off||settings.ControllerFeedbackVolume<=0)controllerFeedback.Cancel();
        RefreshFeedbackControls();
        try
        {
            // Only these preferences are changed on disk; pending scene controls stay pending.
            var stored=ViewerSettings.Load();stored.ControllerConfirmation=settings.ControllerConfirmation;stored.ControllerFeedbackVolume=settings.ControllerFeedbackVolume;stored.Save();
        }
        catch(Exception ex){Fail(ex);}
        if(vr?.Menu is {} menu&&feedbackMenuOpen)UpdateFeedbackMenu(menu);
    }
    private void RefreshFeedbackControls()
    {
        refreshingFeedback=true;
        try
        {
            if(feedbackChoice is {IsDisposed:false})feedbackChoice.SelectedIndex=(int)settings.ControllerConfirmation;
            if(feedbackLevel is {IsDisposed:false})feedbackLevel.Value=(int)Math.Round(settings.ControllerFeedbackVolume*100);
            if(feedbackPercent is {IsDisposed:false})feedbackPercent.Text=$"Confirmation volume: {settings.ControllerFeedbackVolume:P0}";
        }
        finally{refreshingFeedback=false;}
    }
    private void OpenControllerFeedback()
    {
        if(feedbackWindow is {IsDisposed:false}){feedbackWindow.Activate();return;}
        var window=new Form{Text="Controller feedback",ClientSize=new(480,485),MinimumSize=new(470,500),StartPosition=FormStartPosition.CenterParent,BackColor=BackColor,ForeColor=ForeColor,Font=Font,
            AutoScaleMode=AutoScaleMode.Dpi,AutoScaleDimensions=new(96,96)};
        feedbackWindow=window;
        var flow=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,Padding=new(18)};window.Controls.Add(flow);
        flow.Controls.Add(new Label{Text="Follow me toggle confirmations",AutoSize=true,Font=new(Font,FontStyle.Bold),Margin=new(3,3,3,12)});
        feedbackChoice=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=410};feedbackChoice.Items.AddRange(["Sounds","Voice","Off"]);flow.Controls.Add(feedbackChoice);
        feedbackPercent=new Label{AutoSize=true,Margin=new(3,12,3,3)};flow.Controls.Add(feedbackPercent);
        feedbackLevel=new TrackBar{Minimum=0,Maximum=100,TickFrequency=10,SmallChange=5,LargeChange=10,Width=410,Height=52,AutoSize=false};flow.Controls.Add(feedbackLevel);
        flow.Controls.Add(new Label{Text="Preview the selected confirmation style:",AutoSize=true,Margin=new(3,6,3,6)});
        foreach(var toggle in Enum.GetValues<FollowToggle>())
        {
            var preview=new StudioDesktopButton{Text="Preview: "+ControllerFeedback.Label(toggle),Width=410,Height=38};
            preview.Click+=(_,_)=>PlayControllerFeedback(toggle);flow.Controls.Add(preview);
        }
        flow.Controls.Add(new Label{Text="Changes take effect and save immediately. Off silences previews.\nVolume is separate from Johnny’s dialogue. Uses the current Windows audio output.",MaximumSize=new(410,0),AutoSize=true,Margin=new(3,10,3,3)});
        RefreshFeedbackControls();
        feedbackChoice.SelectedIndexChanged+=(_,_)=>{if(!refreshingFeedback)SetControllerFeedback((FeedbackConfirmation)feedbackChoice.SelectedIndex);};
        feedbackLevel.ValueChanged+=(_,_)=>{if(!refreshingFeedback)SetControllerFeedback(volume:feedbackLevel.Value/100f);};
        StudioDesktopTheme.Apply(window);
        window.FormClosed+=(_,_)=>{feedbackWindow=null;feedbackChoice=null;feedbackLevel=null;feedbackPercent=null;};window.Show(this);
    }
    private bool feedbackMenuOpen;
    private void BuildControllerFeedbackMenu()
    {
        if(vr?.Menu is not {} menu)return;
        feedbackMenuOpen=true;menu.GoBack=()=>{feedbackMenuOpen=false;BuildBodyMotionMenu();};
        var rows=new List<MenuRow>();
        foreach(var mode in Enum.GetValues<FeedbackConfirmation>())rows.Add(new("",()=>SetControllerFeedback(mode)));
        rows.Add(new("",()=>{}){SliderValue=()=>settings.ControllerFeedbackVolume,SetSlider=value=>SetControllerFeedback(volume:value)});
        foreach(var toggle in Enum.GetValues<FollowToggle>())rows.Add(new("Preview: "+ControllerFeedback.Label(toggle),()=>PlayControllerFeedback(toggle)));
        rows.Add(new("Back",()=>{feedbackMenuOpen=false;BuildBodyMotionMenu();}));
        menu.Set("Controller feedback",rows.ToArray(),"Changes save immediately. Point at the volume bar and hold Trigger to adjust, or use right stick left/right on that row. Off silences previews. Volume is separate from Johnny's dialogue.");
        UpdateFeedbackMenu(menu);
    }
    private void UpdateFeedbackMenu(VrMenu menu)
    {
        // A stale desktop dialog must never rewrite a different VR menu.
        if(menu.Rows.Length!=8||menu.Rows[3].SliderValue==null)return;
        foreach(var mode in Enum.GetValues<FeedbackConfirmation>())menu.Rows[(int)mode]=menu.Rows[(int)mode] with{Label=(settings.ControllerConfirmation==mode?"● ":"○ ")+mode};
        menu.Rows[3]=menu.Rows[3] with{Label=$"Volume: {settings.ControllerFeedbackVolume:P0}"};
    }
}
