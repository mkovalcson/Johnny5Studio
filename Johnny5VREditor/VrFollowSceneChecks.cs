using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows;
namespace ServoAnimator;
internal static class VrFollowSceneChecks
{
    internal static void Run(Action<bool,string> check)
    {
        GeometryModel3D Part()=>new(new MeshGeometry3D{Positions=new Point3DCollection{new(0,0,0),new(1,0,0),new(0,1,0)},TriangleIndices=new System.Windows.Media.Int32Collection{0,1,2}},new DiffuseMaterial(Brushes.White));
        var robot=new Model3DGroup();robot.Children.Add(Part());var stage=new Model3DGroup();stage.Children.Add(Part());stage.Transform=new TranslateTransform3D(0,0,2);
        var root=new Model3DGroup();root.Children.Add(new AmbientLight(Colors.White));root.Children.Add(robot);root.Children.Add(stage);
        var capture=new VrGraphCapture();
        Johnny5.VrBridge.VrFrame Frame(bool backdrop,bool force)=>capture.Capture(root,robot,stage,new Rect3D(0,0,2,1,1,1),0,"fixture","check",[],backdrop,force);
        check(Frame(true,true).Nodes.Length==2,"Loaded backdrop is included exactly once during Follow.");
        var hidden=Frame(false,false);check(hidden.Nodes.Length==1&&hidden.Nodes[0].Robot&&hidden.Lights.Length==1,"No backdrop removes scenery while preserving the robot and scene lighting.");
        root.Children.Remove(stage);var restored=Frame(true,true);
        check(restored.Nodes.Length==2&&restored.Nodes.Any(n=>!n.Robot&&n.World.M43==2)&&restored.StageFloor==2,"Follow retains a loaded URDF backdrop even when desktop presentation hides it, with its floor alignment preserved.");
        check(Frame(false,false).Nodes.Length==1&&Frame(true,true).Nodes.Length==2,"Backdrop can be switched off and restored without reloading the model.");
        Frame(true,true);check(capture.CachedBranches>0&&capture.VisitedBranches==0,"Unchanged scene branches reuse their captured contents without walking geometry.");
        robot.Transform=new TranslateTransform3D(3,0,0);var moved=Frame(true,true);
        check(moved.Nodes.Single(n=>n.Robot).World.M41==3&&capture.CachedBranches>0,"A moving robot updates its transforms while unchanged scenery stays cached.");
        var robotPart=(GeometryModel3D)robot.Children[0];robotPart.Material=new DiffuseMaterial(Brushes.Red);
        check(Frame(true,true).Materials.Any(m=>m.Diffuse.X>0.9f&&m.Diffuse.Y<.1f),"Material replacement invalidates cached branches.");
        var brush=new SolidColorBrush(Colors.Blue);robotPart.Material=new DiffuseMaterial(brush);Frame(true,true);brush.Color=Colors.Green;
        check(Frame(true,true).Materials.Any(m=>m.Diffuse.Y>.4f&&m.Diffuse.Z<.1f),"Nested brush edits invalidate material snapshots.");
        var added=Part();robot.Children.Add(added);check(Frame(true,true).Nodes.Count(n=>n.Robot)==2,"Adding a part invalidates cached topology.");
        robot.Children.Remove(added);check(Frame(true,true).Nodes.Count(n=>n.Robot)==1,"Removing a part invalidates cached topology.");
        var mesh=(MeshGeometry3D)robotPart.Geometry;var beforeMesh=Frame(true,true).Nodes.Single(n=>n.Robot).Mesh;
        mesh.Positions[1]=new(2,0,0);var revised=Frame(true,true);
        check(revised.Nodes.Single(n=>n.Robot).Mesh!=beforeMesh&&revised.Meshes.Any(m=>m.Positions.Contains(2f)),"Mutable geometry revisions are captured rather than skipped.");

    }
}
