using System.Drawing.Drawing2D;
using ServoAnimator;
namespace Johnny5VR;
internal static class CalibrationPoseGuide
{
    // time: seconds, animates the required motion (negative = draw the end pose only).
    // compact: leave the lower part for gauges (the figure's labels move up).
    internal static void Draw(Graphics g,RectangleF area,int step,float countdown=0,float time=-1,bool compact=false)
    {
        // Sweep to the target over 1.2 s, hold 0.8 s, ease back 0.5 s, rest 0.5 s.
        float k=1;
        if(time>=0){float t=time%3f;k=t<1.2f?Ease(t/1.2f):t<2f?1:t<2.5f?1-Ease((t-2f)/.5f):0;}
        static float Ease(float x)=>x<.5f?2*x*x:1-MathF.Pow(-2*x+2,2)/2;
        var saved=g.Save();g.SetClip(area);g.SmoothingMode=SmoothingMode.AntiAlias;
        PointF P(float x,float y)=>new(area.X+x*area.Width,area.Y+y*area.Height);
        using var body=new Pen(Color.SlateGray,4){StartCap=LineCap.Round,EndCap=LineCap.Round};
        using var steady=new Pen(Color.LightGreen,5){StartCap=LineCap.Round,EndCap=LineCap.Round};
        using var active=new Pen(Color.Gold,5){StartCap=LineCap.Round,EndCap=LineCap.Round};
        using var cap=new AdjustableArrowCap(3,4);using var arrow=new Pen(Color.Gold,3){CustomEndCap=cap};
        using var font=new Font("Segoe UI",15,FontStyle.Bold,GraphicsUnit.Pixel);
        bool? left=BodyCalibrationWizard.ActiveLeft(step);string motion=BodyCalibrationWizard.Motion(step);bool detail=motion.Length>0;
        float factor=detail?.61f:1;
        PointF B(float x,float y)=>P(x,y*factor);
        PointF head=B(.5f,.1f),neck=B(.5f,.2f),hip=B(.5f,.61f),ls=B(.33f,.24f),rs=B(.67f,.24f),le=B(.32f,.43f),re=B(.68f,.43f),lh=B(.31f,.63f),rh=B(.69f,.63f);
        PointF lk=B(.41f,.77f),rk=B(.59f,.77f),lf=B(.38f,.9f),rf=B(.62f,.9f);
        // Anatomical side is labeled explicitly; gold identifies the active hand.
        if(step==1){le=B(.3f,.1f);re=B(.7f,.1f);lh=B(.28f,.01f);rh=B(.72f,.01f);}
        if(step is 2 or 3){ls=B(.49f,.24f);rs=B(.53f,.26f);le=B(.23f,.24f);re=B(.27f,.26f);lh=step==2?B(.02f,.24f):B(.23f,.04f);rh=step==2?B(.06f,.26f):B(.27f,.06f);}
        if(step==4||detail){lh=B(.30f,.39f);rh=B(.70f,.39f);}
        if(step==BodyCalibrationWizard.CrouchStep){head=B(.5f,.25f);neck=B(.5f,.35f);ls=B(.33f,.38f);rs=B(.67f,.38f);hip=B(.5f,.7f);lk=B(.22f,.8f);rk=B(.78f,.8f);}
        if(step==BodyCalibrationWizard.BowStep){head=B(.17f,.32f);neck=B(.25f,.4f);ls=B(.23f,.41f);rs=B(.3f,.43f);}
        void Line(Pen pen,PointF a,PointF b)=>g.DrawLine(pen,a,b);
        void Mark(PointF point)=>g.FillEllipse(Brushes.Gold,point.X-6,point.Y-6,12,12);
        Line(body,neck,hip);Line(body,ls,rs);Line(body,hip,lk);Line(body,hip,rk);Line(body,lk,lf);Line(body,rk,rf);
        float radius=area.Width*.06f;g.DrawEllipse(body,head.X-radius,head.Y-radius,radius*2,radius*2);Line(body,new(head.X,head.Y+radius),neck);
        Line(steady,ls,le);Line(steady,rs,re);Line(left==true?active:steady,le,lh);Line(left==false?active:steady,re,rh);
        if(step<=2){Mark(ls);Mark(rs);}if(step==3){Mark(le);Mark(re);}
        if(detail)
        {
            Mark(left==true?lh:rh);g.DrawString((left==true?"LEFT":"RIGHT")+" only",font,Brushes.Gold,new RectangleF(area.X,area.Y+area.Height*.50f,area.Width,22));
            var origin=P(.46f,.75f);float w=area.Width,h=area.Height;
            if(motion=="Pitch")
            {
                bool up=step is 5 or 9;
                Line(steady,P(.08f,.75f),origin);Line(body,origin,P(.87f,.75f));
                float bend=k*(up?-1:1)*28*MathF.PI/180,reach=w*.34f;
                var tip=new PointF(origin.X+reach*MathF.Cos(bend),origin.Y+reach*MathF.Sin(bend)*h/w*(w/h));Line(active,origin,tip);Mark(origin);
                g.DrawArc(arrow,origin.X-w*.12f,origin.Y-h*.14f,w*.48f,h*.28f,up?0:300,up?-60:60);
                g.DrawString("HINGE bend · side view",font,Brushes.LightGreen,new RectangleF(area.X,area.Y+area.Height*.50f+22,w,24));
                g.DrawString("Forearm stays still",font,Brushes.White,new RectangleF(area.X,area.Y+h*.91f,w,26));
            }
            else if(motion=="Roll")
            {
                bool inward=step is 7 or 11;
                float size=Math.Min(w*.44f,h*.25f);var ring=new RectangleF(origin.X-size/2,origin.Y-size/2,size,size);
                float tilt=k*(inward?1:-1);
                g.DrawEllipse(steady,ring);Line(active,new(origin.X-size*.28f,origin.Y+size*.2f*tilt),new(origin.X+size*.28f,origin.Y-size*.2f*tilt));
                g.FillEllipse(Brushes.Gold,origin.X+size*.22f,origin.Y-size*.31f*tilt,10,10);
                g.DrawArc(arrow,ring.X-13,ring.Y-13,ring.Width+26,ring.Height+26,inward?30:150,inward?240:-240);
                g.DrawString("AXIAL roll · end view",font,Brushes.LightGreen,new RectangleF(area.X,area.Y+area.Height*.50f+22,w,24));
                g.DrawString("Straight wrist · palm turns",font,Brushes.White,new RectangleF(area.X,area.Y+h*.91f,w,26));
            }
            else
            {
                bool outward=step is 14 or 16;var elbow=P(.5f,.7f);Mark(elbow);
                Line(steady,P(.5f,.58f),elbow);Line(body,elbow,P(.5f,.9f));
                float sign=left==true?-1:1;if(!outward)sign=-sign;
                float sweep=k*sign*58*MathF.PI/180;var rest=P(.5f,.9f);float length=rest.Y-elbow.Y;
                Line(active,elbow,new(elbow.X+length*MathF.Sin(sweep),elbow.Y+length*MathF.Cos(sweep)));g.DrawArc(arrow,area.X+w*.18f,area.Y+h*.51f,w*.64f,h*.38f,90,-sign*55);
                g.DrawString("ARM twist · top view",font,Brushes.LightGreen,new RectangleF(area.X,area.Y+area.Height*.50f+22,w,24));
                g.DrawString("Elbow fixed · hand sweeps",font,Brushes.White,new RectangleF(area.X,area.Y+h*.91f,w,26));
            }
        }
        else
        {
            if(BodyCalibrationWizard.IsFingerStep(step))foreach(var hand in new[]{lh,rh})
            {if(step==BodyCalibrationWizard.HandsClosedStep)g.DrawRectangle(active,hand.X-10,hand.Y,20,17);else for(int i=0;i<5;i++)Line(active,hand,new(hand.X+(i-2)*8,hand.Y+22-Math.Abs(i-2)*3));}
            string caption=step switch{0=>"Arms relaxed down",1=>"Both arms straight up",2=>"Straight forward · side view",3=>"Elbows 90° · side view",4=>"Elbows DOWN · forearms forward",17=>"Fingers OPEN",18=>"Fingers CLOSED",19=>"Crouch · stay upright",20=>"Bend at waist",_=>"Calibration complete"};
            g.DrawString(caption,font,Brushes.LightGreen,new RectangleF(area.X,area.Y+area.Height-38,area.Width,38));
        }
        if(countdown>0){using var countFont=new Font("Segoe UI",36,FontStyle.Bold,GraphicsUnit.Pixel);g.DrawString(Math.Ceiling(countdown).ToString(),countFont,Brushes.Gold,P(.75f,.05f));}
        g.Restore(saved);
    }
    // One bar per gate: the green band is where the measurement needs to be, the marker is where it is now.
    internal static void DrawGauges(Graphics g,RectangleF area,CalibrationGauge gauge)
    {
        var saved=g.Save();g.SetClip(area);g.SmoothingMode=SmoothingMode.AntiAlias;
        using var font=new Font("Segoe UI",15,FontStyle.Bold,GraphicsUnit.Pixel);using var track=new SolidBrush(Color.FromArgb(60,70,85));
        var rows=new List<(string Label,float Value,float Min,float Max,float GoodFrom,float GoodTo,bool Ok,string Text)>();
        bool twist=gauge.Motion=="Twist";
        float top=Math.Max(gauge.AngleNeeded*3,twist?30:45);
        rows.Add((twist?"Sweep":"Moved",gauge.Angle,twist?-top:0,top,gauge.AngleNeeded,top,gauge.AngleOk,$"{gauge.Angle:F0}° · need {gauge.AngleNeeded:F0}°+"));
        if(!float.IsNaN(gauge.Opposite))rows.Add(("Opposite",gauge.Opposite,0,180,gauge.OppositeNeeded,180,gauge.OppositeOk,$"{gauge.Opposite:F0}° · need {gauge.OppositeNeeded:F0}°+"));
        if(!float.IsNaN(gauge.Similarity))rows.Add(("Like bend",gauge.Similarity*100,0,100,0,gauge.SimilarityLimit*100,gauge.SimilarityOk,$"{gauge.Similarity:P0} · keep under {gauge.SimilarityLimit:P0}"));
        float rowHeight=Math.Min(38,area.Height/Math.Max(1,rows.Count));
        for(int i=0;i<rows.Count;i++)
        {
            var r=rows[i];float y=area.Y+i*rowHeight;float barX=area.X+78,barW=area.Width-80,barY=y+18,barH=7;
            float X(float v)=>barX+barW*Math.Clamp((v-r.Min)/(r.Max-r.Min),0,1);
            g.DrawString(r.Label,font,r.Ok?Brushes.LightGreen:Brushes.Orange,area.X,y);
            g.DrawString(r.Text,font,r.Ok?Brushes.LightGreen:Brushes.Orange,barX,y-1);
            g.FillRectangle(track,barX,barY,barW,barH);
            g.FillRectangle(Brushes.MediumSeaGreen,X(r.GoodFrom),barY,Math.Max(2,X(r.GoodTo)-X(r.GoodFrom)),barH);
            if(twist&&i==0){float zero=X(0);g.FillRectangle(Brushes.LightGray,zero-1,barY-3,2,barH+6);}
            float marker=X(r.Value);var tri=new[]{new PointF(marker,barY+barH+1),new PointF(marker-7,barY+barH+10),new PointF(marker+7,barY+barH+10)};
            g.FillPolygon(r.Ok?Brushes.LightGreen:Brushes.Orange,tri);
        }
        g.Restore(saved);
    }
    internal static void SavePreview(string path)
    {
        int count=BodyCalibrationWizard.Steps.Length;using var bitmap=new Bitmap(1440,((count+3)/4)*480);using var g=Graphics.FromImage(bitmap);g.Clear(Color.FromArgb(17,25,37));
        using var font=new Font("Segoe UI",18,FontStyle.Bold,GraphicsUnit.Pixel);
        for(int step=0;step<count;step++)
        {float x=(step%4)*360,y=(step/4)*480;g.DrawString($"{step+1}. {BodyCalibrationWizard.Steps[step].Name}",font,Brushes.White,new RectangleF(x+15,y+10,330,55));Draw(g,new(x+35,y+75,290,390),step);}
        bitmap.Save(path);
        using var comparison=new Bitmap(1200,620);using var cg=Graphics.FromImage(comparison);cg.Clear(Color.FromArgb(17,25,37));
        int[] samples=[5,7,14];for(int i=0;i<samples.Length;i++)
        {cg.DrawString(BodyCalibrationWizard.Steps[samples[i]].Name,font,Brushes.White,new RectangleF(i*400+18,15,365,55));Draw(cg,new(i*400+28,80,340,520),samples[i]);}
        comparison.Save(Path.Combine(Path.GetDirectoryName(path)!,"arm-calibration-motion-guide.png"));
    }
}
