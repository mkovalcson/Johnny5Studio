using System.Numerics;
using System.Drawing.Drawing2D;
using ServoAnimator;
namespace Johnny5VR;
internal sealed partial class VrBodyMotion
{
    internal static Bitmap DrawCalibrationAlignmentPanel(BodyCalibrationWizard wizard,BodyTrackingFrame? current,string status,string triggerStatus)
    {
        var bitmap=new Bitmap(1400,940);using var g=Graphics.FromImage(bitmap);g.Clear(Color.FromArgb(17,25,37));g.SmoothingMode=SmoothingMode.AntiAlias;
        using var title=new Font("Segoe UI",35,FontStyle.Bold,GraphicsUnit.Pixel);using var text=new Font("Segoe UI",25,FontStyle.Regular,GraphicsUnit.Pixel);
        using var small=new Font("Segoe UI",20,FontStyle.Regular,GraphicsUnit.Pixel);using var bold=new Font("Segoe UI",22,FontStyle.Bold,GraphicsUnit.Pixel);
        var alignment=wizard.Alignment;int step=wizard.Step;bool complete=wizard.Complete;
        g.DrawString(complete?"Calibration complete":$"Calibration {step+1}/{BodyCalibrationWizard.Steps.Length}: {BodyCalibrationWizard.Steps[step].Name}",title,Brushes.White,new RectangleF(30,18,1340,90));
        g.DrawString(complete?status:BodyCalibrationWizard.Steps[step].Instruction,text,Brushes.LightCyan,new RectangleF(30,112,1340,100));
        string state=complete?status:!alignment.Ready?"Adjust amber items: "+alignment.Blocker:wizard.CountdownRemaining>0?$"Prepare fingers: {Math.Ceiling(wizard.CountdownRemaining)}…":wizard.Collecting?"Aligned — hold still. Measurement progresses automatically.":"Aligned — pull and release Trigger to begin measuring.";
        bool inputPaused=triggerStatus.Contains("Paused:",StringComparison.Ordinal);if(inputPaused)state="Input paused: "+triggerStatus[(triggerStatus.IndexOf("Paused:",StringComparison.Ordinal)+7)..];
        g.DrawString(state,bold,alignment.Ready&&!inputPaused?Brushes.LightGreen:Brushes.Orange,new RectangleF(30,216,1340,64));
        string feedback=wizard.LastRejection.Length>0?"Last measurement reset: "+wizard.LastRejection:wizard.Feedback;
        g.DrawString(feedback,small,Brushes.LightGray,new RectangleF(30,283,1340,58));
        g.FillRectangle(Brushes.DarkSlateGray,30,345,1340,13);g.FillRectangle(alignment.Ready?Brushes.MediumSeaGreen:Brushes.DarkOrange,30,345,1340*wizard.Progress,13);
        g.DrawString("Target motion",bold,Brushes.Gold,30,375);g.DrawString("Your live alignment",bold,Brushes.White,375,375);
        g.DrawString("Green = ready  ·  Amber = adjust",bold,Brushes.White,755,375);
        if(!complete)
        {
            // The figure animates the required motion; the gauges under it show how far the
            // live measurement is from each gate, so the user sees what still needs to change.
            float time=(Environment.TickCount64%100000)/1000f;var gauge=wizard.LiveGauge;
            CalibrationPoseGuide.Draw(g,new(30,415,310,gauge==null?345:235),step,wizard.CountdownRemaining,time,gauge!=null);
            if(gauge!=null)CalibrationPoseGuide.DrawGauges(g,new(30,655,310,118),gauge);
        }
        if(current!=null)DrawMeasuredAlignment(g,new(375,418,340,335),wizard,current,alignment);
        int rows=alignment.Checks.Length;float rowHeight=Math.Min(57,390f/Math.Max(1,rows));
        for(int i=0;i<rows;i++)
        {
            var check=alignment.Checks[i];float y=415+i*rowHeight;var brush=check.Good?Brushes.LightGreen:Brushes.Orange;
            g.FillEllipse(brush,755,y+6,13,13);
            string description=check.Label+(check.Required?"":" (optional)")+": "+check.Detail;
            if(description.Length>120)description=description[..117]+"…";
            g.DrawString(description,small,brush,new RectangleF(780,y,588,rowHeight-3));
        }
        g.DrawString("Dashed joints are estimated. Reference rings show placement zones; depth corrections appear in text.",small,Brushes.LightGray,new RectangleF(30,778,1340,30));
        var p=wizard.Profile;
        string pivots=(p.HasWristPivot(true)?$" · Wrist pivot L {p.LeftWristPivot.Length()*100:F0} cm":"")+(p.HasWristPivot(false)?$" R {p.RightWristPivot.Length()*100:F0} cm":"");
        g.DrawString($"Arm lengths L {p.LeftUpper*100:F0}/{p.LeftForearm*100:F0} cm · R {p.RightUpper*100:F0}/{p.RightForearm*100:F0} cm{pivots}  |  Hold steady 1.2 s; movement ≤2.5 cm, rotation ≤11.5°",small,Brushes.LightCyan,new RectangleF(30,817,1340,32));
        g.DrawString("Trigger: arm capture, then release · Fix amber items without pulling again · A: menu / repeat previous step · B: cancel",small,Brushes.White,new RectangleF(30,861,1340,30));
        g.DrawString(triggerStatus,small,Brushes.LightGray,new RectangleF(30,899,1340,30));
        return bitmap;
    }
    private static void DrawMeasuredAlignment(Graphics g,RectangleF area,BodyCalibrationWizard wizard,BodyTrackingFrame current,CalibrationAlignment report)
    {
        var p=wizard.Profile;float scale=Math.Min(area.Width/1.3f,area.Height/1.9f);
        PointF At(Vector3 value)=>new(area.X+area.Width*.5f+value.X*scale,area.Y+area.Height*.23f-value.Y*scale);
        using var estimated=new Pen(Color.SlateGray,3){DashStyle=DashStyle.Dash};using var ring=new Pen(Color.DeepSkyBlue,2){DashStyle=DashStyle.Dash};
        using var green=new Pen(Color.LightGreen,5);using var amber=new Pen(Color.Orange,5);using var gray=new Pen(Color.SlateGray,3);
        using var font=new Font("Segoe UI",17,FontStyle.Bold,GraphicsUnit.Pixel);using var cap=new AdjustableArrowCap(3,4);using var arrow=new Pen(Color.Orange,3){CustomEndCap=cap};
        Brush ColorFor(string part)=>report.PartReady(part)?Brushes.LightGreen:Brushes.Orange;
        var head=At(Vector3.Zero);g.DrawEllipse(report.PartReady("Head")?green:amber,head.X-16,head.Y-32,32,32);
        var neck=At(new(0,-.1f,0));var hip=At(new(0,-.85f,0));g.DrawLine(report.PartReady("Body")?green:amber,neck,hip);
        g.DrawLine(estimated,hip,At(new(-.17f,-1.45f,0)));g.DrawLine(estimated,hip,At(new(.17f,-1.45f,0)));
        foreach(bool left in new[]{true,false})
        {
            string side=left?"Left":"Right";var shoulder=left?p.LeftShoulder:p.RightShoulder;if(shoulder==Vector3.Zero)shoulder=new(left?-.2f:.2f,-.3f,0);
            var hand=wizard.Local((left?current.Left:current.Right).Translation,current.Head);float upper=left?p.LeftUpper:p.RightUpper,fore=left?p.LeftForearm:p.RightForearm;
            var elbow=upper>.1f?BodyTrackingSolver.SolveElbow(shoulder,BodyTrackingSolver.ElbowTarget(shoulder,hand,upper+fore),upper,fore,new(left?-.25f:.25f,-1,.15f)).Elbow:Vector3.Lerp(shoulder,hand,.5f);
            var pen=report.PartReady(side)?green:amber;g.DrawLine(estimated,At(shoulder),At(elbow));g.DrawLine(pen,At(elbow),At(hand));
            foreach(var point in new[]{At(shoulder),At(elbow),At(hand)})g.FillEllipse(ColorFor(side),point.X-5,point.Y-5,10,10);
            var target=left?report.LeftTarget:report.RightTarget;
            if(target!=null)
            {
                var center=At(target.Position);float radius=Math.Max(8,target.Radius*scale);g.DrawEllipse(ring,center.X-radius,center.Y-radius,radius*2,radius*2);
                if(!report.PartReady(side)&&Vector2.Distance(new(At(hand).X,At(hand).Y),new(center.X,center.Y))>radius)g.DrawLine(arrow,At(hand),center);
            }
        }
        g.DrawString("Your LEFT",font,ColorFor("Left"),area.X,area.Bottom-25);g.DrawString("Your RIGHT",font,ColorFor("Right"),area.Right-105,area.Bottom-25);
    }
}
