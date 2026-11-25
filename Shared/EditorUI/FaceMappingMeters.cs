using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ServoAnimator;

internal sealed partial class FaceTrackingWindow
{
    private readonly HashSet<FaceMappingMeter> _mappingMeters=new();

    private void AddLiveMappingColumn()
    {
        var element=new FrameworkElementFactory(typeof(FaceMappingMeter));
        element.SetValue(FrameworkElement.HeightProperty,36d);
        element.AddHandler(FrameworkElement.LoadedEvent,new RoutedEventHandler((sender,_)=>
        {
            _mappingMeters.Add((FaceMappingMeter)sender);
            RefreshMappingMeters(_client.Latest);
        }));
        element.AddHandler(FrameworkElement.UnloadedEvent,new RoutedEventHandler((sender,_)=>_mappingMeters.Remove((FaceMappingMeter)sender)));
        _grid.Columns.Insert(3,new DataGridTemplateColumn { Header="Live input / target",Width=170,
            IsReadOnly=true,CellTemplate=new DataTemplate{VisualTree=element} });
    }

    private void RefreshMappingMeters(FacePacket packet)
    {
        var signals=_client.Fresh&&packet!=null?_draft.SignalValues(packet,out _):null;
        foreach(var meter in _mappingMeters)
        {
            double? input=null,applied=null;
            if(meter.DataContext is FaceMapping mapping)
            {
                if(signals!=null&&signals.TryGetValue(mapping.Signal,out double value)&&double.IsFinite(value))input=value;
                applied=AppliedOutput?.Invoke(mapping);
            }
            meter.Update(input,applied);
        }
    }

    private void LearnSelectedRange()
    {
        if(!CommitMappings())return;
        if(_grid.SelectedItem is not FaceMapping mapping)
        { _message="Select the mapping you want to learn first.";Refresh();return; }
        if(!_client.Fresh)
        { _message="Start the camera and face it before learning a range.";Refresh();return; }
        if(!double.IsFinite(mapping.Neutral))
        { _message="Correct the mapping's neutral value before learning a range.";Refresh();return; }
        var window=new FaceRangeWindow(_client,_draft,mapping){Owner=this};
        if(window.ShowDialog()!=true)return;
        mapping.InputMin=window.LearnedMinimum;
        mapping.InputMax=window.LearnedMaximum;
        _grid.Items.Refresh();
        _message=$"Range learned for {UiNames.Words(mapping.Signal)}. Apply to try it, or Save configuration to keep it.";
        Refresh();
    }
}

public sealed class FaceMappingMeter:FrameworkElement
{
    private double? input,applied;
    private static readonly SolidColorBrush MeterBackground=new(Color.FromRgb(24,33,44));
    static FaceMappingMeter(){MeterBackground.Freeze();}
    public FaceMappingMeter(){MinWidth=150;IsHitTestVisible=true;ClipToBounds=true;}

    internal void Update(double? sample,double? output)
    {
        input=sample;applied=output;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext draw)
    {
        if(DataContext is not FaceMapping mapping)return;
        draw.DrawRectangle(MeterBackground,null,new Rect(RenderSize));
        double width=Math.Max(1,ActualWidth-10);
        bool valid=double.IsFinite(mapping.Neutral)&&double.IsFinite(mapping.InputMin)&&double.IsFinite(mapping.InputMax)&&
            mapping.InputMax>mapping.InputMin&&double.IsFinite(mapping.OutputMin)&&double.IsFinite(mapping.OutputMax)&&
            mapping.OutputMax>=mapping.OutputMin&&double.IsFinite(mapping.DeadZone)&&mapping.DeadZone>=0&&mapping.DeadZone<.5;
        void Text(string text,double y,Brush color)
        {
            var label=new FormattedText(text,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),10,color,VisualTreeHelper.GetDpi(this).PixelsPerDip)
                {MaxTextWidth=width,MaxLineCount=1,Trimming=TextTrimming.CharacterEllipsis};
            draw.DrawText(label,new Point(5,y));
        }
        if(!valid){Text("Check mapping range",3,Brushes.Gold);return;}
        double Normalize(double value,double min,double max)=>max==min?.5:Math.Clamp((value-min)/(max-min),0,1);
        double neutral=Normalize(0,mapping.InputMin,mapping.InputMax);
        double? centered=input-mapping.Neutral;
        double? target=input.HasValue?mapping.Map(input.Value):null;
        Text(centered.HasValue?$"Input {centered:0.###}":"Input —",0,Brushes.LightGray);
        draw.DrawRoundedRectangle(Brushes.DimGray,null,new Rect(5,13,width,3),2,2);
        double start=Math.Max(0,neutral-mapping.DeadZone);
        double end=Math.Min(1,neutral+mapping.DeadZone);
        draw.DrawRectangle(Brushes.DarkGoldenrod,null,new Rect(5+start*width,13,(end-start)*width,3));
        draw.DrawLine(new Pen(Brushes.White,1),new Point(5+neutral*width,12),new Point(5+neutral*width,17));
        if(centered.HasValue)
        {
            double marker=5+Normalize(centered.Value,mapping.InputMin,mapping.InputMax)*width;
            draw.DrawEllipse(Brushes.LightSkyBlue,null,new Point(marker,14.5),2.5,2.5);
        }
        Text(target.HasValue?$"Target {target:0.#}"+(applied.HasValue?$" · applied {applied:0.#}":""):"Target —",18,Brushes.LightGray);
        draw.DrawRoundedRectangle(Brushes.DimGray,null,new Rect(5,31,width,3),2,2);
        if(target.HasValue)
        {
            double marker=5+Normalize(target.Value,mapping.OutputMin,mapping.OutputMax)*width;
            draw.DrawEllipse(Brushes.LightSkyBlue,null,new Point(marker,32.5),2.5,2.5);
        }
        if(applied.HasValue)
        {
            double marker=5+Normalize(applied.Value,mapping.OutputMin,mapping.OutputMax)*width;
            draw.DrawLine(new Pen(Brushes.Cyan,2),new Point(marker,29),new Point(marker,35));
        }
        ToolTip=string.Join(Environment.NewLine,
            $"Input limits relative to neutral: {mapping.InputMin:0.###} to {mapping.InputMax:0.###}",
            $"Raw neutral: {mapping.Neutral:0.###}. White line: neutral. Gold area: dead zone.",
            $"Draft output limits: {mapping.OutputMin:0.###} to {mapping.OutputMax:0.###}.",
            mapping.Enabled?"Mapping enabled.":"Draft mapping disabled.",
            "Blue dot: draft target. Cyan line: last applied target after smoothing, before servo speed limiting.");
    }
}
