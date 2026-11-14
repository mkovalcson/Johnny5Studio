using Microsoft.Win32;
namespace Johnny5VR;
internal sealed partial class ViewerWindow
{
    private CleanOutputWindow? cleanOutputWindow;
    private Form? cleanSettingsWindow;
    private readonly Label cleanOutputNotice=new(){Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter,BackColor=Color.FromArgb(29,35,45),ForeColor=Color.LightCyan,Visible=false};
    private bool changingCleanOutput;
    private void InitializeCleanOutput()
    {
        viewport.Controls.Add(cleanOutputNotice);SystemEvents.DisplaySettingsChanged+=CleanDisplaysChanged;
    }
    private void CleanDisplaysChanged(object? sender,EventArgs e)
    {
        if(closing||IsDisposed||!IsHandleCreated)return;
        try{BeginInvoke(new Action(()=>
        {
            if(closing||!settings.CleanOutputEnabled)return;
            var display=CleanOutputDisplay.Enumerate().FirstOrDefault(d=>d.Id==settings.CleanOutputDisplay);
            if(display==null){DisableCleanOutput(true,"Clean Output display disconnected — application preview restored.");return;}
            if(cleanOutputWindow is {IsDisposed:false} output)
            {
                changingCleanOutput=true;try{output.Place(display,settings.CleanOutputFullscreen,settings.CleanOutputWidth,settings.CleanOutputHeight);renderer?.ResizeCleanOutput(output.ClientSize.Width,output.ClientSize.Height);}finally{changingCleanOutput=false;}
            }
        }));}catch(InvalidOperationException){}
    }
    private void SaveCleanOutputSettings()
    {
        var saved=ViewerSettings.Load();saved.CleanOutputEnabled=settings.CleanOutputEnabled;saved.CleanOutputDisplay=settings.CleanOutputDisplay;saved.CleanOutputFullscreen=settings.CleanOutputFullscreen;saved.CleanOutputWidth=settings.CleanOutputWidth;saved.CleanOutputHeight=settings.CleanOutputHeight;saved.CleanOutputOverlays=settings.CleanOutputOverlays;saved.Save();
    }
    private void ApplyCleanOutput()
    {
        if(renderer==null)return;
        if(!settings.CleanOutputEnabled){DisableCleanOutput(false);return;}
        var display=CleanOutputDisplay.Enumerate().FirstOrDefault(d=>d.Id==settings.CleanOutputDisplay);
        if(display==null){DisableCleanOutput(true,"Selected output display is unavailable — application preview restored.");return;}
        changingCleanOutput=true;
        try
        {
            if(cleanOutputWindow==null||cleanOutputWindow.IsDisposed)
            {
                cleanOutputWindow=new();
                cleanOutputWindow.ClientSizeChanged+=(_,_)=>
                {
                    if(changingCleanOutput||closing||cleanOutputWindow is not {IsDisposed:false} output)return;
                    try{renderer?.ResizeCleanOutput(output.ClientSize.Width,output.ClientSize.Height);}catch(Exception error){Diagnostics.Log(error);DisableCleanOutput(true,"Clean Output resize failed — preview restored.");}
                };
                cleanOutputWindow.FormClosed+=(_,_)=>{if(!changingCleanOutput&&!closing)DisableCleanOutput(true,"Clean Output closed — application preview restored.");};
            }
            cleanOutputWindow.Place(display,settings.CleanOutputFullscreen,settings.CleanOutputWidth,settings.CleanOutputHeight);
            // This is deliberately not an owned form: minimizing the controls must not hide the output.
            cleanOutputWindow.Show();
            renderer.ConfigureCleanOutput(cleanOutputWindow.Handle,cleanOutputWindow.ClientSize.Width,cleanOutputWindow.ClientSize.Height,settings.CleanOutputWidth,settings.CleanOutputHeight);
            renderer.ConfigureOverlayCapture(settings.CleanOutputOverlays);
            cleanOutputNotice.Text=$"Clean Output active on\n{display.Name} ({display.Device})\n\n{settings.CleanOutputWidth} × {settings.CleanOutputHeight}\nApplication preview is disabled.";
            cleanOutputNotice.Visible=true;cleanOutputNotice.BringToFront();
            status.Text=$"Clean Output · {display.Name} · {settings.CleanOutputWidth} × {settings.CleanOutputHeight} · {(display.Refresh>1?display.Refresh+" Hz":"refresh unknown")}";
        }
        catch(Exception error){Diagnostics.Log(error);DisableCleanOutput(true,"Clean Output could not start — application preview restored. "+error.Message);}
        finally{changingCleanOutput=false;}
    }
    private void DisableCleanOutput(bool save,string? message=null)
    {
        bool previous=changingCleanOutput;changingCleanOutput=true;
        try
        {
            renderer?.DisableCleanOutput();var output=cleanOutputWindow;cleanOutputWindow=null;if(output is {IsDisposed:false})output.Close();
            settings.CleanOutputEnabled=false;cleanOutputNotice.Visible=false;viewport.Invalidate();
            if(message!=null)status.Text=message;
            if(save&&!closing)try{SaveCleanOutputSettings();}catch(Exception error){Diagnostics.Log(error);}
        }
        finally{changingCleanOutput=previous;}
    }
    internal static void CheckCleanOutputLayout()
    {
        using var viewer=new ViewerWindow(new ViewerSettings{ConfigFolder=Path.Combine(ViewerSettings.Home,"packaging","editor-check","Config")},layoutCheckOnly:true);
        viewer.Show();viewer.OpenCleanOutputSettings();Application.DoEvents();
        var window=viewer.cleanSettingsWindow!;
        using var image=new Bitmap(window.ClientSize.Width,window.ClientSize.Height);window.DrawToBitmap(image,window.ClientRectangle);
        image.Save(Path.Combine(Diagnostics.Folder,"clean-output-settings.png"));
        if(window.Controls[0] is not TableLayoutPanel table||table.GetControlFromPosition(0,7) is not {} actions||actions.Bottom>table.ClientSize.Height-table.Padding.Bottom)
            throw new InvalidOperationException("Clean Output actions do not fit the settings window.");
        window.Close();viewer.Close();
    }
    private void OpenCleanOutputSettings()
    {
        if(cleanSettingsWindow is {IsDisposed:false}){cleanSettingsWindow.Activate();return;}
        var window=new Form{Text="Clean Output",ClientSize=new(690,525),MinimumSize=new(600,500),StartPosition=FormStartPosition.CenterParent,BackColor=BackColor,ForeColor=ForeColor,Font=Font,AutoScaleMode=AutoScaleMode.Dpi,AutoScaleDimensions=new(96,96)};
        cleanSettingsWindow=window;
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new(16),ColumnCount=2,RowCount=9};layout.ColumnStyles.Add(new(SizeType.Absolute,150));layout.ColumnStyles.Add(new(SizeType.Percent,100));
        foreach(int rowHeight in new[]{42,74,42,42,42,42,95,42})layout.RowStyles.Add(new(SizeType.Absolute,rowHeight));layout.RowStyles.Add(new(SizeType.Percent,100));window.Controls.Add(layout);
        var enabled=new CheckBox{Text="Enable Clean Output",Checked=settings.CleanOutputEnabled,AutoSize=true};layout.Controls.Add(enabled,0,0);layout.SetColumnSpan(enabled,2);
        var displays=new ComboBox{Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDownList};var refresh=new StudioDesktopButton{Text="Refresh displays",Dock=DockStyle.Bottom,Height=30};var displayHost=new Panel{Dock=DockStyle.Fill};displayHost.Controls.Add(displays);displayHost.Controls.Add(refresh);
        layout.Controls.Add(new Label{Text="Output display",AutoSize=true},0,1);layout.Controls.Add(displayHost,1,1);
        void RefreshDisplays()
        {
            string? selected=(displays.SelectedItem as CleanOutputDisplay)?.Id??settings.CleanOutputDisplay;displays.Items.Clear();displays.Items.AddRange(CleanOutputDisplay.Enumerate());
            displays.SelectedItem=displays.Items.Cast<CleanOutputDisplay>().FirstOrDefault(d=>d.Id==selected);
            if(displays.SelectedIndex<0&&string.IsNullOrEmpty(selected))displays.SelectedItem=displays.Items.Cast<CleanOutputDisplay>().FirstOrDefault(d=>!d.Primary)??displays.Items.Cast<CleanOutputDisplay>().FirstOrDefault();
        }
        RefreshDisplays();refresh.Click+=(_,_)=>RefreshDisplays();displays.DropDownWidth=900;
        var mode=new ComboBox{Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDownList};mode.Items.AddRange(["Windowed","Borderless fullscreen"]);mode.SelectedIndex=settings.CleanOutputFullscreen?1:0;
        layout.Controls.Add(new Label{Text="Output mode",AutoSize=true},0,2);layout.Controls.Add(mode,1,2);
        var width=new NumericUpDown{Minimum=320,Maximum=7680,Increment=16,Value=Math.Clamp(settings.CleanOutputWidth,320,7680),Dock=DockStyle.Fill};
        var height=new NumericUpDown{Minimum=180,Maximum=4320,Increment=16,Value=Math.Clamp(settings.CleanOutputHeight,180,4320),Dock=DockStyle.Fill};
        layout.Controls.Add(new Label{Text="Render width",AutoSize=true},0,3);layout.Controls.Add(width,1,3);layout.Controls.Add(new Label{Text="Render height",AutoSize=true},0,4);layout.Controls.Add(height,1,4);
        var overlays=new CheckBox{Text="Include VR overlays (menus, editor and glove displays)",Checked=settings.CleanOutputOverlays,AutoSize=true};layout.Controls.Add(overlays,0,5);layout.SetColumnSpan(overlays,2);
        var help=new Label{Dock=DockStyle.Fill,Text="Capture the scene alone, or include the application’s visible VR overlays. The main 3D preview stops while it is active. Fullscreen fits the selected monitor proportionally. Set monitor resolution/refresh in Windows or your virtual-display driver. The controls can be minimized while output continues."};layout.Controls.Add(help,0,6);layout.SetColumnSpan(help,2);
        var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft};var close=new StudioDesktopButton{Text="Close",AutoSize=true};var applyOutput=new StudioDesktopButton{Text="Apply output",AutoSize=true};actions.Controls.Add(close);actions.Controls.Add(applyOutput);layout.Controls.Add(actions,0,7);layout.SetColumnSpan(actions,2);
        close.Click+=(_,_)=>window.Close();applyOutput.Click+=(_,_)=>
        {
            if(enabled.Checked&&displays.SelectedItem is not CleanOutputDisplay){MessageBox.Show(window,"Select an available output display.","Clean Output");return;}
            settings.CleanOutputEnabled=enabled.Checked;if(displays.SelectedItem is CleanOutputDisplay selected)settings.CleanOutputDisplay=selected.Id;
            settings.CleanOutputFullscreen=mode.SelectedIndex==1;settings.CleanOutputWidth=(int)width.Value;settings.CleanOutputHeight=(int)height.Value;settings.CleanOutputOverlays=overlays.Checked;
            ApplyCleanOutput();enabled.Checked=settings.CleanOutputEnabled;try{SaveCleanOutputSettings();}catch(Exception error){Fail(error);}
        };
        StudioDesktopTheme.Apply(window);
        window.Show(this);
    }
}
