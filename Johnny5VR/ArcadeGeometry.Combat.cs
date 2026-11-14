using System.Numerics;
namespace Johnny5VR;
internal static class ArcadeCombatGeometry
{
    internal static Vertex[] Build(ArcadeGame game,ArcadeTeleport teleport)
    {
        var vertices=new List<Vertex>();
        void Ring(Vector3 center,float radius,Vector4 color)
        {
            for(int i=0;i<36;i++)
            {
                float angle=(i+.5f)*MathF.Tau/36;var matrix=Matrix4x4.CreateRotationY(-angle)*Matrix4x4.CreateTranslation(center+new Vector3(MathF.Cos(angle)*radius,.012f,MathF.Sin(angle)*radius));
                ArcadeGeometry.Add(vertices,Shapes.Box(new(.035f,.025f,radius*MathF.Tau/36*1.08f)),color,matrix);
            }
        }
        foreach(var pad in teleport.Pads)Ring(pad.Centre,pad.Radius,teleport.Selected==pad?new(.15f,1,.3f,1):new(.12f,.65f,.9f,1));
        if(teleport.Landing is {} end)Ring(end,.3f,teleport.Selected!=null?new(.15f,1,.3f,1):new(1,.15f,.1f,1));
        foreach(var enemy in game.Targets.Where(t=>t.Alive&&t.WarningUntil>game.Time))
            ArcadeGeometry.Add(vertices,ArcadeGeometry.Sphere(10,6),new(.45f,.6f,1,1),Matrix4x4.CreateScale(.10f+.045f*MathF.Sin((float)game.Time*30))*Matrix4x4.CreateTranslation(enemy.Position+Vector3.UnitY*1.48f));
        if(game.Protected&&game.Phase==ArcadePhase.Playing)Ring(game.Position,.75f,new(.1f,.75f,1,1));
        return vertices.ToArray();
    }
}
