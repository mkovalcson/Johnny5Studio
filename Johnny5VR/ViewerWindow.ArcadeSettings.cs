namespace Johnny5VR;
internal sealed partial class ViewerWindow
{
    private ArcadeSettings? arcadeSettings;
    private void BuildArcadeSettingsMenu()
    {
        if(vr?.Menu is not {} menu)return;arcadeSettings=ArcadeSettings.Load(settings.ConfigFolder);var options=arcadeSettings;
        string Label(string path)=>path.Length==0?"Default":Path.GetFileNameWithoutExtension(path);
        menu.GoBack=BuildArcadeMenu;
        var rows=new List<MenuRow>{new("Instructions: "+(options.Introduction.Length==0?"Skip until selected":Label(options.Introduction)),PickArcadeIntroduction)};
        foreach(var cue in Enum.GetValues<ArcadeCue>())
        {var captured=cue;rows.Add(new(cue+" audio: "+Label(options.Audio(cue)),()=>PickArcadeAudio(captured)));}
        menu.Set("Game setup · Sequence and audio",rows.ToArray(),"Select an instruction sequence and event audio from Config. Defaults work immediately. Changes apply on the next Start / Restart. Desktop Game setup can import audio files from another folder.");
    }
    private void PickArcadeIntroduction()
    {
        if(vr?.Menu is not {} menu)return;var options=arcadeSettings??=ArcadeSettings.Load(settings.ConfigFolder);
        void Choose(string path){options.Introduction=path;options.Save(settings.ConfigFolder);BuildArcadeSettingsMenu();}
        menu.GoBack=BuildArcadeSettingsMenu;
        menu.Set("Instruction sequence",new[]{new MenuRow("None · skip instructions",()=>Choose(""))}.Concat(library.Where(e=>!e.Movie).Select(e=>new MenuRow(e.Name,()=>Choose(e.File)))).ToArray(),"The sequence plays at ground level with its movements and audio. Pull a trigger to skip. Saved scene changes are ignored so the arena remains in place.");
    }
    private void PickArcadeAudio(ArcadeCue cue)
    {
        if(vr?.Menu is not {} menu)return;var options=arcadeSettings??=ArcadeSettings.Load(settings.ConfigFolder);
        void Choose(string path){options.SetAudio(cue,path);options.Save(settings.ConfigFolder);BuildArcadeSettingsMenu();}
        var extensions=new[]{".wav",".mp3",".flac",".aiff",".wma",".m4a"};
        var files=Directory.EnumerateFiles(settings.ConfigFolder,"*",SearchOption.AllDirectories).Where(f=>extensions.Contains(Path.GetExtension(f).ToLowerInvariant())).Select(f=>Path.GetRelativePath(settings.ConfigFolder,f)).OrderBy(f=>f).ToArray();
        menu.GoBack=BuildArcadeSettingsMenu;menu.Set(cue+" audio",new[]{new MenuRow("Use default sound / voice",()=>Choose(""))}.Concat(files.Select(f=>new MenuRow(f,()=>Choose(f)))).ToArray(),"Select an audio file already in Config. Your choice is saved relative to Config. Restart the game to use it.");
    }
    private void OpenArcadeSettings()
    {
        var options=ArcadeSettings.Load(settings.ConfigFolder);using var window=new Form{Text="Laser Range · Sequence and event audio",ClientSize=new(790,365),MinimumSize=new(700,400),StartPosition=FormStartPosition.CenterParent,AutoScaleDimensions=new SizeF(96,96),AutoScaleMode=AutoScaleMode.Dpi,Font=Font};
        var table=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new(12),ColumnCount=3,RowCount=7};table.ColumnStyles.Add(new(SizeType.Absolute,175));table.ColumnStyles.Add(new(SizeType.Percent,100));table.ColumnStyles.Add(new(SizeType.Absolute,110));
        for(int i=0;i<5;i++)table.RowStyles.Add(new(SizeType.Absolute,48));table.RowStyles.Add(new(SizeType.Percent,100));table.RowStyles.Add(new(SizeType.Absolute,44));
        var introduction=new ComboBox{Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDownList};var entries=new[]{new LibraryEntry("","None · skip instructions",false,"")}.Concat(library.Where(e=>!e.Movie)).ToArray();introduction.Items.AddRange(entries);introduction.SelectedItem=entries.FirstOrDefault(e=>e.File==options.Introduction)??entries[0];
        table.Controls.Add(new Label{Text="Instruction sequence",AutoSize=true},0,0);table.Controls.Add(introduction,1,0);
        var fields=new Dictionary<ArcadeCue,TextBox>();int row=1;
        foreach(var cue in Enum.GetValues<ArcadeCue>())
        {
            var input=new TextBox{Text=options.Audio(cue),Dock=DockStyle.Fill,PlaceholderText="Default sound / voice"};fields[cue]=input;
            table.Controls.Add(new Label{Text=cue+" audio",AutoSize=true},0,row);table.Controls.Add(input,1,row);
            var choose=new StudioDesktopButton{Text="Browse…",Dock=DockStyle.Fill};choose.Click+=(_,_)=>
            {
                using var dialog=new OpenFileDialog{Filter="Audio|*.wav;*.mp3;*.flac;*.aiff;*.wma;*.m4a|All files|*.*"};
                if(dialog.ShowDialog(window)!=DialogResult.OK)return;
                string source=Path.GetFullPath(dialog.FileName),root=Path.GetFullPath(settings.ConfigFolder)+Path.DirectorySeparatorChar;
                if(source.StartsWith(root,StringComparison.OrdinalIgnoreCase))input.Text=Path.GetRelativePath(settings.ConfigFolder,source);
                else {string folder=Path.Combine(settings.ConfigFolder,"Library","Audio","LaserRange");Directory.CreateDirectory(folder);string target=Path.Combine(folder,Path.GetFileName(source));if(File.Exists(target))target=Path.Combine(folder,Path.GetFileNameWithoutExtension(source)+"-"+Guid.NewGuid().ToString("N")[..6]+Path.GetExtension(source));File.Copy(source,target);input.Text=Path.GetRelativePath(settings.ConfigFolder,target);}
            };table.Controls.Add(choose,2,row++);
        }
        table.Controls.Add(new Label{Text="Blank audio uses defaults. Imported audio is copied into Config/Library/Audio/LaserRange. Changes apply on the next Start / Restart.",Dock=DockStyle.Fill},0,5);table.SetColumnSpan(table.GetControlFromPosition(0,5)!,3);
        var save=new StudioDesktopButton{Text="Save",Dock=DockStyle.Fill};save.Click+=(_,_)=>{options.Introduction=((LibraryEntry)introduction.SelectedItem!).File;foreach(var item in fields)options.SetAudio(item.Key,item.Value.Text.Trim());options.Save(settings.ConfigFolder);window.DialogResult=DialogResult.OK;window.Close();};
        StudioDesktopTheme.Apply(window);
        table.Controls.Add(save,2,6);window.Controls.Add(table);window.ShowDialog(this);
    }
}
