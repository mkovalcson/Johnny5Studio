using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace ServoAnimator;

internal sealed class UrdfPropLibraryWindow : Window
{
    internal List<UrdfPropPlacement> Placements { get; }
    private readonly string root;
    private readonly ComboBox library=new(){MinWidth=250};
    private readonly ListBox placed=new(){MinHeight=125};
    private readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,4)};
    private readonly TextBox[] fields=new TextBox[7];
    internal UrdfPropLibraryWindow(string root, UrdfStageSettings stage)
    {
        this.root=root;Placements=new(stage?.Props??new());
        Title="URDF Prop Library";Width=670;Height=stage==null?290:570;MinWidth=530;MinHeight=250;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty,"AppBackground");SetResourceReference(ForegroundProperty,"PrimaryText");
        var dock=new DockPanel{Margin=new Thickness(15)};Content=dock;
        var bottom=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right};
        DockPanel.SetDock(bottom,Dock.Bottom);dock.Children.Add(bottom);
        Button(bottom,stage==null?"Close":"Apply to setup",()=>{if(stage!=null)UpdateSelected();DialogResult=true;});
        if(stage!=null) Button(bottom,"Cancel",()=>DialogResult=false).IsCancel=true;
        DockPanel.SetDock(status,Dock.Bottom);dock.Children.Add(status);
        var panel=new StackPanel();dock.Children.Add(new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        panel.Children.Add(new TextBlock{Text="Library models",FontWeight=FontWeights.SemiBold});
        var choices=new WrapPanel{Margin=new Thickness(0,5,0,8)};panel.Children.Add(choices);choices.Children.Add(library);
        Button(choices,"Import URDF…",()=>
        {
            var picker=new OpenFileDialog{Filter="URDF model (*.urdf)|*.urdf",CheckFileExists=true};
            if(picker.ShowDialog(this)!=true)return;
            var item=UrdfPropLibrary.Import(root,picker.FileName);RefreshLibrary(item.ModelFile);
            status.Text="Added to the shared prop library: "+item.Name;
        });
        if(stage!=null)
        {
            Button(choices,"Add to stage",()=>
            {
                UpdateSelected();
                if(Placements.Count>=128)throw new InvalidOperationException("The stage already has 128 props.");
                if(library.SelectedItem is not UrdfPropEntry entry)throw new InvalidOperationException("Choose a library model first.");
                Placements.Add(new(entry.ModelFile,.6,.45));RefreshPlaced(Placements.Count-1);
            });
            panel.Children.Add(new TextBlock{Text="Placed props",FontWeight=FontWeights.SemiBold});
            panel.Children.Add(placed);
            var form=new WrapPanel{Margin=new Thickness(0,5,0,0)};panel.Children.Add(form);
            string[] labels=["X (m)","Y (m)","Above floor (m)","Yaw (°)","Pitch (°)","Roll (°)","Scale (×)"];
            for(int i=0;i<labels.Length;i++)
            {
                var column=new StackPanel{Margin=new Thickness(0,2,8,2)};
                column.Children.Add(new TextBlock{Text=labels[i]});
                fields[i]=new TextBox{Width=70};column.Children.Add(fields[i]);form.Children.Add(column);
            }
            var actions=new WrapPanel();panel.Children.Add(actions);
            Button(actions,"Update placement",()=>{int index=placed.SelectedIndex;UpdateSelected();RefreshPlaced(index);});
            Button(actions,"Remove from stage",()=>{int index=placed.SelectedIndex;if(index<0)return;Placements.RemoveAt(index);RefreshPlaced(Math.Min(index,Placements.Count-1));});
            placed.SelectionChanged+=(_,_)=>LoadSelected();
            panel.Children.Add(new TextBlock{Text="X and Y are room coordinates. Height is relative to the stage floor. Update a placement before selecting another; Apply previews the setup. Save Scene or Save Background Only to reuse the arrangement.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,0)});
        }
        else panel.Children.Add(new TextBlock{Text="Import a URDF to use it in any 3D backdrop. To place objects, select a 3D background in Scene Setup and open Props.",TextWrapping=TextWrapping.Wrap});
        RefreshLibrary();if(stage!=null)RefreshPlaced(Placements.Count>0?0:-1);
    }
    private Button Button(Panel parent,string text,Action action)
    {
        var b=new Button{Content=text,Margin=new Thickness(4,3,4,3),Padding=new Thickness(8,4,8,4)};
        b.Click+=(_,_)=>{try{action();}catch(Exception ex){status.Text=ex.Message;}};
        parent.Children.Add(b);return b;
    }
    private void RefreshLibrary(string selected=null)
    {
        var list=UrdfPropLibrary.List(root);library.ItemsSource=list;
        library.SelectedItem=list.FirstOrDefault(p=>p.ModelFile==selected)??list.FirstOrDefault();
    }
    private void RefreshPlaced(int selected)
    {
        var names=library.Items.Cast<UrdfPropEntry>().ToDictionary(p=>p.ModelFile,p=>p.Name,StringComparer.OrdinalIgnoreCase);
        placed.ItemsSource=Placements.Select((p,i)=>$"{i+1}. {names.GetValueOrDefault(p.ModelFile,p.ModelFile)}   ·   ({p.X:0.###}, {p.Y:0.###}, {p.Height:0.###}) m").ToArray();
        placed.SelectedIndex=selected;LoadSelected();
    }
    private void LoadSelected()
    {
        int i=placed.SelectedIndex;var p=i<0?null:Placements[i];
        double[] values=p==null?[0,0,0,0,0,0,1]:[p.X,p.Y,p.Height,p.Yaw,p.Pitch,p.Roll,p.Scale];
        for(int n=0;n<fields.Length;n++){fields[n].IsEnabled=p!=null;fields[n].Text=values[n].ToString("G",CultureInfo.CurrentCulture);}
    }
    private void UpdateSelected()
    {
        int i=placed.SelectedIndex;if(i<0)return;
        var v=fields.Select(f=>double.TryParse(f.Text,NumberStyles.Float,CultureInfo.CurrentCulture,out double n)?n:double.NaN).ToArray();
        var p=new UrdfPropPlacement(Placements[i].ModelFile,v[0],v[1],v[2],v[3],v[4],v[5],v[6]);p.Resolve(root);Placements[i]=p;
    }
}
