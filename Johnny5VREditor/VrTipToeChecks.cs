using System.IO;
using System.Windows.Media.Media3D;
using Johnny5.VrBridge;

namespace ServoAnimator;

// TipToe through the real editor path: the VR pose handle, the URDF scene's virtual joint, the
// streamed VR scene, and a picture of the editor view at rest and fully on tiptoe.
public partial class MainWindow
{
    private void RunVrTipToeChecks(Action<bool,string> check,RobotHeadView source,string folder,Func<string,float,VrPoseRequest> set)
    {
        var scene=source.CurrentModelScene;
        check(scene!=null&&scene.HasTipToe&&scene.HasJoint(UrdfScene.TipToeJoint),"The full-body model supports the TipToe virtual joint.");
        var handle=CaptureVrPoseState(source).Handles.FirstOrDefault(h=>h.Key=="Servo:B_TipToe");
        check(handle!=null&&handle.Minimum==-100&&handle.Maximum==100&&!handle.Ganged,"TipToe has its own VR pose handle.");
        check(scene.TryTransformLinkPoint("body_pelvis_drum_link",new Point3D(),out var drum0)&&scene.TryTransformLinkPoint("body_track_front_roller_left_link",new Point3D(),out var front0)
            &&scene.TryTransformLinkPoint("body_caster_axle_link",new Point3D(),out var caster0)&&scene.TryTransformLinkPoint("body_shroud_strip_line_left_0_link",new Point3D(),out var strip0),"TipToe check finds the drum, front roller, caster axle and shroud strip.");
        DumpScene(source.CaptureVrScene("rest"),Path.Combine(folder,"tiptoe-scene-rest.bin"));
        set("Servo:B_TipToe",100);
        var motion=scene.TipToeMotion;
        check(Math.Abs(scene.TipToeRadians-TipToeGeometry.MaximumRadians)<1e-9&&Math.Abs(source.BodyPosition(ServoNames.B_TipToe)-100)<.01,"Command 100 rotates the tracks the full 61.3 degrees.");
        double Deg(float r)=>r*180/Math.PI;
        check(motion.Lift>.18f&&motion.Lift<.215f&&Math.Abs(Deg(motion.UpperBrace)-41.7)<.05&&Deg(motion.LowerBrace)>35&&Deg(motion.LowerBrace)<39&&Deg(motion.Pitch)>4.5&&Deg(motion.Pitch)<7,$"Fully on tiptoe the body rises {motion.Lift*100:F1} cm, the braces swing {Deg(motion.UpperBrace):F1}° / {Deg(motion.LowerBrace):F1}° and the robot tips back {Deg(motion.Pitch):F1}°.");
        scene.TryTransformLinkPoint("body_pelvis_drum_link",new Point3D(),out var drum1);scene.TryTransformLinkPoint("body_track_front_roller_left_link",new Point3D(),out var front1);
        scene.TryTransformLinkPoint("body_caster_axle_link",new Point3D(),out var caster1);scene.TryTransformLinkPoint("body_shroud_strip_line_left_0_link",new Point3D(),out var strip1);
        check(Math.Abs(drum1.Z-drum0.Z-motion.Lift)<.001&&Math.Abs(drum1.X-drum0.X-motion.Forward)<.001&&motion.Forward>.26f&&motion.Forward<.31f,$"The body rises {motion.Lift*100:F1} cm and moves forward {motion.Forward*100:F1} cm.");
        check(Math.Abs(front1.Z-front0.Z)<.002&&Math.Abs(front1.X-front0.X)<.002,"The front rollers stay planted where they were on the floor.");
        check(caster1.X>caster0.X+.25&&Math.Abs(caster1.Z-caster0.Z)<.012,$"The caster wheel rolls in {(caster1.X-caster0.X)*100:F1} cm from behind and stays on the floor (axle {(caster1.Z-caster0.Z)*1000:F0} mm from its rest height).");
        check(scene.TryTransformLinkPoint("body_caster_head_link",new Point3D(),out var brace1)&&scene.TryTransformLinkPoint("body_caster_collar_link",new Point3D(),out var collar1)&&collar1.X>caster0.X&&brace1.Z<drum1.Z,"The one-piece upper brace and the column swing down with the four-bar.");
        var expectedStrip=strip0-drum0;var a=TipToeGeometry.MaximumRadians-motion.Pitch;
        var rotated=new Vector3D(expectedStrip.X*Math.Cos(a)+expectedStrip.Z*Math.Sin(a),expectedStrip.Y,-expectedStrip.X*Math.Sin(a)+expectedStrip.Z*Math.Cos(a));
        check((strip1-drum1-rotated).Length<.002,"The track shroud strip lines rotate with the tracks (and the robot's tip-back).");
        var frame=source.CaptureVrScene("tiptoe");
        check(frame.Nodes.Length>0,"The streamed VR scene includes the tiptoe pose.");
        DumpScene(frame,Path.Combine(folder,"tiptoe-scene-full.bin"));
        set("Servo:B_TipToe",-100);
        check(scene.TipToeRadians==0,"Negative commands keep the tracks flat (0 is the rest position).");
        set("Servo:B_TipToe",50);
        check(Math.Abs(scene.TipToeRadians-TipToeGeometry.MaximumRadians/2)<1e-9,"Command 50 tilts the tracks 30.6 degrees.");
        set("Servo:B_TipToe",0);
        scene.TryTransformLinkPoint("body_pelvis_drum_link",new Point3D(),out var drum2);
        check((drum2-drum0).Length<1e-6,"Returning to 0 restores the rest pose exactly.");
    }
    // World-space triangles with each part's colour, for an offline picture of exactly what the editor streams.
    private static void DumpScene(VrFrame frame,string path)
    {
        var meshes=frame.Meshes.ToDictionary(m=>m.Id);var materials=frame.Materials.ToDictionary(m=>m.Id);
        using var w=new BinaryWriter(File.Create(path));
        foreach(var node in frame.Nodes)
        {
            if(!node.Robot||!meshes.TryGetValue(node.Mesh,out var mesh)||!materials.TryGetValue(node.Material,out var material))continue;
            w.Write(material.Diffuse.X);w.Write(material.Diffuse.Y);w.Write(material.Diffuse.Z);w.Write(mesh.Indices.Length/3);
            foreach(int i in mesh.Indices)
            {
                var v=System.Numerics.Vector3.Transform(new System.Numerics.Vector3(mesh.Positions[i*3],mesh.Positions[i*3+1],mesh.Positions[i*3+2]),node.World);
                w.Write(v.X);w.Write(v.Y);w.Write(v.Z);
            }
        }
    }
}
