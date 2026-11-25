using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace ServoAnimator;

internal sealed class FaceRangeWindow:Window
{
    private readonly FaceTrackingClient client;
    private readonly FaceTrackingSettings settings;
    private readonly FaceMapping mapping;
    private readonly Stopwatch clock=new();
    private readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(50)};
    private readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,12)};
    private readonly TextBlock measured=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)};
    private readonly Button start=new(){Content="Start",Padding=new Thickness(12,5,12,5),Margin=new Thickness(0,0,8,0)};
    private readonly Button accept=new(){Content="Accept range",IsEnabled=false,Padding=new Thickness(12,5,12,5),Margin=new Thickness(0,0,8,0)};
    private bool reviewing;
    internal double LearnedMinimum {get;private set;}
    internal double LearnedMaximum {get;private set;}

    internal FaceRangeWindow(FaceTrackingClient client,FaceTrackingSettings settings,FaceMapping mapping)
    {
        this.client=client;this.settings=settings;this.mapping=mapping;
        Title="Learn range · "+UiNames.Words(mapping.Signal);
        Width=540;SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty,"AppBackground");SetResourceReference(ForegroundProperty,"PrimaryText");
        var layout=new StackPanel{Margin=new Thickness(20)};
        layout.Children.Add(new TextBlock{Text=Instruction(mapping.Signal),FontSize=16,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap});
        layout.Children.Add(new TextBlock{Text="After a three-second countdown, move through your comfortable range for six seconds. Keep your face in view. Existing neutral and robot output limits are preserved.",
            TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,0)});
        layout.Children.Add(status);layout.Children.Add(measured);
        var actions=new WrapPanel();
        var cancel=new Button{Content="Cancel",Padding=new Thickness(12,5,12,5)};
        actions.Children.Add(start);actions.Children.Add(accept);actions.Children.Add(cancel);
        layout.Children.Add(actions);Content=layout;
        status.Text="Press Start when ready.";
        start.Click+=(_,_)=>Start();
        accept.Click+=(_,_)=>{DialogResult=true;};
        cancel.Click+=(_,_)=>{DialogResult=false;};
        timer.Tick+=(_,_)=>Tick();
        Closed+=(_,_)=>{timer.Stop();clock.Stop();};
    }

    private FaceRangeLearning active;
    private void Start()
    {
        if(!client.Fresh){status.Text="Face not tracked. Look at the camera, then press Start.";return;}
        active=new(mapping);
        reviewing=false;accept.IsEnabled=false;start.IsEnabled=false;
        clock.Restart();timer.Start();Tick();
    }

    private void Tick()
    {
        double seconds=clock.Elapsed.TotalSeconds;
        if(seconds<3)
        {status.Text=$"Starting in {Math.Ceiling(3-seconds):0}… Get into position.";return;}
        if(seconds>=9){Review();return;}
        if(!client.Fresh||client.Latest is not {} packet)
        {status.Text="Face lost. Face the camera; Retry if the measured range is incomplete.";return;}
        var signals=settings.SignalValues(packet,out _);
        if(!signals.TryGetValue(mapping.Signal,out double input)||!double.IsFinite(input))
        {status.Text="This face signal is unavailable. Try another mapping or restart tracking.";return;}
        active.Observe(packet.TimestampMs,input);
        status.Text=$"Measuring · {Math.Ceiling(9-seconds):0} seconds remaining";
        measured.Text=$"{active.Count} frames · input relative to neutral: {input-mapping.Neutral:0.###}";
    }

    private void Review()
    {
        if(reviewing)return;
        reviewing=true;timer.Stop();clock.Stop();
        bool valid=active.TryRange(out double min,out double max,out string reason);
        LearnedMinimum=Math.Round(min,4);LearnedMaximum=Math.Round(max,4);status.Text=reason;
        measured.Text=$"{active.Count} frames · relative input range {min:0.###} to {max:0.###}";
        accept.IsEnabled=valid;start.IsEnabled=true;start.Content="Retry";
    }

    internal static string Instruction(string signal)=>signal switch
    {
        "headYaw"=>"Slowly look comfortably left, then right.",
        "headPitch"=>"Slowly look comfortably up, then down.",
        "headRoll"=>"Slowly tilt your head toward each shoulder.",
        "gazeX"=>"Keep your head still and look left, then right.",
        "gazeY"=>"Keep your head still and look up, then down.",
        "jawOpen"=>"Relax your jaw, then open your mouth comfortably and close it.",
        "squint" or "eyeSquintLeft" or "eyeSquintRight"=>"Relax your eyes, then squint and relax again.",
        "LPiston" or "RPiston"=>"Slowly nod and tilt through your comfortable head movement.",
        _=>"Relax your face, then perform "+UiNames.Words(signal)+" through its comfortable range."
    };
}
