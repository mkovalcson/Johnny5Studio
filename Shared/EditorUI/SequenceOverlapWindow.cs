using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace ServoAnimator;
internal sealed class SequenceOverlapWindow:Window
{
    internal readonly Dictionary<SequenceControlKey,SequenceOverlapChoice> Choices=[];
    internal SequenceOverlapWindow(Window owner,SequenceInsertion plan)
    {
        Owner=owner;Title="Insert sequence — overlapping controls";Width=940;Height=700;MinWidth=660;MinHeight=420;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
        Background=new SolidColorBrush(Color.FromRgb(25,32,42));Foreground=Brushes.White;
        var root=new DockPanel{Margin=new Thickness(18)};Content=root;
        var header=new TextBlock{Text=$"Insertion span: {plan.Start:F3}–{plan.End:F3} seconds · {plan.Overlaps.Count} overlapping controls\nChoose separately for each control. Only commands in this span are affected. The inserted commands form one new group. Merge keeps both sets, with inserted commands winning exact-time matches.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,14),FontSize=15};DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};
        var insert=new Button{Content="Insert sequence",IsDefault=true,Padding=new Thickness(16,8,16,8)};insert.Click+=(_,_)=>DialogResult=true;buttons.Children.Add(insert);
        buttons.Children.Add(new Button{Content="Cancel",IsCancel=true,Padding=new Thickness(16,8,16,8),Margin=new Thickness(10,0,0,0)});DockPanel.SetDock(buttons,Dock.Bottom);root.Children.Add(buttons);
        var rows=new StackPanel();root.Children.Add(new ScrollViewer{Content=rows,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
        int index=0;
        foreach(var overlap in plan.Overlaps)
        {
            var key=overlap.Key;Choices[key]=SequenceOverlapChoice.Merge;
            var row=new StackPanel{Margin=new Thickness(12)};
            rows.Children.Add(new Border{Child=row,Background=new SolidColorBrush(Color.FromRgb(39,49,64)),CornerRadius=new CornerRadius(5),Margin=new Thickness(0,0,0,12)});
            string name=ControlLabels.Text(key.Servo.ToString())+(key.Control.HasValue?" / "+ControlLabels.Text(key.Control.Value.ToString()):"");
            row.Children.Add(new TextBlock{Text=$"{name}    Existing: {overlap.ExistingCount:N0} commands    Inserted: {overlap.InsertedCount:N0} commands",TextWrapping=TextWrapping.Wrap,FontSize=16,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,8)});
            string group="Overlap"+index++;
            foreach(var (choice,label) in new[]{(SequenceOverlapChoice.KeepOriginal,"1. Keep original — remove inserted commands for this control"),(SequenceOverlapChoice.UseInserted,"2. Use inserted — remove original commands for this control"),(SequenceOverlapChoice.Merge,"3. Merge — inserted commands win at the same time point")})
            {
                var radio=new RadioButton{GroupName=group,IsChecked=choice==SequenceOverlapChoice.Merge,Margin=new Thickness(0,5,0,5),Content=new TextBlock{Text=label,TextWrapping=TextWrapping.Wrap,Foreground=Brushes.White},Foreground=Brushes.White};
                radio.Checked+=(_,_)=>Choices[key]=choice;row.Children.Add(radio);
            }
        }
    }
}
