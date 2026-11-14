using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace ServoAnimator;

internal enum UrdfWorkshopPart { None, PaintedHousing, Flap, Shell, Brass, Rubber, Protected, Vent,
    BodyPaint, BodyMetal, BodyBrass, BodyRubber, BodyProtected }

internal static partial class UrdfWeathering
{
    // Head recipes retain their approved appearance. Body recipes also have
    // a restrained Light Wear variant, using the same exposed contact masks.
    internal static UrdfWorkshopPart WorkshopPart(string source, string name, string link = "", Color? color = null)
    {
        source=source.Replace('\\','/').ToLowerInvariant();
        if(link.StartsWith("body_",StringComparison.Ordinal)||source.Contains("/body/"))
        {
            if(new[]{"bellows","hose","rubber","tread_pads","tread_band","cable","belt"}.Any(name.Contains))return UrdfWorkshopPart.BodyRubber;
            if(new[]{"_chest_a_","_chest_b_","_chest_c_","waist_module","pcb","lcd","screen","led","firing_window","shaft","piston"}.Any(name.Contains)
                ||new[]{"screw","bolt","rivet","socket","washer","_pin"}.Any(name.Contains)&&!name.Contains("finger"))return UrdfWorkshopPart.BodyProtected;
            if(name.Contains("brass")||name.Contains("gold")||name.Contains("e7c863")||name.Contains("d89d8b"))return UrdfWorkshopPart.BodyBrass;
            var c=color??Colors.Gray;
            return Math.Max(c.R,Math.Max(c.G,c.B))<85||Math.Max(c.R,Math.Max(c.G,c.B))-Math.Min(c.R,Math.Min(c.G,c.B))>45
                ?UrdfWorkshopPart.BodyPaint:UrdfWorkshopPart.BodyMetal;
        }
        if(source.Contains("/eyemechanism/") || source.Contains("/slotblackout/") || source.Contains("/simplemouth/")
            || source.EndsWith("/left_piston.stl") || source.EndsWith("/right_piston.stl")) return UrdfWorkshopPart.Protected;
        if(source.Contains("/simplifiedhead2/"))
        {
            if(System.IO.Path.GetFileName(source).StartsWith("vent_left_fin",StringComparison.Ordinal)
                || System.IO.Path.GetFileName(source).StartsWith("vent_right_fin",StringComparison.Ordinal))return UrdfWorkshopPart.Vent;
            if(source.EndsWith("_flap_panel.stl"))return UrdfWorkshopPart.Flap;
            if(source.EndsWith("/static_020202.stl")||source.EndsWith("/static_080b4e.stl"))return UrdfWorkshopPart.PaintedHousing;
            if(source.Contains("eye_tube"))return UrdfWorkshopPart.Shell;
            return name.Contains("e7c863")?UrdfWorkshopPart.Brass:UrdfWorkshopPart.Shell;
        }
        if(source.Contains("/rearcover/"))return UrdfWorkshopPart.Shell;
        if(source.Contains("/neckassembly/")||source.Contains("/headtopcad/"))
        {
            if(name.Contains("black")||name.Contains("rubber"))return UrdfWorkshopPart.Rubber;
            if(name.Contains("gold")||name.Contains("e7c863"))return UrdfWorkshopPart.Brass;
            if(name.Contains("080b4e"))return UrdfWorkshopPart.PaintedHousing;
            return UrdfWorkshopPart.Shell;
        }
        return UrdfWorkshopPart.None;
    }

