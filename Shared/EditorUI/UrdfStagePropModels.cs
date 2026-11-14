using System.Windows.Media.Media3D;

namespace ServoAnimator;

internal static class UrdfStagePropModels
{
    internal static Model3DGroup Build(UrdfStageSettings stage,string root)
    {
        var group=new Model3DGroup();
        var models=new Dictionary<string,Model3DGroup>(StringComparer.OrdinalIgnoreCase);
        foreach(var prop in stage.Props??new())
        {
            string path=prop.Resolve(root);
            if(!models.TryGetValue(path,out var model))models[path]=model=UrdfScene.Load(path,optimizeExterior:false).RootModel;
            // Each instance needs distinct geometry objects for live VR node IDs.
            var item=new Model3DGroup();item.Children.Add(model.CloneCurrentValue());
            var transforms=new Transform3DGroup();
            transforms.Children.Add(new ScaleTransform3D(prop.Scale,prop.Scale,prop.Scale));
            transforms.Children.Add(UrdfScene.BuildTransform(new Vector3D(prop.X,prop.Y,prop.Height),
                new Vector3D((prop.Roll%360)*Math.PI/180,(prop.Pitch%360)*Math.PI/180,(prop.Yaw%360)*Math.PI/180)));
            item.Transform=transforms;group.Children.Add(item);
        }
        return group;
    }
}
