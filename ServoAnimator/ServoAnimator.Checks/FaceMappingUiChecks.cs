using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ServoAnimator;

internal static partial class Program
{
    private static void FaceMappingUiChecks()
    {
        var app=new App();app.InitializeComponent();DisableTestStartup(app);
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        object Get(object source,string name)=>source.GetType().GetField(name,flags)!.GetValue(source)!;
        void Call(object source,string name)=>source.GetType().GetMethod(name,flags)!.Invoke(source,null);
        var client=new FaceTrackingClient(()=>true);
        client.AcceptPacket(new(){TimestampMs=1,Detected=true,Values=new(){["headYaw"]=15}});
        var current=new FaceTrackingSettings();
        var face=new FaceTrackingWindow(client,current){ShowInTaskbar=false,ShowActivated=false,Left=-10000,Top=-10000};
        var grid=(DataGrid)Get(face,"_grid");
        var draft=(FaceTrackingSettings)Get(face,"_draft");
        var preview=(CheckBox)Get(face,"_preview");
        int applies=0,previews=0,saves=0;
        face.ApplyRequested=_=>applies++;
        face.PreviewChanged=()=>previews++;
        face.SaveRequested=_=>saves++;
        try
        {
            draft.Mappings[0].OutputMax=75;
            preview.IsChecked=true;
            Check(applies==0&&saves==0&&previews==1,"Enabling preview does not apply pending mapping edits.");
            Check(face.PreviewSettings.Mappings[0].OutputMax==50,"Preview retains the last applied mapping.");
            preview.IsChecked=false;
            Check(previews==2&&applies==0,"Disabling preview is also independent of Apply.");
            Call(face,"Apply");
            Check(applies==1&&face.PreviewSettings.Mappings[0].OutputMax==75&&saves==0,"Explicit Apply activates the draft without saving.");
            Check(grid.Columns.Any(c=>Equals(c.Header,"Live input / target")),"The mapping table exposes live meters.");
            ((CheckBox)Get(face,"_advancedMappings")).IsChecked=false;
            Check(grid.Columns.Count(c=>c.Visibility==Visibility.Visible)==7,"Meters remain visible with advanced settings hidden.");

            var mapping=new FaceMapping{Signal="headYaw",Neutral=5};
            var learning=new FaceRangeLearning(mapping);
            for(int i=0;i<50;i++)learning.Observe(i,-15+40d*i/49);
            int count=learning.Count;learning.Observe(49,999);learning.Observe(100,double.NaN);
            Check(learning.Count==count,"Duplicate frames and non-finite inputs cannot affect learning.");
            Check(learning.TryRange(out double min,out double max,out _)&&min<0&&max>0&&max<=20&&min>=-20,"Learned limits are relative to the existing neutral.");
            var still=new FaceRangeLearning(mapping);
            for(int i=0;i<30;i++)still.Observe(i,10);
            Check(!still.TryRange(out _,out _,out var why)&&why.Contains("movement"),"Insufficient movement gives a reason to retry.");
            still.Observe(100,1000);
            Check(!still.TryRange(out _,out _,out _),"A single outlier cannot turn a stationary face into a valid learned range.");
            var shortSample=new FaceRangeLearning(mapping);shortSample.Observe(1,10);
            Check(!shortSample.TryRange(out _,out _,out why)&&why.Contains("20"),"Too few frames cannot produce an accepted range.");
            var jaw=new FaceRangeLearning(new FaceMapping{Signal="jawOpen"});
            for(int i=0;i<60;i++)jaw.Observe(i,i/100d);
            Check(jaw.TryRange(out min,out max,out _)&&min==0&&max>0,"One-sided expressions retain neutral within the learned range.");
            Check(FaceRangeWindow.Instruction("headYaw").Contains("left")&&FaceRangeWindow.Instruction("jawOpen").Contains("mouth"),"Instructions match the selected face movement.");

            face.Show();
            client.AcceptPacket(new(){TimestampMs=2,Detected=true,Values=new(){["headYaw"]=15}});
            Call(face,"Refresh");
            var meters=(HashSet<FaceMappingMeter>)Get(face,"_mappingMeters");
            Check(meters.Count>0,"Mapping cells create visible live meters.");
            var first=meters.First(m=>ReferenceEquals(m.DataContext,draft.Mappings[0]));
            Check(Get(first,"input") is double value&&value==15,"Meters receive the latest valid face signal.");
            client.AcceptPacket(new(){TimestampMs=2,Detected=false});
            Call(face,"Refresh");
            Check(Get(first,"input")==null,"Tracking loss clears the meter instead of displaying stale input.");
            client.AcceptPacket(new(){TimestampMs=3,Detected=true,Values=new(){["headYaw"]=0}});
            grid.SelectedItem=draft.Mappings[0];
            double neutral=draft.Mappings[0].Neutral;
            int beforeApply=applies;
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>
            {
                var dialog=Application.Current.Windows.OfType<FaceRangeWindow>().Single();
                client.AcceptPacket(new(){TimestampMs=4,Detected=true,Values=new(){["headYaw"]=0}});
                Call(dialog,"Start");
                var active=(FaceRangeLearning)Get(dialog,"active");
                for(int i=0;i<60;i++)active.Observe(i,-20+40d*i/59);
                Call(dialog,"Review");
                Check(((Button)Get(dialog,"accept")).IsEnabled,"A valid measurement enables Accept range.");
                dialog.DialogResult=true;
            }));
            Call(face,"LearnSelectedRange");
            Check(draft.Mappings[0].InputMin<0&&draft.Mappings[0].InputMax>0&&draft.Mappings[0].Neutral==neutral,"Accepting a learned range updates the draft and preserves neutral.");
            Check(applies==beforeApply&&face.PreviewSettings.Mappings[0].InputMin==-30,"Learned draft ranges wait for Apply.");
            double accepted=draft.Mappings[0].InputMin;
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>
                Application.Current.Windows.OfType<FaceRangeWindow>().Single().DialogResult=false));
            Call(face,"LearnSelectedRange");
            Check(draft.Mappings[0].InputMin==accepted,"Canceling Learn range preserves the existing draft.");
            client.AcceptPacket(new(){TimestampMs=5,Detected=true,Values=new(){["headYaw"]=15}});
            face.AppliedOutput=mapping=>mapping.Signal=="headYaw"?12:null;
            Call(face,"Refresh");
            ((CheckBox)Get(face,"_advancedMappings")).IsChecked=true;
            string previewFolder=PreviewDirectory("face-mapping");
            System.IO.Directory.CreateDirectory(previewFolder);
            RenderControl((FrameworkElement)face.Content,System.IO.Path.Combine(previewFolder,"FaceMappingLive.png"),1292,944);
            Console.WriteLine("Preview: "+System.IO.Path.Combine(previewFolder,"FaceMappingLive.png"));
        }
        finally{face.Close();client.Dispose();app.Shutdown();}
    }
}