    internal static bool IsBodyRecipe(UrdfWorkshopPart part)=>part>=UrdfWorkshopPart.BodyPaint;
    private static UrdfWorkshopPart SurfaceRecipe(UrdfWorkshopPart part)=>part switch
    {
        UrdfWorkshopPart.BodyPaint=>UrdfWorkshopPart.PaintedHousing,UrdfWorkshopPart.BodyMetal=>UrdfWorkshopPart.Shell,
        UrdfWorkshopPart.BodyBrass=>UrdfWorkshopPart.Brass,UrdfWorkshopPart.BodyRubber=>UrdfWorkshopPart.Rubber,
        UrdfWorkshopPart.BodyProtected=>UrdfWorkshopPart.Protected,_=>part
    };
    internal static Material WorkshopMaterial(Color color, Color specular, double shine, MeshGeometry3D mapped, UrdfWorkshopPart part, int level = 2)
    {
        bool body=IsBodyRecipe(part);double strength=body&&level==1?.28:1;
        var recipe=SurfaceRecipe(part);
        var group=new MaterialGroup();
        bool protectedPart=recipe==UrdfWorkshopPart.Protected;
        group.Children.Add(new DiffuseMaterial(protectedPart||body&&recipe==UrdfWorkshopPart.Rubber ? new SolidColorBrush(color)
            : new ImageBrush(WorkshopTexture(color,mapped,part,level)) { Stretch=Stretch.Fill }));
        // Broad, low-energy highlights leave the dark paint readable. A uniform
        // material setting works in both renderers without inventing reflections.
        double gloss=recipe switch { UrdfWorkshopPart.PaintedHousing=>.3,UrdfWorkshopPart.Flap=>.25,
            UrdfWorkshopPart.Brass=>.4,UrdfWorkshopPart.Shell=>.25,UrdfWorkshopPart.Vent=>.35,UrdfWorkshopPart.Rubber=>.07,_=>1 };
        if(body)gloss=1-(1-gloss)*strength*(recipe==UrdfWorkshopPart.Rubber?1:.2);
        group.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromArgb(specular.A,
            (byte)(specular.R*gloss),(byte)(specular.G*gloss),(byte)(specular.B*gloss))),protectedPart?shine:Math.Max(12,shine*(1-.35*strength))));
        group.Freeze();return group;
    }

    internal static BitmapSource WorkshopTexture(Color color,MeshGeometry3D mapped,UrdfWorkshopPart part,int level = 2)
    {
        bool body=IsBodyRecipe(part);double strength=body&&level==1?.28:1;
        part=SurfaceRecipe(part);
        var layout=Layouts.GetValue(mapped,_=>new Layout(new[]{mapped.Bounds}));
        EdgeMaps.TryGetValue(mapped,out var edges);DepthMaps.TryGetValue(mapped,out var depth);ContactMaps.TryGetValue(mapped,out var contact);
        if(part==UrdfWorkshopPart.Vent&&ConvexEdgeMaps.TryGetValue(mapped,out var finEdges))edges=finEdges;
        var pixels=new byte[layout.Width*layout.Height*4];
        bool paint=part is UrdfWorkshopPart.PaintedHousing or UrdfWorkshopPart.Flap or UrdfWorkshopPart.Vent;
        for(int chart=0;chart<layout.Bounds.Length;chart++)for(int face=0;face<6;face++)
        {
            int tileX=chart%layout.Columns*layout.PageWidth+face%3*layout.Size;
            int tileY=chart/layout.Columns*layout.PageHeight+face/3*layout.Size;
            // Different, reproducible placement on each disconnected CAD solid.
            var box=layout.Bounds[chart];
            int seed=73+face*97+(int)Math.Abs((box.X*17+box.Y*31+box.Z*47)%1009);
            int bodySeed=73+(int)Math.Abs((box.X*17+box.Y*31+box.Z*47)%1009);
            for(int y=0;y<layout.Size;y++)for(int x=0;x<layout.Size;x++)
            {
                int index=(tileY+y)*layout.Width+tileX+x;
                double u=(double)x/layout.Size,v=(double)y/layout.Size;
                double fieldX=u,fieldY=v;int fieldSeed=seed;
                if(body&&depth!=null&&!float.IsNegativeInfinity(depth[index]))
                {
                    // Use continuous part-local coordinates across cube charts.
                    // Chart cuts on smooth cylinders are not seams or wear sites.
                    double a=(x+.5-Border)/(layout.Size-2*Border),projectV=1-(y+.5-Border)/(layout.Size-2*Border);
                    if((face&1)!=0)a=1-a;
                    double d=depth[index]*((face&1)==0?1:-1);
                    double px=face<2?d:box.X+a*box.SizeX;
                    double py=face<2?box.Y+a*box.SizeY:face<4?d:box.Y+projectV*box.SizeY;
                    double pz=face<4?box.Z+projectV*box.SizeZ:d;
                    double extent=Math.Max(1e-9,Math.Max(box.SizeX,Math.Max(box.SizeY,box.SizeZ)));
                    double s=(px-box.X+.31*(pz-box.Z))/extent,t=(py-box.Y+.37*(pz-box.Z))/extent;
                    fieldX=s;fieldY=t;fieldSeed=bodySeed;
                }
                double grain=Noise(fieldX*58,fieldY*58,fieldSeed+7),patch=Noise(fieldX*13,fieldY*13,fieldSeed+29);
                double cloud=Noise(fieldX*3,fieldY*3,fieldSeed+51);
                double edge=edges?[index]??0;
                if(paint&&!body&&contact?[index]==true&&depth!=null&&!float.IsNegativeInfinity(depth[index]))
                {
                    // Curved outer rims have no hard crease. Allow contact wear
                    // on their silhouettes too, but reject holes inside the hull.
                    double silhouette=0;
                    for(int dy=-2;dy<=2;dy++)for(int dx=-2;dx<=2;dx++)
                    {
                        int sx=x+dx,sy=y+dy;
                        if(sx>=0&&sx<layout.Size&&sy>=0&&sy<layout.Size&&float.IsNegativeInfinity(depth[(tileY+sy)*layout.Width+tileX+sx]))
                            silhouette=Math.Max(silhouette,1-Smooth(.5,2.5,Math.Sqrt(dx*dx+dy*dy)));
                    }
                    edge=Math.Max(edge,silhouette*.85);
                }
                // Art-directed contact zones: flap tips/corners get most wear;
                // housings get a few separated knocks along their outer rims.
                double Spot(double a,double b,double radius)=>1-Smooth(radius*.4,radius,Math.Sqrt((u-a)*(u-a)+(v-b)*(v-b)));
                double zones=part==UrdfWorkshopPart.Flap
                    ? Math.Max(Spot(.14,.18,.27),Math.Max(Spot(.84,.22,.22),Spot(.7,.83,.19)))
                    : Math.Max(Spot(.18,.17,.21),Math.Max(Spot(.85,.76,.24),Spot(.2,.86,.15)));
                bool flap=part==UrdfWorkshopPart.Flap;
                double broken=Smooth(flap?.28:.4,flap?.56:.64,patch)*(.65+.35*grain);
                bool vent=part==UrdfWorkshopPart.Vent;
                double chips=paint?Math.Clamp(Math.Pow(edge,flap||vent?.8:1.15)*broken*(flap||vent?.7+.5*zones:.35+.65*zones)*(flap||vent?1.45:1),0,1):0;
                if(body&&paint)chips=Math.Clamp(Math.Pow(edge,.9)*Smooth(.3,.65,patch)*(.65+.35*zones)*1.7,0,1);
                // Fine grazing scratches stay beside exposed contact edges.
                double stroke=1-Smooth(.004,.012,Math.Abs(v-.23*u-.29));
                stroke*=Smooth(.2,.34,u)*(1-Smooth(.65,.8,u));
                double scratch=paint||body&&part==UrdfWorkshopPart.Shell?stroke*edge*Smooth(.48,.72,grain)*(flap?.42:.23):0;
                double finWear=vent?Math.Pow(edge,.85)*Smooth(.22,.56,patch)*(.8+.2*grain):0;
                double polished=body&&part==UrdfWorkshopPart.Shell?Math.Pow(edge,1.2)*broken*(.25+.65*zones)*.65:0;
                double worn=Math.Max(polished,Math.Max(finWear,Math.Max(chips,scratch)))*strength;
                // Recesses are darkened, never chipped. Neighboring missing
                // surface texels include bolt holes and panel/vent boundaries.
                double seam=0;
                if(depth!=null&&!float.IsNegativeInfinity(depth[index]))
                {
                    foreach(var (dx,dy) in Neighbors)
                    {
                        int sx=x+dx,sy=y+dy;
                        if(sx>=0&&sx<layout.Size&&sy>=0&&sy<layout.Size&&float.IsNegativeInfinity(depth[(tileY+sy)*layout.Width+tileX+sx]))seam=1;
                    }
                }
                double grime=(.025+.07*cloud+(body?0:.10*seam))*(part==UrdfWorkshopPart.Rubber?.3:1)*strength;
                double patina=(part switch { UrdfWorkshopPart.Shell=>.08+.13*cloud,UrdfWorkshopPart.Brass=>.06+.16*cloud,_=>0 })*strength;
                double Channel(byte original,double tarnish,double metal,double paintFloor)
                {
                    // The imported black CAD color is almost zero reflectance.
                    // Give worn paint a charcoal undercoat so reducing its
                    // mirror highlight does not turn the whole part jet black.
                    double baseColor=paint?original+(Math.Max(original,paintFloor)-original)*strength:original;
                    double value=baseColor*(1-grime);
                    value=value*(1-patina)+tarnish*patina;
                    // Dull exposed metal, rather than a white outline.
                    return Math.Clamp(value*(1-worn)+(metal+grain*16)*worn,0,255);
                }
                int offset=index*4;
                bool navy=color.B>color.R*1.8;
                // Shared midpoint between the previous housing (35/43/100)
                // and fin (65/73/118) paint, before dirt and edge chips.
                bool bareMetal=body&&part==UrdfWorkshopPart.Shell;
                pixels[offset]=(byte)Channel(color.B,part==UrdfWorkshopPart.Brass?35:100,vent?185:bareMetal?185:132,navy?109:46);
                pixels[offset+1]=(byte)Channel(color.G,part==UrdfWorkshopPart.Brass?79:110,vent?193:bareMetal?193:140,navy?58:46);
                pixels[offset+2]=(byte)Channel(color.R,part==UrdfWorkshopPart.Brass?111:117,vent?201:bareMetal?201:148,navy?50:46);
                pixels[offset+3]=255;
            }
        }
        var bitmap=BitmapSource.Create(layout.Width,layout.Height,96,96,PixelFormats.Bgr32,null,pixels,layout.Width*4);bitmap.Freeze();
        double r=0,g=0,b=0;
        for(int i=0;i<pixels.Length;i+=4){r+=LinearChannel[pixels[i+2]];g+=LinearChannel[pixels[i+1]];b+=LinearChannel[pixels[i]];}
        int count=pixels.Length/4;
        Lighting.Add(bitmap,new(Color.FromScRgb(1,(float)(r/count),(float)(g/count),(float)(b/count))));
        return bitmap;
    }
    private static readonly (int X,int Y)[] Neighbors={(-1,0),(1,0),(0,-1),(0,1)};
}
